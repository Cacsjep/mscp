using System;
using System.Collections.Generic;
using System.Threading;
using VideoOS.Platform.DriverFramework.Utilities;

namespace RTSPDriver.Rtsp
{
    /// <summary>
    /// Thread-safe audio frame queue bridging RTSP client worker (producer) and
    /// Milestone GetLiveFrame threads (consumer). One instance per channel.
    /// </summary>
    internal class RtspAudioBuffer
    {
        private const int MaxQueueSize = 100;

        private readonly string _channelName;
        private readonly object _lock = new object();
        private readonly Queue<AudioFrameInfo> _frameQueue = new Queue<AudioFrameInfo>();
        private readonly ManualResetEventSlim _frameAvailable = new ManualResetEventSlim(false);
        private volatile bool _isLive;

        private int _sampleRate;
        private int _channels;
        private int _bitsPerSample;
        private uint _codecType;
        private uint _codecSubtype;

        public RtspAudioBuffer(string channelName)
        {
            _channelName = channelName;
        }

        public bool IsLive => _isLive;
        public int SampleRate { get { lock (_lock) { return _sampleRate; } } }
        public int Channels { get { lock (_lock) { return _channels; } } }
        public int BitsPerSample { get { lock (_lock) { return _bitsPerSample; } } }
        public uint CodecType { get { lock (_lock) { return _codecType; } } }
        public uint CodecSubtype { get { lock (_lock) { return _codecSubtype; } } }

        /// <summary>
        /// Store audio stream info when the RTSP connection discovers the audio codec.
        /// </summary>
        public void SetStreamInfo(int sampleRate, int channels, int bitsPerSample, uint codecType, uint codecSubtype)
        {
            lock (_lock)
            {
                _sampleRate = sampleRate;
                _channels = channels;
                _bitsPerSample = bitsPerSample;
                _codecType = codecType;
                _codecSubtype = codecSubtype;
            }
            Toolbox.Log.Trace("RtspAudioBuffer[{0}]: codec={1} subtype={2} rate={3} ch={4} bits={5}",
                _channelName, codecType, codecSubtype, sampleRate, channels, bitsPerSample);
        }

        /// <summary>
        /// Enqueue an audio frame. Drops oldest frames if queue is full.
        /// </summary>
        public void PushFrame(byte[] data, DateTime timestamp)
        {
            lock (_lock)
            {
                if (_frameQueue.Count >= MaxQueueSize)
                {
                    int dropped = 0;
                    while (_frameQueue.Count > MaxQueueSize / 2)
                    {
                        _frameQueue.Dequeue();
                        dropped++;
                    }
                    Toolbox.Log.Trace("RtspAudioBuffer[{0}]: Queue full, dropped {1} frames", _channelName, dropped);
                }

                _frameQueue.Enqueue(new AudioFrameInfo
                {
                    Data = data,
                    Timestamp = timestamp,
                });
            }
            _frameAvailable.Set();
        }

        /// <summary>
        /// Dequeue the next audio frame. Returns false if queue is empty.
        /// </summary>
        public bool TryGetFrame(out byte[] data, out DateTime timestamp)
        {
            lock (_lock)
            {
                if (_frameQueue.Count == 0)
                {
                    _frameAvailable.Reset();
                    data = null;
                    timestamp = DateTime.MinValue;
                    return false;
                }

                var frame = _frameQueue.Dequeue();
                data = frame.Data;
                timestamp = frame.Timestamp;
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

        public void SetLive()
        {
            lock (_lock)
            {
                _isLive = true;
                _frameQueue.Clear();
            }
            _frameAvailable.Reset();
            Toolbox.Log.Trace("RtspAudioBuffer[{0}]: Live", _channelName);
        }

        public void SetOffline()
        {
            lock (_lock)
            {
                _isLive = false;
            }
            _frameAvailable.Set();
        }

        private struct AudioFrameInfo
        {
            public byte[] Data;
            public DateTime Timestamp;
        }
    }
}
