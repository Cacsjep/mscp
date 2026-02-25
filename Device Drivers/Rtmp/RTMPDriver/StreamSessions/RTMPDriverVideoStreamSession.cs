using RTMPDriver.Rtmp;
using RTMPDriver.Video;
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using VideoOS.Platform.DriverFramework.Data;
using VideoOS.Platform.DriverFramework.Data.Settings;
using VideoOS.Platform.DriverFramework.Managers;
using VideoOS.Platform.DriverFramework.Utilities;

namespace RTMPDriver
{
    /// <summary>
    /// Video stream session that serves live H.264 from RTMP push,
    /// falling back to a JPEG test pattern when no stream is active.
    /// </summary>
    internal class RTMPDriverVideoStreamSession : BaseRTMPDriverStreamSession
    {
        private const int IdleFps = 1;
        private static readonly int IdleFrameIntervalMs = 1000 / IdleFps;
        private DateTime _lastFrameTime = DateTime.MinValue;

        private string _serverPort;
        private string _streamPath;
        private string _rtmpUrl;
        private string _deviceName;
        private bool _showPreview;
        private bool _tlsEnabled;
        private RtmpStreamBuffer _streamBuffer;
        private bool _wasLive;
        private DateTime _lastKeyFrameTimestamp = DateTime.MinValue;
        private DateTime _prevFrameTs = DateTime.MinValue;
        private long _prevDeliverTick;

        public RTMPDriverVideoStreamSession(ISettingsManager settingsManager, RTMPDriverConnectionManager connectionManager, IEventManager eventManager, Guid sessionId, string deviceId, Guid streamId) :
            base(settingsManager, connectionManager, eventManager, sessionId, deviceId, streamId)
        {
            Channel = 0;
            RefreshSettings();
            _settingsManager.OnSettingsChanged += OnSettingsChanged;
            Toolbox.Log.Trace("VideoStreamSession: Initialized sessionId={0} path={1} url={2}", sessionId, _streamPath, _rtmpUrl);
        }

        public override void Close()
        {
            Toolbox.Log.Trace("VideoStreamSession: Closing path={0} h264={1}", _streamPath, _h264Count);
            _settingsManager.OnSettingsChanged -= OnSettingsChanged;

            // Remove buffer so disabled/removed devices reject new RTMP publishers
            var rtmpServer = _connectionManager.RtmpServer;
            if (rtmpServer != null && _streamPath != null)
            {
                rtmpServer.RemoveBuffer(_streamPath);
            }

            base.Close();
        }

        private void OnSettingsChanged(object sender, SettingsChangedEventArgs e)
        {
            if (e.Settings.Any(s => s.Key == Constants.ServerPort || s.Key == Constants.RtmpPushStreamPath || s.Key == Constants.ShowOfflineInfo || s.Key == Constants.RateLimitEnabled || s.Key == Constants.RateLimitMaxRequests || s.Key == Constants.MaxConnections || s.Key == Constants.EnableTls || s.Key == Constants.TlsCertificatePassword))
            {
                Toolbox.Log.Trace("VideoStreamSession: Settings changed, refreshing path={0}", _streamPath);
                RefreshSettings();
            }
        }

        private void RefreshSettings()
        {
            var portSetting = _settingsManager.GetSetting(new HardwareSetting(Constants.ServerPort, "8783"));
            _serverPort = portSetting?.Value ?? "8783";

            if (int.TryParse(_serverPort, out int newPort))
                _connectionManager.RestartOnPort(newPort);

            var tlsEnabledSetting = _settingsManager.GetSetting(new HardwareSetting(Constants.EnableTls, "false"));
            bool.TryParse(tlsEnabledSetting?.Value ?? "false", out _tlsEnabled);

            var certPwdSetting = _settingsManager.GetSetting(new HardwareSetting(Constants.TlsCertificatePassword, ""));
            string certPassword = certPwdSetting?.Value ?? "";

            _connectionManager.UpdateTls(_tlsEnabled, certPassword);

            var pathSetting = _settingsManager.GetSetting(new DeviceSetting(Constants.RtmpPushStreamPath, _deviceId, "/stream"));
            _streamPath = pathSetting?.Value ?? "/stream";

            string scheme = _tlsEnabled ? "rtmps" : "rtmp";
            _rtmpUrl = $"{scheme}://localhost:{_serverPort}{_streamPath}";

            // Derive device name from stream path: "/stream1" → "Stream 1"
            string pathName = _streamPath.TrimStart('/');
            int nameIdx = 0;
            while (nameIdx < pathName.Length && !char.IsDigit(pathName[nameIdx])) nameIdx++;
            if (nameIdx > 0 && nameIdx < pathName.Length)
                _deviceName = char.ToUpper(pathName[0]) + pathName.Substring(1, nameIdx - 1) + " " + pathName.Substring(nameIdx);
            else
                _deviceName = pathName;

            var showPreviewSettings = _settingsManager.GetSetting(new HardwareSetting(Constants.ShowOfflineInfo, "true"));
            bool.TryParse(showPreviewSettings?.Value ?? "false", out _showPreview);

            var rateLimitEnabledSetting = _settingsManager.GetSetting(new HardwareSetting(Constants.RateLimitEnabled, "true"));
            bool.TryParse(rateLimitEnabledSetting?.Value ?? "false", out bool rateLimitEnabled);

            var rateLimitMaxSetting = _settingsManager.GetSetting(new HardwareSetting(Constants.RateLimitMaxRequests, "10"));
            if (!int.TryParse(rateLimitMaxSetting?.Value ?? "10", out int rateLimitMax))
                rateLimitMax = 10;

            _connectionManager.UpdateRateLimiter(rateLimitEnabled, rateLimitMax);

            var maxConnSetting = _settingsManager.GetSetting(new HardwareSetting(Constants.MaxConnections, Constants.DefaultMaxConnections.ToString()));
            if (int.TryParse(maxConnSetting?.Value ?? Constants.DefaultMaxConnections.ToString(), out int maxConn) && maxConn > 0)
                _connectionManager.UpdateMaxConnections(maxConn);

            // Look up the stream buffer from the RTMP server
            var rtmpServer = _connectionManager.RtmpServer;
            if (rtmpServer != null)
            {
                _streamBuffer = rtmpServer.GetOrCreateBuffer(_streamPath);
            }
        }

        private VideoHeader BuildH264Header(byte[] frameData, bool isKeyFrame, DateTime frameTs)
        {
            if (isKeyFrame)
                _lastKeyFrameTimestamp = frameTs;

            return new VideoHeader()
            {
                CodecType = VideoCodecType.H264,
                Length = (ulong)frameData.Length,
                SequenceNumber = _sequence++,
                SyncFrame = isKeyFrame,
                TimestampSync = _lastKeyFrameTimestamp,
                TimestampFrame = frameTs
            };
        }

        private int _h264Count;
        private DateTime _lastDiagLog = DateTime.MinValue;

        protected override bool GetLiveFrameInternal(TimeSpan timeout, out BaseDataHeader header, out byte[] data)
        {
            header = null;
            data = null;

            bool isLive = _streamBuffer != null && _streamBuffer.IsLive;

            // Detect live/offline transitions and fire events
            if (isLive && !_wasLive)
            {
                _wasLive = true;
                _eventManager?.NewEvent(_deviceId, Constants.StreamStarted);
                Toolbox.Log.Trace("VideoStreamSession: Stream started event fired for {0}", _streamPath);
            }
            else if (!isLive && _wasLive)
            {
                _wasLive = false;
                _eventManager?.NewEvent(_deviceId, Constants.StreamStopped);
                Toolbox.Log.Trace("VideoStreamSession: Stream stopped event fired for {0}", _streamPath);
            }

            // Live H.264 path — block until a frame arrives or stream goes offline
            if (isLive)
            {
                byte[] frameData = null;
                bool isKeyFrame = false;
                DateTime frameTs = DateTime.MinValue;

                // Wait for the next frame using event signaling (no polling).
                // Loop exits when we get a frame OR the stream goes offline.
                while (_streamBuffer.IsLive)
                {
                    if (_streamBuffer.TryGetFrame(out frameData, out isKeyFrame, out frameTs))
                        break;
                    _streamBuffer.WaitForFrame(50);
                }

                if (frameData != null)
                {
                    // Smooth burst delivery: only pace when frames are queued ahead.
                    // When queue is empty, WaitForFrame already provided natural pacing.
                    // When queue is deep (>30), skip pacing to drain backlog.
                    int queueDepth = _streamBuffer.QueueDepth;
                    if (queueDepth > 0 && queueDepth <= 30
                        && _prevFrameTs != DateTime.MinValue && _prevDeliverTick != 0
                        && frameTs > _prevFrameTs)
                    {
                        double mediaIntervalMs = (frameTs - _prevFrameTs).TotalMilliseconds;
                        long targetTick = _prevDeliverTick + (long)(mediaIntervalMs * Stopwatch.Frequency / 1000.0);
                        long tickNow = Stopwatch.GetTimestamp();
                        double remainingMs = (targetTick - tickNow) * 1000.0 / Stopwatch.Frequency;

                        if (remainingMs > 0 && remainingMs < 200)
                        {
                            int coarseSleepMs = (int)remainingMs - 2;
                            if (coarseSleepMs > 0)
                                Thread.Sleep(coarseSleepMs);
                            while (Stopwatch.GetTimestamp() < targetTick)
                                Thread.SpinWait(1);
                        }
                    }
                    _prevFrameTs = frameTs;
                    _prevDeliverTick = Stopwatch.GetTimestamp();

                    _h264Count++;
                    var diagNow = DateTime.UtcNow;
                    if ((diagNow - _lastDiagLog).TotalSeconds >= 20)
                    {
                        Toolbox.Log.Trace("Session[{0}]: h264={1} queued={2} isLive={3}",
                            _streamPath, _h264Count, _streamBuffer.QueueDepth, _streamBuffer.IsLive);
                        _lastDiagLog = diagNow;
                    }
                    data = frameData;
                    header = BuildH264Header(frameData, isKeyFrame, frameTs);
                    return true;
                }
                // Stream went offline during wait — fall through to test pattern
            }

            // Drain remaining H.264 frames after stream went offline
            if (_streamBuffer != null &&
                _streamBuffer.TryGetFrame(out byte[] drainData, out bool drainKey, out DateTime drainTs))
            {
                data = drainData;
                header = BuildH264Header(drainData, drainKey, drainTs);
                return true;
            }

            // Idle test pattern at 1 FPS
            var now = DateTime.UtcNow;
            var elapsed = (now - _lastFrameTime).TotalMilliseconds;
            if (elapsed < IdleFrameIntervalMs)
            {
                int sleepMs = (int)(IdleFrameIntervalMs - elapsed);
                if (sleepMs > 0)
                    Thread.Sleep(sleepMs);
            }
            _lastFrameTime = DateTime.UtcNow;

            string serverError = _connectionManager.ServerError;
            if (serverError != null)
            {
                data = TestPatternGenerator.GenerateErrorFrame(_deviceName, Constants.DriverVersion, serverError);
            }
            else if (_showPreview)
            {
                data = TestPatternGenerator.GenerateFrame(_rtmpUrl, _deviceName, Constants.DriverVersion);
            }
            else
            {
                data = TestPatternGenerator.GenerateBlackFrame();
            }
            if (data == null || data.Length == 0)
                return false;

            DateTime dt = DateTime.UtcNow;
            header = new VideoHeader()
            {
                CodecType = VideoCodecType.JPEG,
                Length = (ulong)data.Length,
                SequenceNumber = _sequence++,
                SyncFrame = true,
                TimestampSync = dt,
                TimestampFrame = dt
            };
            return true;
        }
    }
}
