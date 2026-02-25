using System;
using System.Collections.Generic;
using System.Linq;
using System.Security;
using RTMPDriver.Rtmp;
using VideoOS.Platform.DriverFramework.Data.Settings;
using VideoOS.Platform.DriverFramework.Managers;
using VideoOS.Platform.DriverFramework.Utilities;

namespace RTMPDriver
{
    /// <summary>
    /// Manages the RTMP server lifecycle. Starts on Connect, stops on Close.
    /// </summary>
    public class RTMPDriverConnectionManager : ConnectionManager
    {
        private bool _connected;
        private RtmpServer _rtmpServer;
        private int _currentPort;
        private bool _rateLimitEnabled;
        private int _rateLimitMaxRequests = 10;
        private int _maxConnections = Constants.DefaultMaxConnections;
        private bool _tlsEnabled;
        private string _tlsCertificatePassword;
        private readonly object _restartLock = new object();

        internal RtmpServer RtmpServer => _rtmpServer;
        internal bool IsTlsEnabled => _tlsEnabled;
        internal string ServerError { get; private set; }
        public Uri HardwareUri { get; private set; }

        public RTMPDriverConnectionManager(RTMPDriverContainer container) : base(container)
        {
        }

        public override void Connect(Uri uri, string userName, SecureString password, ICollection<HardwareSetting> hardwareSettings)
        {
            if (_connected)
                return;

            HardwareUri = uri;

            // ServerPort is initialized from the URI port via BuildHardwareSettings().
            // The user can change it later in the Management Client.
            int port = uri?.Port > 0 ? uri.Port : 8783;
            var portSetting = hardwareSettings?.FirstOrDefault(s => s.Key == Constants.ServerPort);
            if (portSetting != null && int.TryParse(portSetting.Value, out int parsedPort) && parsedPort > 0)
            {
                port = parsedPort;
            }

            bool rateLimitEnabled = false;
            var rateLimitSetting = hardwareSettings?.FirstOrDefault(s => s.Key == Constants.RateLimitEnabled);
            if (rateLimitSetting != null)
                bool.TryParse(rateLimitSetting.Value, out rateLimitEnabled);

            int rateLimitMax = 10;
            var rateLimitMaxSetting = hardwareSettings?.FirstOrDefault(s => s.Key == Constants.RateLimitMaxRequests);
            if (rateLimitMaxSetting != null)
                int.TryParse(rateLimitMaxSetting.Value, out rateLimitMax);

            int maxConnections = Constants.DefaultMaxConnections;
            var maxConnSetting = hardwareSettings?.FirstOrDefault(s => s.Key == Constants.MaxConnections);
            if (maxConnSetting != null && int.TryParse(maxConnSetting.Value, out int parsedMaxConn) && parsedMaxConn > 0)
                maxConnections = parsedMaxConn;

            bool tlsEnabled = false;
            var tlsSetting = hardwareSettings?.FirstOrDefault(s => s.Key == Constants.EnableTls);
            if (tlsSetting != null)
                bool.TryParse(tlsSetting.Value, out tlsEnabled);

            string certPassword = "";
            var certPwdSetting = hardwareSettings?.FirstOrDefault(s => s.Key == Constants.TlsCertificatePassword);
            if (certPwdSetting != null)
                certPassword = certPwdSetting.Value ?? "";

            Toolbox.Log.Trace("ConnectionManager: Connecting, RTMP server port={0} rateLimit={1} maxReq={2} maxConn={3} tls={4}", port, rateLimitEnabled, rateLimitMax, maxConnections, tlsEnabled);
            _rtmpServer = new RtmpServer();
            try
            {
                _rtmpServer.RateLimitEnabled = rateLimitEnabled;
                _rtmpServer.RateLimitMaxRequests = rateLimitMax;
                _rtmpServer.MaxConnections = maxConnections;
                _rateLimitEnabled = rateLimitEnabled;
                _rateLimitMaxRequests = rateLimitMax;
                _maxConnections = maxConnections;
                _tlsEnabled = tlsEnabled;
                _tlsCertificatePassword = certPassword;
                if (tlsEnabled)
                    _rtmpServer.SetTlsCertificate(certPassword);
                _rtmpServer.Start(port);
                _currentPort = port;
                _connected = true;
                ServerError = null;
                Toolbox.Log.Trace("ConnectionManager: Connected, RTMP server running on port {0}", _currentPort);
            }
            catch (Exception ex)
            {
                Toolbox.Log.LogError("RTMPDriverConnectionManager", "Failed to start RTMP server on port {0}: {1}", port, ex.Message);
                _rtmpServer = null;
                _currentPort = port;
                _connected = true;
                ServerError = ex.Message;
            }
        }

        internal void RestartOnPort(int newPort)
        {
            lock (_restartLock)
            {
                if (newPort == _currentPort)
                    return;

                Toolbox.Log.Trace("ConnectionManager: Port changed {0} -> {1}, restarting RTMP server", _currentPort, newPort);

                if (_rtmpServer != null)
                {
                    _rtmpServer.Stop();
                    _rtmpServer = null;
                }

                _rtmpServer = new RtmpServer();
                try
                {
                    _rtmpServer.RateLimitEnabled = _rateLimitEnabled;
                    _rtmpServer.RateLimitMaxRequests = _rateLimitMaxRequests;
                    _rtmpServer.MaxConnections = _maxConnections;
                    if (_tlsEnabled)
                        _rtmpServer.SetTlsCertificate(_tlsCertificatePassword);
                    _rtmpServer.Start(newPort);
                    _currentPort = newPort;
                    ServerError = null;
                    Toolbox.Log.Trace("ConnectionManager: RTMP server restarted on port {0}, tls={1}", _currentPort, _tlsEnabled);
                }
                catch (Exception ex)
                {
                    Toolbox.Log.LogError("RTMPDriverConnectionManager", "Failed to restart RTMP server on port {0}: {1}", newPort, ex.Message);
                    _rtmpServer = null;
                    _currentPort = newPort;
                    ServerError = ex.Message;
                }
            }
        }

        internal void UpdateRateLimiter(bool enabled, int maxRequests)
        {
            _rateLimitEnabled = enabled;
            _rateLimitMaxRequests = maxRequests;
            if (_rtmpServer != null)
            {
                _rtmpServer.RateLimitEnabled = enabled;
                _rtmpServer.RateLimitMaxRequests = maxRequests;
                Toolbox.Log.Trace("ConnectionManager: Rate limiter updated enabled={0} maxReq={1}", enabled, maxRequests);
            }
        }

        internal void UpdateTls(bool enabled, string certPassword)
        {
            if (enabled == _tlsEnabled && certPassword == _tlsCertificatePassword)
                return;

            _tlsEnabled = enabled;
            _tlsCertificatePassword = certPassword;

            // TLS changes require a server restart since the listener behavior changes
            lock (_restartLock)
            {
                Toolbox.Log.Trace("ConnectionManager: TLS settings changed (enabled={0}), restarting RTMP server", enabled);

                if (_rtmpServer != null)
                {
                    _rtmpServer.Stop();
                    _rtmpServer = null;
                }

                _rtmpServer = new RtmpServer();
                try
                {
                    _rtmpServer.RateLimitEnabled = _rateLimitEnabled;
                    _rtmpServer.RateLimitMaxRequests = _rateLimitMaxRequests;
                    _rtmpServer.MaxConnections = _maxConnections;
                    if (_tlsEnabled)
                        _rtmpServer.SetTlsCertificate(_tlsCertificatePassword);
                    _rtmpServer.Start(_currentPort);
                    ServerError = null;
                    Toolbox.Log.Trace("ConnectionManager: RTMP server restarted on port {0}, tls={1}", _currentPort, _tlsEnabled);
                }
                catch (Exception ex)
                {
                    Toolbox.Log.LogError("RTMPDriverConnectionManager", "Failed to restart RTMP server: {0}", ex.Message);
                    _rtmpServer = null;
                    ServerError = ex.Message;
                }
            }
        }

        internal void UpdateMaxConnections(int maxConnections)
        {
            _maxConnections = maxConnections;
            if (_rtmpServer != null)
            {
                _rtmpServer.MaxConnections = maxConnections;
                Toolbox.Log.Trace("ConnectionManager: Max connections updated maxConn={0}", maxConnections);
            }
        }

        public override void Close()
        {
            Toolbox.Log.Trace("ConnectionManager: Closing");
            if (_rtmpServer != null)
            {
                _rtmpServer.Stop();
                _rtmpServer = null;
            }
            _connected = false;
            ServerError = null;
            Toolbox.Log.Trace("ConnectionManager: Closed");
        }

        public override bool IsConnected
        {
            get { return _connected; }
        }
    }
}
