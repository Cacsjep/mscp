using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using VideoOS.Platform.DriverFramework.Utilities;

namespace RTMPDriver.Rtmp
{
    /// <summary>
    /// TCP listener that accepts RTMP push connections and routes them
    /// to per-stream-path buffers. One instance per hardware.
    /// </summary>
    internal class RtmpServer
    {
        [DllImport("winmm.dll")]
        private static extern uint timeBeginPeriod(uint period);
        [DllImport("winmm.dll")]
        private static extern uint timeEndPeriod(uint period);

        private TcpListener _listener;
        private Thread _acceptThread;
        private CancellationTokenSource _cts;
        private readonly ConcurrentDictionary<string, RtmpStreamBuffer> _buffers = new ConcurrentDictionary<string, RtmpStreamBuffer>(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<int, Thread> _clientThreads = new ConcurrentDictionary<int, Thread>();
        private int _activeClientCount;
        private int _port;
        private readonly ConcurrentDictionary<string, int> _perIpConnections = new ConcurrentDictionary<string, int>();

        // TLS
        private X509Certificate2 _tlsCertificate;
        internal bool TlsEnabled { get; private set; }

        // Rate limiting
        internal bool RateLimitEnabled { get; set; }
        internal int RateLimitMaxRequests { get; set; } = 10;
        internal int MaxConnections { get; set; } = Constants.DefaultMaxConnections;
        private readonly ConcurrentDictionary<string, RateLimitEntry> _rateLimits = new ConcurrentDictionary<string, RateLimitEntry>();
        private DateTime _lastRateLimitCleanup = DateTime.UtcNow;

        private class RateLimitEntry
        {
            private readonly Queue<long> _timestamps = new Queue<long>();
            private readonly object _lock = new object();

            public bool IsAllowed(int maxRequests)
            {
                long now = DateTime.UtcNow.Ticks;
                long windowStart = now - TimeSpan.TicksPerSecond;
                lock (_lock)
                {
                    while (_timestamps.Count > 0 && _timestamps.Peek() < windowStart)
                        _timestamps.Dequeue();

                    if (_timestamps.Count >= maxRequests)
                        return false;

                    _timestamps.Enqueue(now);
                    return true;
                }
            }

            public bool IsStale()
            {
                long cutoff = DateTime.UtcNow.Ticks - TimeSpan.TicksPerSecond * 60;
                lock (_lock)
                {
                    return _timestamps.Count == 0 || _timestamps.Peek() < cutoff;
                }
            }
        }

        /// <summary>
        /// Load a TLS certificate for RTMPS support.
        /// Looks for rtmp.pfx in the driver folder.
        /// Throws if the certificate cannot be loaded so the caller can fail the connection.
        /// </summary>
        internal void SetTlsCertificate(string certPassword)
        {
            string driverDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            string certPath = Path.Combine(driverDir, Constants.TlsCertificateFileName);

            if (!File.Exists(certPath))
                throw new FileNotFoundException($"TLS certificate file not found: {certPath}. Place a '{Constants.TlsCertificateFileName}' file in the driver folder.");

            try
            {
                _tlsCertificate = new X509Certificate2(certPath, certPassword);
            }
            catch (System.Security.Cryptography.CryptographicException)
            {
                throw new InvalidOperationException("TLS certificate password is incorrect.");
            }
            TlsEnabled = true;
            Toolbox.Log.Trace("RtmpServer: TLS certificate loaded from {0}, subject={1} expires={2}", certPath, _tlsCertificate.Subject, _tlsCertificate.NotAfter);
        }

        /// <summary>
        /// Start listening for RTMP connections on the specified port.
        /// </summary>
        public void Start(int port)
        {
            _port = port;
            _cts = new CancellationTokenSource();

            _listener = new TcpListener(IPAddress.Any, port);
            _listener.ExclusiveAddressUse = true;
            _listener.Start();

            _acceptThread = new Thread(AcceptLoop)
            {
                IsBackground = true,
                Name = "RTMP-Accept"
            };
            _acceptThread.Start();

            // Set Windows timer resolution to 1ms for accurate Thread.Sleep pacing
            timeBeginPeriod(1);

            Toolbox.Log.Trace("RtmpServer: Started on port {0}, maxConnections={1}, tls={2}", port, MaxConnections, TlsEnabled);
        }

        /// <summary>
        /// Stop the server and disconnect all clients.
        /// </summary>
        public void Stop()
        {
            if (_cts == null) return;

            Toolbox.Log.Trace("RtmpServer: Stopping, {0} active clients, {1} stream buffers", _activeClientCount, _buffers.Count);
            _cts.Cancel();

            try { _listener.Stop(); } catch { }

            // Wait for accept thread
            if (_acceptThread != null && _acceptThread.IsAlive)
                _acceptThread.Join(3000);

            // Wait for client threads
            foreach (var kvp in _clientThreads)
            {
                if (kvp.Value.IsAlive)
                    kvp.Value.Join(2000);
            }

            // Mark all buffers offline
            foreach (var buffer in _buffers.Values)
            {
                buffer.SetOffline();
            }

            // Restore Windows timer resolution
            timeEndPeriod(1);

            Toolbox.Log.Trace("RtmpServer: Stopped");
        }

        /// <summary>
        /// Get or create a stream buffer for the given path.
        /// </summary>
        public RtmpStreamBuffer GetOrCreateBuffer(string streamPath)
        {
            string normalized = NormalizePath(streamPath);
            return _buffers.GetOrAdd(normalized, key => new RtmpStreamBuffer(key));
        }

        /// <summary>
        /// Get a stream buffer by path. Returns null if not found.
        /// </summary>
        public RtmpStreamBuffer GetBuffer(string streamPath)
        {
            string normalized = NormalizePath(streamPath);
            _buffers.TryGetValue(normalized, out var buffer);
            return buffer;
        }

        /// <summary>
        /// Remove a stream buffer for the given path. Sets it offline first
        /// so any active publisher is disconnected.
        /// Called when a Milestone session closes (device disabled/removed).
        /// </summary>
        public void RemoveBuffer(string streamPath)
        {
            string normalized = NormalizePath(streamPath);
            if (_buffers.TryRemove(normalized, out var buffer))
            {
                buffer.SetOffline();
                Toolbox.Log.Trace("RtmpServer: Removed buffer for path '{0}'", normalized);
            }
        }

        private static string NormalizePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return "/";
            path = path.Trim().ToLowerInvariant();
            if (!path.StartsWith("/")) path = "/" + path;
            return path;
        }

        private void AcceptLoop()
        {
            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    if (_listener.Pending())
                    {
                        TcpClient client = _listener.AcceptTcpClient();
                        client.NoDelay = true;
                        client.ReceiveTimeout = 30000;
                        client.SendTimeout = 10000;

                        var remoteIpEp = (IPEndPoint)client.Client.RemoteEndPoint;
                        string remoteEp = remoteIpEp.ToString();
                        string remoteIp = remoteIpEp.Address.ToString();

                        // Enforce max concurrent connections
                        if (_activeClientCount >= MaxConnections)
                        {
                            Toolbox.Log.Trace("RtmpServer: Max connections ({0}) reached, rejecting {1}", MaxConnections, remoteIp);
                            try { client.Close(); } catch { }
                            continue;
                        }

                        // Enforce per-IP connection limit
                        _perIpConnections.TryGetValue(remoteIp, out int ipConnCount);
                        if (ipConnCount >= Constants.MaxConnectionsPerIp)
                        {
                            Toolbox.Log.Trace("RtmpServer: Per-IP limit ({0}) reached for {1}, rejecting", Constants.MaxConnectionsPerIp, remoteIp);
                            try { client.Close(); } catch { }
                            continue;
                        }

                        if (RateLimitEnabled)
                        {
                            var entry = _rateLimits.GetOrAdd(remoteIp, _ => new RateLimitEntry());
                            if (!entry.IsAllowed(RateLimitMaxRequests))
                            {
                                Toolbox.Log.Trace("RtmpServer: Rate limited connection from {0}", remoteIp);
                                try { client.Close(); } catch { }
                                continue;
                            }
                        }

                        Toolbox.Log.Trace("RtmpServer: New connection from {0} ({1}/{2})", remoteEp, _activeClientCount + 1, MaxConnections);

                        var clientThread = new Thread(() => HandleClient(client, remoteIp))
                        {
                            IsBackground = true,
                            Name = "RTMP-" + remoteEp
                        };

                        _clientThreads.TryAdd(clientThread.ManagedThreadId, clientThread);
                        clientThread.Start();
                    }
                    else
                    {
                        Thread.Sleep(50);

                        // Clean up stale rate limit entries every 60 seconds
                        if ((DateTime.UtcNow - _lastRateLimitCleanup).TotalSeconds >= 60)
                        {
                            _lastRateLimitCleanup = DateTime.UtcNow;
                            foreach (var kvp in _rateLimits)
                            {
                                if (kvp.Value.IsStale())
                                    _rateLimits.TryRemove(kvp.Key, out _);
                            }
                        }
                    }
                }
                catch (SocketException) when (_cts.IsCancellationRequested)
                {
                    break;
                }
                catch (ObjectDisposedException) when (_cts.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    if (!_cts.IsCancellationRequested)
                        Toolbox.Log.LogError("RtmpServer", "Accept error: {0}", ex.Message);
                }
            }
        }

        private void HandleClient(TcpClient tcpClient, string remoteIp)
        {
            Interlocked.Increment(ref _activeClientCount);
            _perIpConnections.AddOrUpdate(remoteIp, 1, (_, c) => c + 1);
            try
            {
                Stream stream;
                if (TlsEnabled && _tlsCertificate != null)
                {
                    var sslStream = new SslStream(tcpClient.GetStream(), false);
                    sslStream.AuthenticateAsServer(
                        _tlsCertificate,
                        clientCertificateRequired: false,
                        enabledSslProtocols: SslProtocols.Tls12,
                        checkCertificateRevocation: false);
                    Toolbox.Log.Trace("RtmpServer: TLS handshake completed with {0}, protocol={1}", remoteIp, sslStream.SslProtocol);
                    stream = sslStream;
                }
                else
                {
                    stream = tcpClient.GetStream();
                }

                var rtmpClient = new RtmpClient(tcpClient, stream, GetBuffer, _cts.Token);
                rtmpClient.Run();
            }
            catch (Exception ex)
            {
                Toolbox.Log.LogError("RtmpServer", "Client handler error: {0}", ex.Message);
            }
            finally
            {
                _clientThreads.TryRemove(Thread.CurrentThread.ManagedThreadId, out _);
                _perIpConnections.AddOrUpdate(remoteIp, 0, (_, c) => c - 1);
                Interlocked.Decrement(ref _activeClientCount);
            }
        }
    }
}
