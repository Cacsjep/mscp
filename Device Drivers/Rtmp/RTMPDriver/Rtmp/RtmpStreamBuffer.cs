using System;
using System.Collections.Generic;
using System.Threading;
using RTMPDriver.Diagnostics;
using VideoOS.Platform.DriverFramework.Utilities;

namespace RTMPDriver.Rtmp
{
    /// <summary>
    /// Thread-safe frame queue bridging RTMP client threads (producer) and
    /// Milestone GetLiveFrame threads (consumer). One instance per stream path.
    /// H.264 requires every frame delivered in order  no skipping.
    /// </summary>
    internal class RtmpStreamBuffer
    {
        private const int MaxQueueSize = 300; // ~10 seconds at 30fps

        private readonly string _streamPath;
        private readonly object _lock = new object();
        private readonly Queue<FrameInfo> _frameQueue = new Queue<FrameInfo>();
        private readonly ManualResetEventSlim _frameAvailable = new ManualResetEventSlim(false);
        private readonly StreamStatsCollector _stats;
        private byte[] _sps;
        private byte[] _pps;
        private volatile bool _isLive;
        private string _clientInfo;

        public RtmpStreamBuffer(string streamPath)
        {
            _streamPath = streamPath;
            _stats = new StreamStatsCollector(streamPath);
        }

        public bool IsLive => _isLive;
        public string ClientInfo { get { lock (_lock) { return _clientInfo; } } }
        public StreamStatsCollector Stats => _stats;

        /// <summary>
        /// Store SPS/PPS extracted from AVC sequence header.
        /// </summary>
        public void SetSequenceHeader(byte[] sps, byte[] pps)
        {
            lock (_lock)
            {
                _sps = sps;
                _pps = pps;
            }
        }

        /// <summary>
        /// Enqueue an Annex B H.264 frame. Drops oldest frames if queue is full.
        /// Called from the RTMP client thread.
        /// </summary>
        public void PushFrame(byte[] annexBData, bool isKeyFrame, DateTime timestamp)
        {
            int queueDepthAfter;
            int overflowDropped = 0;
            lock (_lock)
            {
                // Drop to next keyframe if queue is full to avoid reference frame corruption
                if (_frameQueue.Count >= MaxQueueSize)
                {
                    // Dequeue until we find a keyframe or the queue is empty
                    while (_frameQueue.Count > 0)
                    {
                        var peek = _frameQueue.Peek();
                        if (peek.IsKeyFrame && overflowDropped > 0)
                            break; // keep this keyframe as the new start
                        _frameQueue.Dequeue();
                        overflowDropped++;
                    }
                    Toolbox.Log.Trace("RtmpStreamBuffer[{0}]: Queue full, dropped {1} frames to keyframe boundary (max={2}, remaining={3})", _streamPath, overflowDropped, MaxQueueSize, _frameQueue.Count);
                }

                _frameQueue.Enqueue(new FrameInfo
                {
                    Data = annexBData,
                    IsKeyFrame = isKeyFrame,
                    Timestamp = timestamp,
                });

                queueDepthAfter = _frameQueue.Count;
            }
            _frameAvailable.Set();
            if (overflowDropped > 0) _stats.RecordOverflowDrop(overflowDropped);
            _stats.RecordPush(annexBData?.Length ?? 0, isKeyFrame, timestamp, queueDepthAfter);
        }

        /// <summary>
        /// Dequeue the next frame in order. Returns false if queue is empty.
        /// Continues to drain the queue even after SetOffline so no frames are lost.
        /// Called from Milestone's stream session thread.
        /// </summary>
        public bool TryGetFrame(out byte[] data, out bool isKeyFrame, out DateTime timestamp)
        {
            bool gotFrame;
            lock (_lock)
            {
                if (_frameQueue.Count == 0)
                {
                    _frameAvailable.Reset();
                    data = null;
                    isKeyFrame = false;
                    timestamp = DateTime.MinValue;
                    return false;
                }

                var frame = _frameQueue.Dequeue();
                data = frame.Data;
                isKeyFrame = frame.IsKeyFrame;
                timestamp = frame.Timestamp;
                gotFrame = true;
            }
            if (gotFrame) _stats.RecordPop();
            return true;
        }

        /// <summary>
        /// Block until a frame is available or timeout expires.
        /// Used by the consumer to avoid polling with Thread.Sleep.
        /// </summary>
        public bool WaitForFrame(int timeoutMs)
        {
            return _frameAvailable.Wait(timeoutMs);
        }

        /// <summary>
        /// Get stored SPS and PPS for Annex B keyframe construction.
        /// </summary>
        public void GetSequenceHeader(out byte[] sps, out byte[] pps)
        {
            lock (_lock)
            {
                sps = _sps;
                pps = _pps;
            }
        }

        /// <summary>
        /// Mark this stream as live (an RTMP client is publishing).
        /// Returns false if another client is already publishing.
        /// </summary>
        public bool SetLive(string clientInfo)
        {
            lock (_lock)
            {
                if (_isLive)
                {
                    Toolbox.Log.Trace("RtmpStreamBuffer[{0}]: Rejected client={1}, already live from {2}", _streamPath, clientInfo, _clientInfo);
                    return false;
                }

                _isLive = true;
                _clientInfo = clientInfo;
                int cleared = _frameQueue.Count;
                _frameQueue.Clear();
                if (cleared > 0)
                    Toolbox.Log.Trace("RtmpStreamBuffer[{0}]: SetLive client={1}, cleared {2} stale frames", _streamPath, clientInfo, cleared);
                else
                    Toolbox.Log.Trace("RtmpStreamBuffer[{0}]: SetLive client={1}", _streamPath, clientInfo);
            }
            _frameAvailable.Reset();
            _stats.OnPublishStart(clientInfo);
            return true;
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
                _clientInfo = null;
                _sps = null;
                _pps = null;
                remaining = _frameQueue.Count;
            }
            _frameAvailable.Set(); // wake up consumer so it sees IsLive=false
            Toolbox.Log.Trace("RtmpStreamBuffer[{0}]: SetOffline, {1} frames remaining to drain", _streamPath, remaining);
            _stats.OnPublishStop();
        }

        /// <summary>
        /// Check if there are queued frames available (live or draining).
        /// </summary>
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
            public DateTime Timestamp;
        }
    }
}
