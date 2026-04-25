using System;
using System.Globalization;
using System.Text;
using System.Threading;
using VideoOS.Platform.DriverFramework.Utilities;

namespace RTMPDriver.Diagnostics
{
    /// <summary>
    /// Per-stream diagnostic counters that gather everything we know about a single
    /// RTMP push session: source-declared metadata, push-side rates, pop-side rates,
    /// queue health, drops by reason, inter-frame jitter, and pacing behavior.
    ///
    /// Emits a single multi-line block to MIPLog.txt every 30 seconds and one final
    /// block when the publisher disconnects. A field engineer can read the block top
    /// to bottom and decide whether the FPS shortfall is on the source side, the
    /// driver side, or the consumer (Recording Server) side.
    ///
    /// All counter mutations are O(1). The 30-second emit grabs a lock to snapshot
    /// the window counters, then formats outside the lock.
    /// </summary>
    internal class StreamStatsCollector
    {
        private const int EmitIntervalSeconds = 30;
        private const int BufferCapacity = 300; // keep in sync with RtmpStreamBuffer.MaxQueueSize

        private readonly string _streamPath;
        private readonly object _lock = new object();
        private Timer _timer;

        // Stream context (set on publish start)
        private string _publisherEndpoint;
        private string _videoCodec;       // e.g. "H.264 (legacy FLV)" or "H.264 (Enhanced RTMP avc1)"
        private int _sourceWidth;
        private int _sourceHeight;
        private double _sourceFps;
        private double _sourceVideoKbps;
        private double _sourceAudioKbps;
        private string _audioCodec;
        private double _audioSampleRate;
        private bool _haveSourceMetadata;
        private DateTime _publishStartUtc;

        // Cumulative counters (since publish start)
        private long _totalPushedFrames;
        private long _totalPoppedFrames;
        private long _totalKeyframes;
        private long _totalDroppedOverflow;
        private long _totalDroppedSeiOnly;
        private long _totalDroppedNonH264;
        private long _totalBytesPushed;

        // Window counters (reset every emit)
        private long _winPushedFrames;
        private long _winPoppedFrames;
        private long _winKeyframes;
        private long _winDroppedOverflow;
        private long _winDroppedSeiOnly;
        private long _winDroppedNonH264;
        private long _winBytesPushed;
        private int _winMaxQueueDepth;
        private long _winQueueDepthSum;
        private int _winQueueDepthSamples;
        private double _winMinDeltaMs;
        private double _winMaxDeltaMs;
        private double _winSumDeltaMs;
        private int _winDeltaSamples;
        private double _winSumPacingSleepMs;
        private int _winPacingSamples;

        // Inter-frame delta state
        private DateTime _lastFrameWallTs;

        // Window timing
        private DateTime _windowStartUtc;

        public StreamStatsCollector(string streamPath)
        {
            _streamPath = streamPath;
        }

        public void OnPublishStart(string publisherEndpoint)
        {
            lock (_lock)
            {
                _publisherEndpoint = publisherEndpoint ?? "?";
                _publishStartUtc = DateTime.UtcNow;
                _windowStartUtc = _publishStartUtc;
                _lastFrameWallTs = DateTime.MinValue;
                ResetCumulativeUnsafe();
                ResetWindowUnsafe();
                _videoCodec = null;
                _sourceWidth = 0;
                _sourceHeight = 0;
                _sourceFps = 0;
                _sourceVideoKbps = 0;
                _sourceAudioKbps = 0;
                _audioCodec = null;
                _audioSampleRate = 0;
                _haveSourceMetadata = false;
            }

            // Fire the timer 30 s after publish start, then every 30 s.
            _timer = new Timer(_ => OnTimerTick(), null,
                TimeSpan.FromSeconds(EmitIntervalSeconds),
                TimeSpan.FromSeconds(EmitIntervalSeconds));
        }

        public void OnPublishStop()
        {
            try { _timer?.Dispose(); } catch { }
            _timer = null;

            // Emit one final stats block on disconnect.
            EmitStats(isFinal: true);
        }

        public void SetVideoCodec(string codecLabel)
        {
            lock (_lock) { _videoCodec = codecLabel; }
        }

        // Called when an AMF onMetaData frame arrives. Any field can be 0/null if absent.
        public void SetSourceMetadata(int width, int height, double fps, double videoKbps, string audioCodec, double audioSampleRate, double audioKbps)
        {
            lock (_lock)
            {
                if (width > 0) _sourceWidth = width;
                if (height > 0) _sourceHeight = height;
                if (fps > 0) _sourceFps = fps;
                if (videoKbps > 0) _sourceVideoKbps = videoKbps;
                if (audioKbps > 0) _sourceAudioKbps = audioKbps;
                if (!string.IsNullOrEmpty(audioCodec)) _audioCodec = audioCodec;
                if (audioSampleRate > 0) _audioSampleRate = audioSampleRate;
                _haveSourceMetadata = true;
            }
        }

        public void RecordPush(int byteCount, bool isKeyFrame, DateTime frameWallTs, int queueDepthAfter)
        {
            lock (_lock)
            {
                _totalPushedFrames++;
                _winPushedFrames++;
                _totalBytesPushed += byteCount;
                _winBytesPushed += byteCount;
                if (isKeyFrame) { _totalKeyframes++; _winKeyframes++; }

                if (queueDepthAfter > _winMaxQueueDepth) _winMaxQueueDepth = queueDepthAfter;
                _winQueueDepthSum += queueDepthAfter;
                _winQueueDepthSamples++;

                if (_lastFrameWallTs != DateTime.MinValue && frameWallTs > _lastFrameWallTs)
                {
                    double deltaMs = (frameWallTs - _lastFrameWallTs).TotalMilliseconds;
                    if (_winDeltaSamples == 0)
                    {
                        _winMinDeltaMs = deltaMs;
                        _winMaxDeltaMs = deltaMs;
                    }
                    else
                    {
                        if (deltaMs < _winMinDeltaMs) _winMinDeltaMs = deltaMs;
                        if (deltaMs > _winMaxDeltaMs) _winMaxDeltaMs = deltaMs;
                    }
                    _winSumDeltaMs += deltaMs;
                    _winDeltaSamples++;
                }
                _lastFrameWallTs = frameWallTs;
            }
        }

        public void RecordPop()
        {
            lock (_lock)
            {
                _totalPoppedFrames++;
                _winPoppedFrames++;
            }
        }

        public void RecordOverflowDrop(int droppedCount)
        {
            if (droppedCount <= 0) return;
            lock (_lock)
            {
                _totalDroppedOverflow += droppedCount;
                _winDroppedOverflow += droppedCount;
            }
        }

        public void RecordSeiOnlyDrop()
        {
            lock (_lock)
            {
                _totalDroppedSeiOnly++;
                _winDroppedSeiOnly++;
            }
        }

        public void RecordNonH264Drop()
        {
            lock (_lock)
            {
                _totalDroppedNonH264++;
                _winDroppedNonH264++;
            }
        }

        public void RecordPacingSleep(double sleepMs)
        {
            if (sleepMs <= 0) return;
            lock (_lock)
            {
                _winSumPacingSleepMs += sleepMs;
                _winPacingSamples++;
            }
        }

        private void OnTimerTick()
        {
            try { EmitStats(isFinal: false); }
            catch (Exception ex)
            {
                try { Toolbox.Log.LogError("StreamStatsCollector[{0}]: emit failed: {1}", _streamPath, ex.Message); }
                catch { }
            }
        }

        private void EmitStats(bool isFinal)
        {
            // Snapshot under lock, then format/log outside.
            Snapshot snap;
            lock (_lock)
            {
                snap = new Snapshot
                {
                    StreamPath = _streamPath,
                    Publisher = _publisherEndpoint,
                    PublishStartUtc = _publishStartUtc,
                    NowUtc = DateTime.UtcNow,
                    WindowStartUtc = _windowStartUtc,
                    VideoCodec = _videoCodec,
                    SourceWidth = _sourceWidth,
                    SourceHeight = _sourceHeight,
                    SourceFps = _sourceFps,
                    SourceVideoKbps = _sourceVideoKbps,
                    SourceAudioKbps = _sourceAudioKbps,
                    AudioCodec = _audioCodec,
                    AudioSampleRate = _audioSampleRate,
                    HaveSourceMetadata = _haveSourceMetadata,
                    TotalPushedFrames = _totalPushedFrames,
                    TotalPoppedFrames = _totalPoppedFrames,
                    TotalKeyframes = _totalKeyframes,
                    TotalDroppedOverflow = _totalDroppedOverflow,
                    TotalDroppedSeiOnly = _totalDroppedSeiOnly,
                    TotalDroppedNonH264 = _totalDroppedNonH264,
                    TotalBytesPushed = _totalBytesPushed,
                    WinPushedFrames = _winPushedFrames,
                    WinPoppedFrames = _winPoppedFrames,
                    WinKeyframes = _winKeyframes,
                    WinDroppedOverflow = _winDroppedOverflow,
                    WinDroppedSeiOnly = _winDroppedSeiOnly,
                    WinDroppedNonH264 = _winDroppedNonH264,
                    WinBytesPushed = _winBytesPushed,
                    WinMaxQueueDepth = _winMaxQueueDepth,
                    WinAvgQueueDepth = _winQueueDepthSamples > 0 ? (double)_winQueueDepthSum / _winQueueDepthSamples : 0,
                    WinMinDeltaMs = _winMinDeltaMs,
                    WinAvgDeltaMs = _winDeltaSamples > 0 ? _winSumDeltaMs / _winDeltaSamples : 0,
                    WinMaxDeltaMs = _winMaxDeltaMs,
                    WinDeltaSamples = _winDeltaSamples,
                    WinAvgPacingSleepMs = _winPacingSamples > 0 ? _winSumPacingSleepMs / _winPacingSamples : 0,
                    WinPacingSamples = _winPacingSamples,
                    IsFinal = isFinal,
                };

                ResetWindowUnsafe();
                _windowStartUtc = snap.NowUtc;
            }

            string block = FormatBlock(snap);
            Toolbox.Log.Trace("{0}", block);
        }

        private static string FormatBlock(Snapshot s)
        {
            double winSec = Math.Max(1.0, (s.NowUtc - s.WindowStartUtc).TotalSeconds);
            double pushFps = s.WinPushedFrames / winSec;
            double popFps = s.WinPoppedFrames / winSec;
            double winMbps = (s.WinBytesPushed * 8.0) / winSec / 1_000_000.0;
            double avgFrameKb = s.WinPushedFrames > 0 ? (s.WinBytesPushed / 1024.0 / s.WinPushedFrames) : 0;
            double gop = s.WinKeyframes > 0 ? (double)s.WinPushedFrames / s.WinKeyframes : 0;
            TimeSpan uptime = s.NowUtc - s.PublishStartUtc;

            var ci = CultureInfo.InvariantCulture;
            var sb = new StringBuilder(1024);
            sb.AppendLine();
            string headerTag = s.IsFinal ? " (final, publisher disconnected)" : "";
            sb.AppendLine(string.Format(ci, "================ RTMP Stream Stats: {0}{1} ================", s.StreamPath, headerTag));
            sb.AppendLine(string.Format(ci, " Publisher       : {0}", s.Publisher ?? "?"));
            sb.AppendLine(string.Format(ci, " Uptime          : {0:hh\\:mm\\:ss} (since {1:yyyy-MM-dd HH:mm:ss} UTC)", uptime, s.PublishStartUtc));
            sb.AppendLine(string.Format(ci, " Video codec     : {0}", s.VideoCodec ?? "(unknown)"));
            if (s.HaveSourceMetadata)
            {
                string res = (s.SourceWidth > 0 && s.SourceHeight > 0)
                    ? string.Format(ci, "{0}x{1}", s.SourceWidth, s.SourceHeight)
                    : "?";
                string srcFps = s.SourceFps > 0 ? string.Format(ci, "{0:0.##} fps", s.SourceFps) : "? fps";
                string srcKbps = s.SourceVideoKbps > 0 ? string.Format(ci, "{0:0} kbps", s.SourceVideoKbps) : "?";
                sb.AppendLine(string.Format(ci, " Source declared : {0} @ {1}, video={2}", res, srcFps, srcKbps));
                if (!string.IsNullOrEmpty(s.AudioCodec) || s.AudioSampleRate > 0 || s.SourceAudioKbps > 0)
                {
                    sb.AppendLine(string.Format(ci, " Source audio    : codec={0} rate={1} Hz {2}",
                        s.AudioCodec ?? "?",
                        s.AudioSampleRate > 0 ? s.AudioSampleRate.ToString("0", ci) : "?",
                        s.SourceAudioKbps > 0 ? string.Format(ci, "({0:0} kbps)", s.SourceAudioKbps) : "(audio is ignored by the driver)"));
                }
            }
            else
            {
                sb.AppendLine(" Source declared : (no onMetaData received from publisher)");
            }

            sb.AppendLine(string.Format(ci, " --- Last {0:0}s window ---", winSec));
            sb.AppendLine(string.Format(ci, " Push (RTMP)     : {0} frames ({1:0.00} fps), {2:0.00} Mbit/s, avg {3:0.0} KB/frame, {4} keyframes (GOP~{5:0.0})",
                s.WinPushedFrames, pushFps, winMbps, avgFrameKb, s.WinKeyframes, gop));
            sb.AppendLine(string.Format(ci, " Pop (XProtect)  : {0} frames ({1:0.00} fps)", s.WinPoppedFrames, popFps));
            sb.AppendLine(string.Format(ci, " Drops in window : overflow={0}, SEI-only={1}, non-H264={2}",
                s.WinDroppedOverflow, s.WinDroppedSeiOnly, s.WinDroppedNonH264));
            sb.AppendLine(string.Format(ci, " Queue depth     : avg={0:0.0}, max={1}, capacity={2}",
                s.WinAvgQueueDepth, s.WinMaxQueueDepth, BufferCapacity));
            if (s.WinDeltaSamples > 0)
                sb.AppendLine(string.Format(ci, " Inter-frame ms  : avg={0:0.0}, min={1:0.0}, max={2:0.0} ({3} samples)",
                    s.WinAvgDeltaMs, s.WinMinDeltaMs, s.WinMaxDeltaMs, s.WinDeltaSamples));
            else
                sb.AppendLine(" Inter-frame ms  : (no frames in window)");
            if (s.WinPacingSamples > 0)
                sb.AppendLine(string.Format(ci, " Pacing sleep    : avg={0:0.0} ms ({1} sleeps) — driver throttled to publisher rate",
                    s.WinAvgPacingSleepMs, s.WinPacingSamples));
            else
                sb.AppendLine(" Pacing sleep    : 0 (queue empty most of the time, or backlog skipped pacing)");

            sb.AppendLine(" --- Cumulative ---");
            sb.AppendLine(string.Format(ci, " Push total      : {0} frames, {1:0.0} MB",
                s.TotalPushedFrames, s.TotalBytesPushed / 1024.0 / 1024.0));
            sb.AppendLine(string.Format(ci, " Pop total       : {0} frames (gap to push: {1})",
                s.TotalPoppedFrames, s.TotalPushedFrames - s.TotalPoppedFrames));
            sb.AppendLine(string.Format(ci, " Drops total     : overflow={0}, SEI-only={1}, non-H264={2}",
                s.TotalDroppedOverflow, s.TotalDroppedSeiOnly, s.TotalDroppedNonH264));
            sb.Append("=========================================================");
            return sb.ToString();
        }

        private void ResetWindowUnsafe()
        {
            _winPushedFrames = 0;
            _winPoppedFrames = 0;
            _winKeyframes = 0;
            _winDroppedOverflow = 0;
            _winDroppedSeiOnly = 0;
            _winDroppedNonH264 = 0;
            _winBytesPushed = 0;
            _winMaxQueueDepth = 0;
            _winQueueDepthSum = 0;
            _winQueueDepthSamples = 0;
            _winMinDeltaMs = 0;
            _winMaxDeltaMs = 0;
            _winSumDeltaMs = 0;
            _winDeltaSamples = 0;
            _winSumPacingSleepMs = 0;
            _winPacingSamples = 0;
        }

        private void ResetCumulativeUnsafe()
        {
            _totalPushedFrames = 0;
            _totalPoppedFrames = 0;
            _totalKeyframes = 0;
            _totalDroppedOverflow = 0;
            _totalDroppedSeiOnly = 0;
            _totalDroppedNonH264 = 0;
            _totalBytesPushed = 0;
        }

        private struct Snapshot
        {
            public string StreamPath;
            public string Publisher;
            public DateTime PublishStartUtc;
            public DateTime NowUtc;
            public DateTime WindowStartUtc;
            public string VideoCodec;
            public int SourceWidth;
            public int SourceHeight;
            public double SourceFps;
            public double SourceVideoKbps;
            public double SourceAudioKbps;
            public string AudioCodec;
            public double AudioSampleRate;
            public bool HaveSourceMetadata;
            public long TotalPushedFrames;
            public long TotalPoppedFrames;
            public long TotalKeyframes;
            public long TotalDroppedOverflow;
            public long TotalDroppedSeiOnly;
            public long TotalDroppedNonH264;
            public long TotalBytesPushed;
            public long WinPushedFrames;
            public long WinPoppedFrames;
            public long WinKeyframes;
            public long WinDroppedOverflow;
            public long WinDroppedSeiOnly;
            public long WinDroppedNonH264;
            public long WinBytesPushed;
            public int WinMaxQueueDepth;
            public double WinAvgQueueDepth;
            public double WinMinDeltaMs;
            public double WinAvgDeltaMs;
            public double WinMaxDeltaMs;
            public int WinDeltaSamples;
            public double WinAvgPacingSleepMs;
            public int WinPacingSamples;
            public bool IsFinal;
        }
    }
}
