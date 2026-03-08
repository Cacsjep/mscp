using System;
using System.Collections.Generic;
using System.Threading;
using VideoOS.Platform.DriverFramework.Utilities;

namespace RTSPDriver.Rtsp
{
    /// <summary>
    /// Thread-safe frame queue bridging RTSP client worker (producer) and
    /// Milestone GetLiveFrame threads (consumer). One instance per channel.
    /// </summary>
    internal class RtspStreamBuffer
    {
        private const int MaxQueueSize = 300;
        private const int LogIntervalFrames = 150;

        private readonly string _channelName;
        private readonly object _lock = new object();
        private readonly Queue<FrameInfo> _frameQueue = new Queue<FrameInfo>();
        private readonly ManualResetEventSlim _frameAvailable = new ManualResetEventSlim(false);
        private volatile bool _isLive;
        private string _codecName;
        private int _width;
        private int _height;
        private int _pushCount;
        private int _popCount;

        public RtspStreamBuffer(string channelName)
        {
            _channelName = channelName;
        }

        public bool IsLive => _isLive;
        public string CodecName { get { lock (_lock) { return _codecName; } } }
        public int Width { get { lock (_lock) { return _width; } } }
        public int Height { get { lock (_lock) { return _height; } } }

        /// <summary>
        /// Store stream info when the RTSP connection discovers the codec.
        /// </summary>
        public void SetStreamInfo(string codecName, int width, int height)
        {
            lock (_lock)
            {
                _codecName = codecName;
                _width = width;
                _height = height;
            }
        }

        /// <summary>
        /// Enqueue an Annex B frame. Drops oldest frames if queue is full.
        /// Called from the RTSP client worker thread.
        /// </summary>
        public void PushFrame(byte[] annexBData, bool isKeyFrame, bool isHevc, DateTime timestamp)
        {
            lock (_lock)
            {
                if (_frameQueue.Count >= MaxQueueSize)
                {
                    int dropped = 0;
                    while (_frameQueue.Count > 0)
                    {
                        var peek = _frameQueue.Peek();
                        if (peek.IsKeyFrame && dropped > 0)
                            break;
                        _frameQueue.Dequeue();
                        dropped++;
                    }
                    Toolbox.Log.Trace("RtspStreamBuffer[{0}]: Queue full, dropped {1} frames to keyframe boundary (max={2}, remaining={3})", _channelName, dropped, MaxQueueSize, _frameQueue.Count);
                }

                _frameQueue.Enqueue(new FrameInfo
                {
                    Data = annexBData,
                    IsKeyFrame = isKeyFrame,
                    IsHevc = isHevc,
                    Timestamp = timestamp,
                });

                _pushCount++;
                if (_pushCount % LogIntervalFrames == 0)
                {
                    Toolbox.Log.Trace("RtspStreamBuffer[{0}]: pushed={1} popped={2} queued={3}",
                        _channelName, _pushCount, _popCount, _frameQueue.Count);
                }
            }
            _frameAvailable.Set();
        }

        /// <summary>
        /// Dequeue the next frame in order. Returns false if queue is empty.
        /// </summary>
        public bool TryGetFrame(out byte[] data, out bool isKeyFrame, out bool isHevc, out DateTime timestamp)
        {
            lock (_lock)
            {
                if (_frameQueue.Count == 0)
                {
                    _frameAvailable.Reset();
                    data = null;
                    isKeyFrame = false;
                    isHevc = false;
                    timestamp = DateTime.MinValue;
                    return false;
                }

                var frame = _frameQueue.Dequeue();
                data = frame.Data;
                isKeyFrame = frame.IsKeyFrame;
                isHevc = frame.IsHevc;
                timestamp = frame.Timestamp;
                _popCount++;
                return true;
            }
        }

        /// <summary>
        /// Block until a frame is available or timeout expires.
        /// </summary>
        public bool WaitForFrame(int timeoutMs)
        {
            return _frameAvailable.Wait(timeoutMs);
        }

        /// <summary>
        /// Mark this stream as live.
        /// </summary>
        public void SetLive()
        {
            lock (_lock)
            {
                _isLive = true;
                _frameQueue.Clear();
                Toolbox.Log.Trace("RtspStreamBuffer[{0}]: SetLive", _channelName);
            }
            _frameAvailable.Reset();
        }

        /// <summary>
        /// Mark this stream as offline. Does NOT clear the queue so remaining frames can be drained.
        /// </summary>
        public void SetOffline()
        {
            int remaining;
            lock (_lock)
            {
                _isLive = false;
                _codecName = null;
                _width = 0;
                _height = 0;
                remaining = _frameQueue.Count;
            }
            _frameAvailable.Set();
            Toolbox.Log.Trace("RtspStreamBuffer[{0}]: SetOffline, {1} frames remaining to drain, pushed={2} popped={3}", _channelName, remaining, _pushCount, _popCount);
        }

        public bool HasFrames
        {
            get { lock (_lock) { return _frameQueue.Count > 0; } }
        }

        public int QueueDepth
        {
            get { lock (_lock) { return _frameQueue.Count; } }
        }

        private struct FrameInfo
        {
            public byte[] Data;
            public bool IsKeyFrame;
            public bool IsHevc;
            public DateTime Timestamp;
        }
    }
}
