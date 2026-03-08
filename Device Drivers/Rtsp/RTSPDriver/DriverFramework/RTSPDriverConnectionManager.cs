using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security;
using RTSPDriver.Rtsp;
using VideoOS.Platform.DriverFramework.Data.Settings;
using VideoOS.Platform.DriverFramework.Managers;
using VideoOS.Platform.DriverFramework.Utilities;

namespace RTSPDriver
{
    /// <summary>
    /// Manages RTSP client connections for all 4 channels.
    /// Each channel has its own RtspClientWorker that connects to the RTSP source.
    /// </summary>
    public class RTSPDriverConnectionManager : ConnectionManager
    {
        private bool _connected;
        private readonly ConcurrentDictionary<int, RtspClientWorker> _workers = new ConcurrentDictionary<int, RtspClientWorker>();
        private readonly ConcurrentDictionary<int, RtspStreamBuffer> _buffers = new ConcurrentDictionary<int, RtspStreamBuffer>();

        private string _host;
        private string _userName;
        private string _password;
        private int _connectionTimeoutSec = 10;
        private int _reconnectIntervalSec = 5;
        private int _rtpBufferSizeKB = 256;

        public Uri HardwareUri { get; private set; }

        public RTSPDriverConnectionManager(RTSPDriverContainer container) : base(container)
        {
        }

        /// <summary>
        /// Get or create the stream buffer for a channel index.
        /// </summary>
        internal RtspStreamBuffer GetOrCreateBuffer(int channelIndex)
        {
            return _buffers.GetOrAdd(channelIndex, idx => new RtspStreamBuffer($"channel{idx + 1}"));
        }

        /// <summary>
        /// Get the worker for a channel to query its status.
        /// </summary>
        internal RtspClientWorker GetWorker(int channelIndex)
        {
            _workers.TryGetValue(channelIndex, out var worker);
            return worker;
        }

        public override void Connect(Uri uri, string userName, SecureString password, ICollection<HardwareSetting> hardwareSettings)
        {
            if (_connected)
                return;

            HardwareUri = uri;
            _host = uri?.Host ?? "localhost";
            _userName = userName ?? "";
            _password = SecureStringToString(password) ?? "";

            // Parse hardware settings
            var timeoutSetting = hardwareSettings?.FirstOrDefault(s => s.Key == Constants.ConnectionTimeoutSec);
            if (timeoutSetting != null && int.TryParse(timeoutSetting.Value, out int timeout) && timeout > 0)
                _connectionTimeoutSec = timeout;

            var reconnectSetting = hardwareSettings?.FirstOrDefault(s => s.Key == Constants.ReconnectIntervalSec);
            if (reconnectSetting != null && int.TryParse(reconnectSetting.Value, out int reconnect) && reconnect > 0)
                _reconnectIntervalSec = reconnect;

            var bufferSetting = hardwareSettings?.FirstOrDefault(s => s.Key == Constants.RtpBufferSizeKB);
            if (bufferSetting != null && int.TryParse(bufferSetting.Value, out int bufSize) && bufSize > 0)
                _rtpBufferSizeKB = bufSize;

            _connected = true;
            Toolbox.Log.Trace("RTSPDriverConnectionManager: Connected host={0} user={1} timeout={2}s reconnect={3}s buffer={4}KB",
                _host, _userName, _connectionTimeoutSec, _reconnectIntervalSec, _rtpBufferSizeKB);
        }

        /// <summary>
        /// Start the RTSP pull worker for a specific channel.
        /// Called from the stream session when it initializes.
        /// </summary>
        internal void StartChannel(int channelIndex, int port, string rtspPath, string transport, bool enabled)
        {
            if (!enabled)
            {
                Toolbox.Log.Trace("RTSPDriverConnectionManager: Channel {0} disabled, skipping", channelIndex + 1);
                return;
            }

            if (string.IsNullOrWhiteSpace(rtspPath))
            {
                Toolbox.Log.Trace("RTSPDriverConnectionManager: Channel {0} has no RTSP path configured, skipping", channelIndex + 1);
                return;
            }

            StopChannel(channelIndex);

            var buffer = GetOrCreateBuffer(channelIndex);
            string rtspUrl = BuildRtspUrl(port, rtspPath);

            var worker = new RtspClientWorker(
                channelIndex,
                rtspUrl,
                transport,
                _connectionTimeoutSec,
                _reconnectIntervalSec,
                _rtpBufferSizeKB,
                buffer);

            if (_workers.TryAdd(channelIndex, worker))
            {
                worker.Start();
                Toolbox.Log.Trace("RTSPDriverConnectionManager: Started channel {0} url={1} transport={2}", channelIndex + 1, worker.DisplayUrl, transport);
            }
        }

        /// <summary>
        /// Stop and dispose the worker for a specific channel.
        /// </summary>
        internal void StopChannel(int channelIndex)
        {
            if (_workers.TryRemove(channelIndex, out var worker))
            {
                worker.Stop();
                Toolbox.Log.Trace("RTSPDriverConnectionManager: Stopped channel {0}", channelIndex + 1);
            }
        }

        /// <summary>
        /// Restart a channel with new settings.
        /// </summary>
        internal void RestartChannel(int channelIndex, int port, string rtspPath, string transport, bool enabled)
        {
            StopChannel(channelIndex);
            StartChannel(channelIndex, port, rtspPath, transport, enabled);
        }

        /// <summary>
        /// Update hardware-level settings (timeout, reconnect, buffer).
        /// </summary>
        internal void UpdateSettings(int connectionTimeoutSec, int reconnectIntervalSec, int rtpBufferSizeKB)
        {
            _connectionTimeoutSec = connectionTimeoutSec;
            _reconnectIntervalSec = reconnectIntervalSec;
            _rtpBufferSizeKB = rtpBufferSizeKB;
            Toolbox.Log.Trace("RTSPDriverConnectionManager: Settings updated timeout={0}s reconnect={1}s buffer={2}KB",
                connectionTimeoutSec, reconnectIntervalSec, rtpBufferSizeKB);
        }

        private string BuildRtspUrl(int port, string path)
        {
            string credentials = "";
            if (!string.IsNullOrEmpty(_userName))
            {
                credentials = string.IsNullOrEmpty(_password)
                    ? Uri.EscapeDataString(_userName) + "@"
                    : Uri.EscapeDataString(_userName) + ":" + Uri.EscapeDataString(_password) + "@";
            }

            if (!path.StartsWith("/"))
                path = "/" + path;

            return $"rtsp://{credentials}{_host}:{port}{path}";
        }

        public override void Close()
        {
            Toolbox.Log.Trace("RTSPDriverConnectionManager: Closing all channels");
            foreach (var kvp in _workers)
            {
                kvp.Value.Stop();
            }
            _workers.Clear();
            _connected = false;
            Toolbox.Log.Trace("RTSPDriverConnectionManager: Closed");
        }

        public override bool IsConnected => _connected;

        private static string SecureStringToString(SecureString secureString)
        {
            if (secureString == null) return null;
            IntPtr ptr = IntPtr.Zero;
            try
            {
                ptr = Marshal.SecureStringToGlobalAllocUnicode(secureString);
                return Marshal.PtrToStringUni(ptr);
            }
            finally
            {
                if (ptr != IntPtr.Zero)
                    Marshal.ZeroFreeGlobalAllocUnicode(ptr);
            }
        }
    }
}
