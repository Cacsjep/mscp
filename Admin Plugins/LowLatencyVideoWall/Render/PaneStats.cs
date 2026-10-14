using System;
using System.Diagnostics;
using System.Threading;

namespace LowLatencyVideoWall.Render
{
    /// <summary>
    /// What one pane looks like right now, as a value, so the UI can sample 48 of these on a
    /// timer without holding anything the render thread needs.
    /// </summary>
    public struct PaneStatsSnapshot
    {
        public Guid CameraId;
        public string CameraName;
        public PaneState State;
        public string Error;

        /// <summary>Where the pane is on the wall, in backbuffer pixels.</summary>
        public PixelRect Rect;

        /// <summary>What was asked of the camera, and what it actually sent.</summary>
        public string RequestedResolution;
        public int SourceWidth;
        public int SourceHeight;
        public bool IsHardwareDecoded;
        public string DecoderName;

        /// <summary>Codec and profile as the decoder read them out of the SPS, empty until then.</summary>
        public string Codec;

        public long VideoPackets;
        public long KeyFrames;
        public double MbitPerSecond;

        /// <summary>
        /// The last complete group of pictures, in frames and in Mbit/s over its own duration.
        /// Both are 0 until two key frames have been seen. Worth its own line next to the
        /// session average: a camera that silently widened its GOP is the usual explanation
        /// for a pane that takes seconds to recover from a reconnect.
        /// </summary>
        public int LastGopFrames;
        public double LastGopMbitPerSecond;

        public long FramesDecoded;
        public long DecodeErrors;
        public double DecodeFps;
        public double AvgDecodeMs;

        /// <summary>Frames replaced in the slot before the render thread took them.</summary>
        public long FramesDropped;

        /// <summary>Blits that carried a frame this pane had not shown before.</summary>
        public long UniqueBlits;
        public double UniqueFps;

        public double AvgLatencyMs;
        public double PeakLatencyMs;

        /// <summary>Milliseconds from Start to this pane's first blit, 0 until it happens.</summary>
        public double TimeToFirstFrameMs;
    }

    /// <summary>
    /// Per pane counters, shared between that pane's decode thread, the one render thread and
    /// the UI timer. Written with Interlocked and read without a lock: the UI is sampling for
    /// display, so a torn read across two unrelated counters is harmless.
    ///
    /// One of these per pane rather than one for the wall, because the interesting failure on
    /// a 48 camera wall is one camera misbehaving, and a wall-wide average hides exactly that.
    /// </summary>
    public sealed class PaneStats
    {
        private static readonly double TicksPerMs = Stopwatch.Frequency / 1000.0;

        private long _videoPackets;
        private long _videoBytes;
        private long _keyFrames;

        private long _framesDecoded;
        private long _decodeErrors;
        private long _decodeTicks;
        private long _decodeSamples;

        private long _framesDropped;

        private long _uniqueBlits;

        private long _latencyTicks;
        private long _latencySamples;
        private long _latencyPeakTicks;

        // The group of pictures being accumulated. Decode thread only, so no interlocks: it
        // is published into the three fields below when the next key frame closes it.
        private long _gopPackets;
        private long _gopBytes;
        private long _gopStartTicks;

        private long _lastGopPackets;
        private long _lastGopBytes;
        private long _lastGopTicks;

        /// <summary>
        /// A rate meter smooths against its own previous call, so it belongs to exactly one
        /// reader. There are two: the UI timer sampling every pane for the status strip, and
        /// the render thread's pane overlay. Sharing one set would give both of them the
        /// interval between whichever call came last, which is neither reader's interval.
        /// </summary>
        private readonly RateSet _uiRates = new RateSet();
        private readonly RateSet _overlayRates = new RateSet();

        internal void AddVideoPacket(int bytes, bool isKeyFrame)
        {
            Interlocked.Increment(ref _videoPackets);
            Interlocked.Add(ref _videoBytes, bytes);

            if (isKeyFrame)
            {
                Interlocked.Increment(ref _keyFrames);
                CloseGop();
            }

            _gopPackets++;
            _gopBytes += bytes;
        }

        /// <summary>
        /// Publishes the GOP that just ended. The first key frame of a session only starts the
        /// clock, so nothing is published until a second one arrives.
        /// </summary>
        private void CloseGop()
        {
            var now = Stopwatch.GetTimestamp();

            if (_gopStartTicks != 0 && _gopPackets > 0)
            {
                Interlocked.Exchange(ref _lastGopPackets, _gopPackets);
                Interlocked.Exchange(ref _lastGopBytes, _gopBytes);
                Interlocked.Exchange(ref _lastGopTicks, now - _gopStartTicks);
            }

            _gopStartTicks = now;
            _gopPackets = 0;
            _gopBytes = 0;
        }

        internal void AddDecodedFrame(long elapsedTicks)
        {
            Interlocked.Increment(ref _framesDecoded);
            Interlocked.Add(ref _decodeTicks, elapsedTicks);
            Interlocked.Increment(ref _decodeSamples);
        }

        internal void AddDecodeError() => Interlocked.Increment(ref _decodeErrors);
        internal void AddDroppedFrame() => Interlocked.Increment(ref _framesDropped);
        internal void AddUniqueBlit() => Interlocked.Increment(ref _uniqueBlits);

        internal void AddLatency(long ticks)
        {
            Interlocked.Add(ref _latencyTicks, ticks);
            Interlocked.Increment(ref _latencySamples);

            // Peak is advisory, so losing this race only loses one sample.
            var peak = Interlocked.Read(ref _latencyPeakTicks);
            if (ticks > peak) Interlocked.CompareExchange(ref _latencyPeakTicks, ticks, peak);
        }

        /// <summary>Clears the per connection counters. Called when a session starts.</summary>
        internal void ResetSession()
        {
            Interlocked.Exchange(ref _videoPackets, 0);
            Interlocked.Exchange(ref _videoBytes, 0);
            Interlocked.Exchange(ref _keyFrames, 0);
            Interlocked.Exchange(ref _framesDecoded, 0);
            Interlocked.Exchange(ref _decodeErrors, 0);
            Interlocked.Exchange(ref _framesDropped, 0);
            Interlocked.Exchange(ref _uniqueBlits, 0);
            Interlocked.Exchange(ref _lastGopPackets, 0);
            Interlocked.Exchange(ref _lastGopBytes, 0);
            Interlocked.Exchange(ref _lastGopTicks, 0);
            _gopStartTicks = 0;
            _gopPackets = 0;
            _gopBytes = 0;
            ResetAverages();
        }

        internal void ResetAverages()
        {
            Interlocked.Exchange(ref _decodeTicks, 0);
            Interlocked.Exchange(ref _decodeSamples, 0);
            Interlocked.Exchange(ref _latencyTicks, 0);
            Interlocked.Exchange(ref _latencySamples, 0);
            Interlocked.Exchange(ref _latencyPeakTicks, 0);
        }

        /// <summary>
        /// Fills in everything this object knows. The identity and geometry fields are left
        /// for <see cref="WallPane"/> to add.
        ///
        /// The rates are smoothed from the last call, so this has to be called from one thread
        /// on an interval: the UI timer, not the render thread.
        /// </summary>
        internal PaneStatsSnapshot Read() => Read(_uiRates);

        /// <summary>
        /// The same numbers for the pane overlay, which samples from the render thread on its
        /// own interval. See <see cref="_uiRates"/> for why that needs a second set of meters.
        /// </summary>
        internal PaneStatsSnapshot ReadOverlay() => Read(_overlayRates);

        private PaneStatsSnapshot Read(RateSet rates)
        {
            var decodeSamples = Interlocked.Read(ref _decodeSamples);
            var latencySamples = Interlocked.Read(ref _latencySamples);
            var gopTicks = Interlocked.Read(ref _lastGopTicks);

            return new PaneStatsSnapshot
            {
                VideoPackets = Interlocked.Read(ref _videoPackets),
                KeyFrames = Interlocked.Read(ref _keyFrames),
                MbitPerSecond = rates.Bytes.Update(Interlocked.Read(ref _videoBytes)) * 8.0 / 1000000.0,

                LastGopFrames = (int)Interlocked.Read(ref _lastGopPackets),
                LastGopMbitPerSecond = gopTicks <= 0
                    ? 0
                    : Interlocked.Read(ref _lastGopBytes) * 8.0 * Stopwatch.Frequency /
                      gopTicks / 1000000.0,

                FramesDecoded = Interlocked.Read(ref _framesDecoded),
                DecodeErrors = Interlocked.Read(ref _decodeErrors),
                DecodeFps = rates.Decode.Update(Interlocked.Read(ref _framesDecoded)),
                AvgDecodeMs = decodeSamples == 0
                    ? 0
                    : Interlocked.Read(ref _decodeTicks) / (double)decodeSamples / TicksPerMs,

                FramesDropped = Interlocked.Read(ref _framesDropped),

                UniqueBlits = Interlocked.Read(ref _uniqueBlits),
                UniqueFps = rates.Unique.Update(Interlocked.Read(ref _uniqueBlits)),

                AvgLatencyMs = latencySamples == 0
                    ? 0
                    : Interlocked.Read(ref _latencyTicks) / (double)latencySamples / TicksPerMs,
                PeakLatencyMs = Interlocked.Read(ref _latencyPeakTicks) / TicksPerMs
            };
        }

        /// <summary>One reader's worth of rate smoothing.</summary>
        private sealed class RateSet
        {
            internal readonly RateMeter Decode = new RateMeter();
            internal readonly RateMeter Unique = new RateMeter();
            internal readonly RateMeter Bytes = new RateMeter();
        }
    }

    /// <summary>Wall level numbers: one render thread, one swapchain, one set of counters.</summary>
    public struct WallStatsSnapshot
    {
        public string AdapterDescription;
        public bool DeviceLost;
        public string LastError;

        public int WallWidth;
        public int WallHeight;

        public int PaneCount;
        public int PanesStreaming;

        /// <summary>Distinct source geometries, which is how many video processors exist.</summary>
        public int VideoProcessors;

        public long Presents;
        public long PresentErrors;
        public double PresentFps;

        /// <summary>Clear plus every pane's blit, so the cost of one whole wall pass.</summary>
        public double AvgPassMs;
        public double AvgPresentMs;

        /// <summary>Blits in the last pass, which is how many panes actually had a picture.</summary>
        public int BlitsPerPass;

        /// <summary>Summed across every pane, so a wall total rather than a per pane figure.</summary>
        public double TotalDecodeFps;
        public double TotalMbitPerSecond;

        public PaneStatsSnapshot[] Panes;
    }

    /// <summary>
    /// Wall level counters. Only the render thread writes these, the UI timer reads them.
    /// </summary>
    internal sealed class WallStats
    {
        private static readonly double TicksPerMs = Stopwatch.Frequency / 1000.0;

        private long _presents;
        private long _presentErrors;
        private long _passTicks;
        private long _presentTicks;
        private long _samples;
        private int _blitsPerPass;

        private readonly RateMeter _presentRate = new RateMeter();

        internal void AddPass(long passTicks, long presentTicks, int blits)
        {
            Interlocked.Increment(ref _presents);
            Interlocked.Add(ref _passTicks, passTicks);
            Interlocked.Add(ref _presentTicks, presentTicks);
            Interlocked.Increment(ref _samples);
            Interlocked.Exchange(ref _blitsPerPass, blits);
        }

        internal void AddPresentError() => Interlocked.Increment(ref _presentErrors);

        internal void ResetAverages()
        {
            Interlocked.Exchange(ref _passTicks, 0);
            Interlocked.Exchange(ref _presentTicks, 0);
            Interlocked.Exchange(ref _samples, 0);
        }

        internal void Fill(ref WallStatsSnapshot snapshot)
        {
            var samples = Interlocked.Read(ref _samples);

            snapshot.Presents = Interlocked.Read(ref _presents);
            snapshot.PresentErrors = Interlocked.Read(ref _presentErrors);
            snapshot.PresentFps = _presentRate.Update(snapshot.Presents);
            snapshot.AvgPassMs = samples == 0
                ? 0
                : Interlocked.Read(ref _passTicks) / (double)samples / TicksPerMs;
            snapshot.AvgPresentMs = samples == 0
                ? 0
                : Interlocked.Read(ref _presentTicks) / (double)samples / TicksPerMs;
            snapshot.BlitsPerPass = Volatile.Read(ref _blitsPerPass);
        }
    }

    /// <summary>
    /// Turns a monotonically increasing counter into a rate. The UI samples at a few Hz, which
    /// is too coarse to read directly at these frame rates, so the result is smoothed.
    /// Only ever touched from the thread that samples.
    /// </summary>
    internal sealed class RateMeter
    {
        private const double Alpha = 0.4;

        private long _lastCount = -1;
        private long _lastTimestamp;
        private double _rate;

        internal double Update(long count)
        {
            var now = Stopwatch.GetTimestamp();

            // First call, or the counter was reset by a new session.
            if (_lastCount < 0 || count < _lastCount)
            {
                _lastCount = count;
                _lastTimestamp = now;
                _rate = 0;
                return 0;
            }

            var elapsed = (now - _lastTimestamp) / (double)Stopwatch.Frequency;
            if (elapsed < 0.05) return _rate;

            var instant = (count - _lastCount) / elapsed;
            _lastCount = count;
            _lastTimestamp = now;
            _rate = _rate <= 0 ? instant : _rate + Alpha * (instant - _rate);
            return _rate;
        }
    }
}
