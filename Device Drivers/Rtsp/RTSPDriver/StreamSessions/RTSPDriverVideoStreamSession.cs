using RTSPDriver.Rtsp;
using RTSPDriver.Video;
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using VideoOS.Platform.DriverFramework.Data;
using VideoOS.Platform.DriverFramework.Data.Settings;
using VideoOS.Platform.DriverFramework.Managers;
using VideoOS.Platform.DriverFramework.Utilities;

namespace RTSPDriver
{
    /// <summary>
    /// Video stream session that serves live H.264/H.265 from RTSP pull,
    /// falling back to rich JPEG status frames showing connection state and errors.
    /// </summary>
    internal class RTSPDriverVideoStreamSession : BaseRTSPDriverStreamSession
    {
        private const int IdleFps = 1;
        private static readonly int IdleFrameIntervalMs = 1000 / IdleFps;
        private DateTime _lastFrameTime = DateTime.MinValue;

        private int _rtspPort;
        private string _rtspPath;
        private string _transport;
        private bool _channelEnabled;
        private int _channelIndex;
        private int _streamIndex; // 0 = primary, 1 = secondary
        private string _deviceName;
        private int _reconnectIntervalSec = 10;
        private int _connectionTimeoutSec = 2;
        private int _rtpBufferSizeKB = 256;
        private RtspStreamBuffer _streamBuffer;
        private bool _wasLive;
        private DateTime _lastKeyFrameTimestamp = DateTime.MinValue;
        private DateTime _prevFrameTs = DateTime.MinValue;
        private long _prevDeliverTick;

        public RTSPDriverVideoStreamSession(ISettingsManager settingsManager, RTSPDriverConnectionManager connectionManager, IEventManager eventManager, Guid sessionId, string deviceId, Guid streamId)
            : base(settingsManager, connectionManager, eventManager, sessionId, deviceId, streamId)
        {
            // Determine channel index from device ID
            Guid devGuid = new Guid(deviceId);
            for (int i = 0; i < Constants.MaxDevices; i++)
            {
                if (Constants.DeviceIds[i] == devGuid)
                {
                    _channelIndex = i;
                    break;
                }
            }
            Channel = _channelIndex;

            // Determine stream index from stream ID
            _streamIndex = streamId == Constants.VideoStream2RefId ? 1 : 0;
            _deviceName = $"Channel {_channelIndex + 1}";

            RefreshSettings();
            _settingsManager.OnSettingsChanged += OnSettingsChanged;
            Toolbox.Log.Trace("RTSPVideoStreamSession: Initialized channel={0} path={1} transport={2} enabled={3}",
                _channelIndex + 1, _rtspPath, _transport, _channelEnabled);
        }

        public override void Close()
        {
            Toolbox.Log.Trace("RTSPVideoStreamSession: Closing channel={0} stream={1} frames={2}", _channelIndex + 1, _streamIndex + 1, _frameCount);
            _settingsManager.OnSettingsChanged -= OnSettingsChanged;
            _connectionManager.StopChannel(_channelIndex, _streamIndex);
            base.Close();
        }

        private void OnSettingsChanged(object sender, SettingsChangedEventArgs e)
        {
            if (e.Settings.Any(s =>
                s.Key == Constants.RtspPort ||
                s.Key == Constants.RtspPath ||
                s.Key == Constants.RtspPath2 ||
                s.Key == Constants.TransportProtocol ||
                s.Key == Constants.ChannelEnabled ||
                s.Key == Constants.ConnectionTimeoutSec ||
                s.Key == Constants.ReconnectIntervalSec ||
                s.Key == Constants.RtpBufferSizeKB))
            {
                Toolbox.Log.Trace("RTSPVideoStreamSession: Settings changed for channel {0} stream {1}", _channelIndex + 1, _streamIndex + 1);
                RefreshSettings();
            }
        }

        private void RefreshSettings()
        {
            var portSetting = _settingsManager.GetSetting(new DeviceSetting(Constants.RtspPort, _deviceId, "554"));
            int.TryParse(portSetting?.Value ?? "554", out int newPort);
            if (newPort <= 0) newPort = 554;

            // Primary stream uses RtspPath, secondary uses RtspPath2
            string pathKey = _streamIndex == 0 ? Constants.RtspPath : Constants.RtspPath2;
            var pathSetting = _settingsManager.GetSetting(new DeviceSetting(pathKey, _deviceId, ""));
            string newPath = (pathSetting?.Value ?? "").Trim();

            // Auto-add leading slash if user forgot it
            if (newPath.Length > 0 && !newPath.StartsWith("/"))
                newPath = "/" + newPath;

            var transportSetting = _settingsManager.GetSetting(new DeviceSetting(Constants.TransportProtocol, _deviceId, "auto"));
            string newTransport = (transportSetting?.Value ?? "auto").Trim().ToLowerInvariant();
            // Normalize: only "tcp", "udp", or "auto" are valid
            if (newTransport != "tcp" && newTransport != "udp")
                newTransport = "auto";

            var enabledSetting = _settingsManager.GetSetting(new DeviceSetting(Constants.ChannelEnabled, _deviceId, "true"));
            bool.TryParse(enabledSetting?.Value ?? "true", out bool newEnabled);

            // Update hardware-level settings
            var timeoutSetting = _settingsManager.GetSetting(new HardwareSetting(Constants.ConnectionTimeoutSec, "2"));
            int.TryParse(timeoutSetting?.Value ?? "2", out int timeout);

            var reconnectSetting = _settingsManager.GetSetting(new HardwareSetting(Constants.ReconnectIntervalSec, "10"));
            int.TryParse(reconnectSetting?.Value ?? "10", out int reconnect);

            var bufferSetting = _settingsManager.GetSetting(new HardwareSetting(Constants.RtpBufferSizeKB, "256"));
            int.TryParse(bufferSetting?.Value ?? "256", out int bufSize);

            _connectionManager.UpdateSettings(timeout, reconnect, bufSize);

            // Check if channel needs restart (includes hardware settings that are baked into worker constructor)
            bool needsRestart = newPort != _rtspPort || newPath != _rtspPath || newTransport != _transport || newEnabled != _channelEnabled
                || timeout != _connectionTimeoutSec || reconnect != _reconnectIntervalSec || bufSize != _rtpBufferSizeKB;

            _rtspPort = newPort;
            _rtspPath = newPath;
            _transport = newTransport;
            _channelEnabled = newEnabled;
            _reconnectIntervalSec = reconnect;
            _connectionTimeoutSec = timeout;
            _rtpBufferSizeKB = bufSize;

            _streamBuffer = _connectionManager.GetOrCreateBuffer(_channelIndex, _streamIndex);

            if (needsRestart)
            {
                _connectionManager.RestartChannel(_channelIndex, _streamIndex, _rtspPort, _rtspPath, _transport, _channelEnabled);
            }
        }

        private VideoHeader BuildVideoHeader(byte[] frameData, bool isKeyFrame, bool isHevc, DateTime frameTs)
        {
            if (isKeyFrame)
                _lastKeyFrameTimestamp = frameTs;

            return new VideoHeader()
            {
                CodecType = isHevc ? VideoCodecType.H265 : VideoCodecType.H264,
                Length = (ulong)frameData.Length,
                SequenceNumber = _sequence++,
                SyncFrame = isKeyFrame,
                TimestampSync = _lastKeyFrameTimestamp,
                TimestampFrame = frameTs
            };
        }

        private int _frameCount;
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
                _prevFrameTs = DateTime.MinValue;
                _prevDeliverTick = 0;
                _eventManager?.NewEvent(_deviceId, Constants.StreamStarted);
                Toolbox.Log.Trace("RTSPVideoStreamSession: Stream started for channel {0}", _channelIndex + 1);
            }
            else if (!isLive && _wasLive)
            {
                _wasLive = false;
                _eventManager?.NewEvent(_deviceId, Constants.StreamStopped);
                Toolbox.Log.Trace("RTSPVideoStreamSession: Stream stopped for channel {0}", _channelIndex + 1);
            }

            // Live video path - block until a frame arrives or stream goes offline
            if (isLive)
            {
                byte[] frameData = null;
                bool isKeyFrame = false;
                bool isHevc = false;
                DateTime frameTs = DateTime.MinValue;

                while (_streamBuffer.IsLive)
                {
                    if (_streamBuffer.TryGetFrame(out frameData, out isKeyFrame, out isHevc, out frameTs))
                        break;
                    _streamBuffer.WaitForFrame(50);
                }

                if (frameData != null)
                {
                    // Smooth burst delivery pacing (same logic as RTMP driver)
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

                    _frameCount++;
                    var diagNow = DateTime.UtcNow;
                    if ((diagNow - _lastDiagLog).TotalSeconds >= 60)
                    {
                        Toolbox.Log.Trace("Session[ch{0}]: frames={1} queued={2} codec={3} isLive={4}",
                            _channelIndex + 1, _frameCount, _streamBuffer.QueueDepth, _streamBuffer.CodecName, _streamBuffer.IsLive);
                        _lastDiagLog = diagNow;
                    }

                    data = frameData;
                    header = BuildVideoHeader(frameData, isKeyFrame, isHevc, frameTs);
                    return true;
                }
                // Stream went offline during wait - fall through to status frame
            }

            // Drain remaining frames after stream went offline
            if (_streamBuffer != null &&
                _streamBuffer.TryGetFrame(out byte[] drainData, out bool drainKey, out bool drainHevc, out DateTime drainTs))
            {
                data = drainData;
                header = BuildVideoHeader(drainData, drainKey, drainHevc, drainTs);
                return true;
            }

            // Status frame at 1 FPS
            var now = DateTime.UtcNow;
            var elapsed = (now - _lastFrameTime).TotalMilliseconds;
            if (elapsed < IdleFrameIntervalMs)
            {
                int sleepMs = (int)(IdleFrameIntervalMs - elapsed);
                if (sleepMs > 0)
                    Thread.Sleep(sleepMs);
            }

            // Re-check after idle sleep — stream may have gone live during the wait
            if (_streamBuffer != null && _streamBuffer.IsLive &&
                _streamBuffer.TryGetFrame(out byte[] liveData, out bool liveKey, out bool liveHevc, out DateTime liveTs))
            {
                if (!_wasLive)
                {
                    _wasLive = true;
                    _prevFrameTs = DateTime.MinValue;
                    _prevDeliverTick = 0;
                    _eventManager?.NewEvent(_deviceId, Constants.StreamStarted);
                    Toolbox.Log.Trace("RTSPVideoStreamSession: Stream started for channel {0}", _channelIndex + 1);
                }
                _prevFrameTs = liveTs;
                _prevDeliverTick = Stopwatch.GetTimestamp();
                _frameCount++;
                data = liveData;
                header = BuildVideoHeader(liveData, liveKey, liveHevc, liveTs);
                return true;
            }

            _lastFrameTime = DateTime.UtcNow;

            data = GenerateStatusFrame();
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

        /// <summary>
        /// Generate the appropriate status frame based on current worker state.
        /// </summary>
        private byte[] GenerateStatusFrame()
        {
            if (!_channelEnabled)
                return StatusFrameGenerator.GenerateDisabledFrame(_deviceName);

            if (string.IsNullOrWhiteSpace(_rtspPath))
                return StatusFrameGenerator.GenerateNotConfiguredFrame(_deviceName);

            var worker = _connectionManager.GetWorker(_channelIndex, _streamIndex);
            if (worker == null)
                return StatusFrameGenerator.GenerateOfflineFrame(_deviceName, "Not started", _transport ?? "auto");

            string displayUrl = worker.DisplayUrl;
            string transport = worker.TransportProtocol;
            string lastError = worker.LastError;
            int attempt = worker.ReconnectAttempt;

            switch (worker.State)
            {
                case RtspWorkerState.Connecting:
                    return StatusFrameGenerator.GenerateConnectingFrame(_deviceName, displayUrl, transport, attempt);

                case RtspWorkerState.AwaitingKeyFrame:
                    return StatusFrameGenerator.GenerateAwaitingKeyFrameFrame(_deviceName, displayUrl, transport);

                case RtspWorkerState.Reconnecting:
                    return StatusFrameGenerator.GenerateReconnectingFrame(_deviceName, displayUrl, transport, lastError, attempt, _reconnectIntervalSec);

                case RtspWorkerState.Error:
                    if (lastError != null && lastError.Contains("401"))
                        return StatusFrameGenerator.GenerateAuthFailedFrame(_deviceName, displayUrl, lastError);
                    return StatusFrameGenerator.GenerateConnectionErrorFrame(_deviceName, displayUrl, transport, lastError);

                case RtspWorkerState.UnsupportedCodec:
                    return StatusFrameGenerator.GenerateUnsupportedCodecFrame(_deviceName, displayUrl, lastError);

                case RtspWorkerState.NoVideoTrack:
                    return StatusFrameGenerator.GenerateNoVideoTrackFrame(_deviceName, displayUrl, lastError);

                default:
                    return StatusFrameGenerator.GenerateOfflineFrame(_deviceName, displayUrl, transport);
            }
        }
    }
}
