using System;
using VideoOS.Platform;
using VideoOS.Platform.Live;

namespace RtmpStreamer.Streaming
{
    /// <summary>
    /// Wraps RawLiveSource to receive raw H.264 Annex B frames from a Milestone camera.
    /// Parses GenericByteData packets and fires an event with the extracted H.264 data.
    /// </summary>
    internal class CameraFrameSource : IDisposable
    {
        private RawLiveSource _rawSource;
        private Item _cameraItem;
        private bool _started;
        private readonly object _lock = new object();
        private long _eventsReceived;
        private long _framesEmitted;
        private bool _codecErrorLogged;

        /// <summary>
        /// Fired when a new H.264 frame is received from the camera.
        /// </summary>
        public event Action<byte[] /* annexBData */, bool /* isKeyFrame */, DateTime /* timestamp */> FrameReceived;

        /// <summary>
        /// Fired when the source encounters an error.
        /// </summary>
        public event Action<string> Error;

        /// <summary>
        /// The camera item this source is connected to.
        /// </summary>
        public Item CameraItem => _cameraItem;

        /// <summary>
        /// Whether the source is currently receiving frames.
        /// </summary>
        public bool IsStarted => _started;

        /// <summary>
        /// Initialize the frame source for the given camera.
        /// </summary>
        public void Init(Item cameraItem)
        {
            _cameraItem = cameraItem ?? throw new ArgumentNullException(nameof(cameraItem));

            PluginLog.Info($"[FrameSource] Creating RawLiveSource for camera: {_cameraItem.Name}, FQID={_cameraItem.FQID}");

            _rawSource = new RawLiveSource(_cameraItem);
            _rawSource.LiveContentEvent += OnLiveContent;
            _rawSource.LiveStatusEvent += OnLiveStatus;
            _rawSource.Init();

            PluginLog.Info($"[FrameSource] RawLiveSource initialized, setting LiveModeStart=true");
        }

        /// <summary>
        /// Start receiving live frames.
        /// </summary>
        public void Start()
        {
            lock (_lock)
            {
                if (_started) return;
                _rawSource.LiveModeStart = true;
                _started = true;
            }
        }

        /// <summary>
        /// Stop receiving live frames.
        /// </summary>
        public void Stop()
        {
            lock (_lock)
            {
                if (!_started) return;
                _rawSource.LiveModeStart = false;
                _started = false;
            }
        }

        public void Dispose()
        {
            Stop();
            if (_rawSource != null)
            {
                _rawSource.LiveContentEvent -= OnLiveContent;
                _rawSource.LiveStatusEvent -= OnLiveStatus;
                _rawSource.Close();
                _rawSource = null;
            }
        }

        private void OnLiveStatus(object sender, EventArgs e)
        {
            // Heartbeat/keepalive from RawLiveSource, no action needed
        }

        private void OnLiveContent(object sender, EventArgs e)
        {
            try
            {
                _eventsReceived++;

                var args = e as LiveContentRawEventArgs;
                if (args?.LiveContent == null)
                    return;

                byte[] content = args.LiveContent.Content;
                if (content == null || content.Length <= GenericByteDataParser.HeaderSize)
                    return;

                if (!GenericByteDataParser.TryParse(content, out var frame))
                    return;

                if (frame.CodecType != GenericByteDataParser.CodecH264)
                {
                    if (!_codecErrorLogged)
                    {
                        _codecErrorLogged = true;
                        string codecName;
                        switch (frame.CodecType)
                        {
                            case GenericByteDataParser.CodecH265: codecName = "H.265"; break;
                            case GenericByteDataParser.CodecJpeg: codecName = "JPEG"; break;
                            default: codecName = $"0x{frame.CodecType:X4}"; break;
                        }
                        Error?.Invoke($"Camera is using {codecName} codec. Only H.264 is supported for RTMP streaming. Please change the camera codec to H.264 in the Recording Server.");
                    }
                    return;
                }

                _framesEmitted++;
                if (_framesEmitted == 1)
                    PluginLog.Info($"[FrameSource] First H.264 frame: {frame.PayloadData.Length} bytes, keyframe={frame.IsKeyFrame}");

                FrameReceived?.Invoke(frame.PayloadData, frame.IsKeyFrame, frame.PictureTimestamp);
            }
            catch (Exception ex)
            {
                Error?.Invoke($"Frame processing error: {ex.Message}");
            }
        }
    }
}
