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
    /// Manages RTSP client connections for all channels.
    /// Each channel can have up to 2 video streams (primary + secondary) plus audio from the primary stream.
    /// Workers and buffers are keyed by WorkerKey(channelIndex, streamIndex).
    /// </summary>
    public class RTSPDriverConnectionManager : ConnectionManager
    {
        private bool _connected;
        private readonly ConcurrentDictionary<int, RtspClientWorker> _workers = new ConcurrentDictionary<int, RtspClientWorker>();
        private readonly ConcurrentDictionary<int, RtspStreamBuffer> _buffers = new ConcurrentDictionary<int, RtspStreamBuffer>();
        private readonly ConcurrentDictionary<int, RtspAudioBuffer> _audioBuffers = new ConcurrentDictionary<int, RtspAudioBuffer>();

        /// <summary>
        /// Compute a flat key from channel index + stream index.
        /// </summary>
        private static int WorkerKey(int channelIndex, int streamIndex) => channelIndex * 2 + streamIndex;

        private string _host;
        private string _userName;
        private string _password;
        private int _connectionTimeoutSec = 2;
        private int _reconnectIntervalSec = 10;
        private int _rtpBufferSizeKB = 256;

        public Uri HardwareUri { get; private set; }

        public RTSPDriverConnectionManager(RTSPDriverContainer container) : base(container)
        {
        }

        /// <summary>
        /// Get or create the video stream buffer for a channel and stream index.
        /// </summary>
        internal RtspStreamBuffer GetOrCreateBuffer(int channelIndex, int streamIndex = 0)
        {
            int key = WorkerKey(channelIndex, streamIndex);
            return _buffers.GetOrAdd(key, _ => new RtspStreamBuffer($"ch{channelIndex + 1}_s{streamIndex + 1}"));
        }

        /// <summary>
        /// Get or create the audio buffer for a channel. Audio always comes from the primary stream.
        /// </summary>
        internal RtspAudioBuffer GetOrCreateAudioBuffer(int channelIndex)
        {
            return _audioBuffers.GetOrAdd(channelIndex, idx => new RtspAudioBuffer($"ch{idx + 1}_audio"));
        }

        /// <summary>
        /// Get the worker for a channel and stream index to query its status.
        /// </summary>
        internal RtspClientWorker GetWorker(int channelIndex, int streamIndex = 0)
        {
            _workers.TryGetValue(WorkerKey(channelIndex, streamIndex), out var worker);
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
        /// Start the RTSP pull worker for a specific channel and stream.
        /// Audio buffer is only attached to the primary stream (streamIndex=0).
        /// </summary>
        internal void StartChannel(int channelIndex, int streamIndex, int port, string rtspPath, string transport, bool enabled)
        {
            if (!enabled)
            {
                Toolbox.Log.Trace("RTSPDriverConnectionManager: Channel {0} stream {1} disabled, skipping", channelIndex + 1, streamIndex + 1);
                return;
            }

            if (string.IsNullOrWhiteSpace(rtspPath))
            {
                Toolbox.Log.Trace("RTSPDriverConnectionManager: Channel {0} stream {1} has no RTSP path configured, skipping", channelIndex + 1, streamIndex + 1);
                return;
            }

            StopChannel(channelIndex, streamIndex);

            var buffer = GetOrCreateBuffer(channelIndex, streamIndex);
            string rtspUrl = BuildRtspUrl(port, rtspPath);

            // Audio buffer only on the primary stream
            RtspAudioBuffer audioBuffer = streamIndex == 0 ? GetOrCreateAudioBuffer(channelIndex) : null;

            var worker = new RtspClientWorker(
                channelIndex,
                rtspUrl,
                transport,
                _connectionTimeoutSec,
                _reconnectIntervalSec,
                _rtpBufferSizeKB,
                buffer,
                audioBuffer);

            int key = WorkerKey(channelIndex, streamIndex);
            if (_workers.TryAdd(key, worker))
            {
                worker.Start();
                Toolbox.Log.Trace("RTSPDriverConnectionManager: Started channel {0} stream {1} url={2} transport={3} audio={4}",
                    channelIndex + 1, streamIndex + 1, worker.DisplayUrl, transport, audioBuffer != null ? "yes" : "no");
            }
        }

        /// <summary>
        /// Stop and dispose the worker for a specific channel and stream.
        /// </summary>
        internal void StopChannel(int channelIndex, int streamIndex = 0)
        {
            int key = WorkerKey(channelIndex, streamIndex);
            if (_workers.TryRemove(key, out var worker))
            {
                worker.Stop();
                Toolbox.Log.Trace("RTSPDriverConnectionManager: Stopped channel {0} stream {1}", channelIndex + 1, streamIndex + 1);
            }
        }

        /// <summary>
        /// Restart a channel stream with new settings.
        /// </summary>
        internal void RestartChannel(int channelIndex, int streamIndex, int port, string rtspPath, string transport, bool enabled)
        {
            StopChannel(channelIndex, streamIndex);
            StartChannel(channelIndex, streamIndex, port, rtspPath, transport, enabled);
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
