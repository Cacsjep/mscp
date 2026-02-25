using System;
using System.Threading;
using RTMPStreamer.Rtmp;
using VideoOS.Platform;

namespace RTMPStreamer.Streaming
{
    /// <summary>
    /// Orchestrates streaming from one camera to one RTMP destination.
    /// Pipeline: CameraFrameSource → FlvMuxer → RtmpPublisher
    /// </summary>
    internal class StreamSession : IDisposable
    {
        private CameraFrameSource _frameSource;
        private FlvMuxer _muxer;
        private RtmpPublisher _publisher;

        private readonly Item _cameraItem;
        private readonly string _rtmpUrl;
        private readonly string _sessionId;
        private readonly bool _allowUntrustedCerts;

        private Thread _reconnectThread;
        private CancellationTokenSource _cts;
        private volatile bool _running;
        private DateTime _startTime;
        private DateTime _streamEpoch;
        private bool _streamEpochSet;

        // Silent audio
        private bool _audioHeaderSent;
        private byte[] _silentAudioFrame;
        private uint _nextAudioTimestamp;

        // Stats
        private long _framesSent;
        private long _bytesSent;
        private long _keyFramesSent;
        private string _lastError;

        public string SessionId => _sessionId;
        public string CameraName => _cameraItem?.Name ?? "Unknown";
        public Guid CameraId => _cameraItem?.FQID?.ObjectId ?? Guid.Empty;
        public string RtmpUrl => _rtmpUrl;
        public bool IsRunning => _running;
        public long FramesSent => _framesSent;
        public long BytesSent => _bytesSent;
        public long KeyFramesSent => _keyFramesSent;
        public string LastError => _lastError;
        public TimeSpan Uptime => _running ? DateTime.UtcNow - _startTime : TimeSpan.Zero;

        public StreamSession(Item cameraItem, string rtmpUrl, bool allowUntrustedCerts = false)
        {
            _cameraItem = cameraItem ?? throw new ArgumentNullException(nameof(cameraItem));
            _rtmpUrl = rtmpUrl ?? throw new ArgumentNullException(nameof(rtmpUrl));
            _allowUntrustedCerts = allowUntrustedCerts;
            _sessionId = Guid.NewGuid().ToString("N").Substring(0, 8);
        }

        /// <summary>
        /// Start the streaming session. Connects to camera and RTMP server.
        /// </summary>
        public void Start()
        {
            if (_running) return;

            _cts = new CancellationTokenSource();
            _running = true;
            _startTime = DateTime.UtcNow;
            _framesSent = 0;
            _bytesSent = 0;
            _keyFramesSent = 0;
            _lastError = null;
            _streamEpochSet = false;
            _audioHeaderSent = false;

            _reconnectThread = new Thread(RunLoop)
            {
                IsBackground = true,
                Name = $"RTMP-Session-{_sessionId}"
            };
            _reconnectThread.Start();
        }

        /// <summary>
        /// Stop the streaming session.
        /// </summary>
        public void Stop()
        {
            _running = false;
            _cts?.Cancel();

            Cleanup();

            if (_reconnectThread != null && _reconnectThread.IsAlive)
                _reconnectThread.Join(5000);

            _reconnectThread = null;
        }

        public void Dispose()
        {
            Stop();
            _cts?.Dispose();
        }

        private void RunLoop()
        {
            while (_running)
            {
                try
                {
                    EmitStatus($"Connecting to {_rtmpUrl}");
                    Log($"Connecting to RTMP server: {_rtmpUrl}");

                    // Initialize components
                    _muxer = new FlvMuxer();
                    _publisher = new RtmpPublisher();
                    _frameSource = new CameraFrameSource();

                    // Connect to RTMP server
                    _publisher.Connect(_rtmpUrl, _allowUntrustedCerts);
                    Log("RTMP publish started, initializing camera");

                    // Start camera frame source
                    EmitStatus("Initializing camera");
                    _frameSource.FrameReceived += OnFrameReceived;
                    _frameSource.Error += OnFrameError;
                    _frameSource.Init(_cameraItem);
                    _frameSource.Start();

                    Log($"Camera initialized: {_cameraItem.Name}");
                    EmitStatus("Waiting for first frame");

                    // Wait until cancelled (by Stop or frame send error)
                    _cts.Token.WaitHandle.WaitOne();
                }
                catch (Exception ex)
                {
                    _lastError = ex.Message;
                    var errorMsg = ClassifyError(ex);
                    EmitStatus($"Error: {errorMsg}");
                    LogError($"Error: {ex.Message}");
                }

                Cleanup();

                if (!_running) break;

                // Reset CTS for reconnect wait and next iteration
                EmitStatus("Reconnecting in 5s");
                Log("Reconnecting in 5 seconds...");
                try { _cts?.Dispose(); } catch { }
                _cts = new CancellationTokenSource();
                _cts.Token.WaitHandle.WaitOne(5000);

                _streamEpochSet = false;
                _audioHeaderSent = false;
            }

            EmitStatus("Stopped");
        }

        private void OnFrameReceived(byte[] annexBData, bool isKeyFrame, DateTime timestamp)
        {
            try
            {
                if (!_running || _publisher == null || !_publisher.IsPublishing)
                    return;

                // Calculate RTMP timestamp (ms since stream start)
                if (!_streamEpochSet)
                {
                    _streamEpoch = timestamp;
                    _streamEpochSet = true;
                }

                uint rtmpTimestamp = (uint)(timestamp - _streamEpoch).TotalMilliseconds;
                if (rtmpTimestamp < 0) rtmpTimestamp = 0;

                // Mux the frame
                byte[] flvPayload = _muxer.MuxFrame(annexBData, isKeyFrame, out byte[] sequenceHeader);

                // Send sequence header first if we just got SPS/PPS
                if (sequenceHeader != null)
                {
                    Log($"Sending AVC sequence header ({sequenceHeader.Length} bytes)");
                    _publisher.SendVideoData(sequenceHeader, rtmpTimestamp);
                    _bytesSent += sequenceHeader.Length;

                    // Send AAC audio sequence header right after video sequence header
                    if (!_audioHeaderSent)
                    {
                        var audioSeqHeader = _muxer.BuildAudioSequenceHeader();
                        _publisher.SendAudioData(audioSeqHeader, rtmpTimestamp);
                        _silentAudioFrame = _muxer.BuildSilentAudioFrame();
                        _audioHeaderSent = true;
                        _nextAudioTimestamp = rtmpTimestamp;
                        Log("Sent AAC audio sequence header (silent track)");
                    }
                }

                // Interleave silent audio frames to keep audio timeline in sync
                if (_audioHeaderSent)
                {
                    while (_nextAudioTimestamp <= rtmpTimestamp)
                    {
                        _publisher.SendAudioData(_silentAudioFrame, _nextAudioTimestamp);
                        _nextAudioTimestamp += 23; // ~23ms per AAC frame (1024 samples / 44100 Hz)
                    }
                }

                // Send the video frame
                if (flvPayload != null)
                {
                    _publisher.SendVideoData(flvPayload, rtmpTimestamp);
                    _framesSent++;
                    _bytesSent += flvPayload.Length;
                    if (isKeyFrame) _keyFramesSent++;

                    // Emit "Streaming" status on first frame
                    if (_framesSent == 1)
                    {
                        EmitStatus("Streaming");
                        Log("First frame sent successfully, stream is live");
                    }

                }
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
                var errorMsg = ClassifyError(ex);
                EmitStatus($"Error: {errorMsg}");
                LogError($"Send error: {ex.Message}");
                // Trigger reconnect by cancelling
                try { _cts?.Cancel(); } catch { }
            }
        }

        private void OnFrameError(string message)
        {
            _lastError = message;
            LogError($"Frame source error: {message}");

            // Emit codec errors as status so they show in Management Client
            if (message.Contains("codec"))
                EmitStatus($"Codec error: {message}");
            else
                EmitStatus($"Error: {message}");
        }

        private void Cleanup()
        {
            try
            {
                if (_frameSource != null)
                {
                    _frameSource.FrameReceived -= OnFrameReceived;
                    _frameSource.Error -= OnFrameError;
                    _frameSource.Dispose();
                    _frameSource = null;
                }
            }
            catch { }

            try { _publisher?.Dispose(); _publisher = null; } catch { }
            _muxer = null;
        }

        private void Log(string message)
        {
            PluginLog.Info($"[Session:{_sessionId}] {message}");
        }

        private void LogError(string message)
        {
            PluginLog.Error($"[Session:{_sessionId}] {message}");
        }

        /// <summary>
        /// Emit a STATUS protocol line on stderr for BackgroundPlugin to parse.
        /// This drives the item status display in Management Client.
        /// </summary>
        private void EmitStatus(string status)
        {
            try { Console.Error.WriteLine($"STATUS {status}"); } catch { }
        }

        /// <summary>
        /// Classify an exception into a user-friendly status message.
        /// </summary>
        private static string ClassifyError(Exception ex)
        {
            var msg = ex.Message;

            if (ex is System.Net.Sockets.SocketException sockEx)
            {
                switch (sockEx.SocketErrorCode)
                {
                    case System.Net.Sockets.SocketError.ConnectionRefused:
                        return "Connection refused by RTMP server";
                    case System.Net.Sockets.SocketError.HostNotFound:
                        return "RTMP server hostname not found";
                    case System.Net.Sockets.SocketError.TimedOut:
                        return "Connection to RTMP server timed out";
                    case System.Net.Sockets.SocketError.NetworkUnreachable:
                        return "Network unreachable";
                    default:
                        return $"Network error: {sockEx.SocketErrorCode}";
                }
            }

            if (ex is System.IO.IOException)
            {
                if (msg.Contains("closed") || msg.Contains("reset"))
                    return "Connection closed by RTMP server";
                return $"Connection error: {msg}";
            }

            if (ex is System.Security.Authentication.AuthenticationException)
                return $"TLS/SSL error: {msg}";

            if (msg.Contains("RTMP error") || msg.Contains("RTMP publish"))
                return msg;

            return msg;
        }
    }
}
