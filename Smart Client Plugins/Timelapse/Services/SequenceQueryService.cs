using System;
using System.Collections.Generic;
using System.Linq;
using CommunitySDK;
using VideoOS.Platform;
using VideoOS.Platform.Data;

namespace Timelapse.Services
{
    /// <summary>
    /// A single recorded block on a camera, clipped to the query window.
    /// </summary>
    internal sealed class RecordingSegment
    {
        public DateTime Start { get; }
        public DateTime End { get; }
        public TimeSpan Duration => End - Start;

        public RecordingSegment(DateTime start, DateTime end)
        {
            Start = start;
            End = end;
        }
    }

    /// <summary>
    /// Wraps VideoOS.Platform.Data.SequenceDataSource with
    /// DataType.SequenceTypeGuids.RecordingSequence to answer:
    ///   - does this camera have any recording inside [from, to]?
    ///   - what are the exact start/end timestamps of every recording block inside [from, to]?
    /// Treat as one-use; dispose when done.
    /// </summary>
    internal sealed class SequenceQuery : IDisposable
    {
        private static readonly PluginLog Log = new PluginLog("Timelapse.SequenceQuery");

        private SequenceDataSource _source;
        private readonly string _cameraName;
        private bool _disposed;

        public SequenceQuery(Item cameraItem)
        {
            _cameraName = cameraItem?.Name ?? "?";
            _source = new SequenceDataSource(cameraItem);
            _source.Init();
        }

        // MIP SequenceDataSource operates in UTC. The UI supplies local DateTimes with
        // Kind=Unspecified, so we normalize in both directions here.
        private const int MaxSequencesPerQuery = 10000;

        private static DateTime ToUtc(DateTime dt)
        {
            if (dt.Kind == DateTimeKind.Utc) return dt;
            // Unspecified → treat as local
            return DateTime.SpecifyKind(dt, DateTimeKind.Local).ToUniversalTime();
        }

        private static DateTime FromUtc(DateTime utc)
        {
            if (utc.Kind == DateTimeKind.Unspecified)
                utc = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
            return utc.ToLocalTime();
        }

        /// <summary>
        /// Returns every RecordingSequence block that overlaps [from, to], clipped to that window.
        /// Returns an empty list if the server has nothing in that range. <paramref name="from"/>
        /// and <paramref name="to"/> are local time (as produced by the UI); returned segments
        /// are also in local time.
        /// </summary>
        public IReadOnlyList<RecordingSegment> GetRecordingSegments(DateTime from, DateTime to)
        {
            if (_disposed) return Array.Empty<RecordingSegment>();
            if (to <= from) return Array.Empty<RecordingSegment>();

            var fromUtc = ToUtc(from);
            var toUtc = ToUtc(to);

            Log.Info($"GetRecordingSegments cam='{_cameraName}' fromLocal={from:O} toLocal={to:O} fromUtc={fromUtc:O} toUtc={toUtc:O}");

            try
            {
                // Anchor at 'from' but look back far enough to catch a sequence that started
                // earlier and runs into the window. 'maxCountBefore=1' keeps it cheap.
                var raw = _source.GetData(
                    fromUtc,
                    TimeSpan.FromDays(30), 1,
                    toUtc - fromUtc, MaxSequencesPerQuery,
                    DataType.SequenceTypeGuids.RecordingSequence);

                int rawCount = raw?.Count ?? 0;
                Log.Info($"GetData returned {rawCount} raw entries for cam='{_cameraName}'");

                if (raw == null || raw.Count == 0)
                    return Array.Empty<RecordingSegment>();

                int dropNoEventSeq = 0, dropZeroDur = 0, dropOutside = 0, clipped = 0;
                var result = new List<RecordingSegment>(raw.Count);
                foreach (var obj in raw)
                {
                    if (!(obj is SequenceData sd) || sd.EventSequence == null) { dropNoEventSeq++; continue; }
                    var s = FromUtc(sd.EventSequence.StartDateTime);
                    var e = FromUtc(sd.EventSequence.EndDateTime);
                    if (e <= s) { dropZeroDur++; continue; }

                    if (e <= from || s >= to) { dropOutside++; continue; }

                    bool didClip = false;
                    if (s < from) { s = from; didClip = true; }
                    if (e > to) { e = to; didClip = true; }
                    if (e <= s) { dropZeroDur++; continue; }
                    if (didClip) clipped++;

                    result.Add(new RecordingSegment(s, e));
                }

                result.Sort((a, b) => a.Start.CompareTo(b.Start));
                Log.Info($"cam='{_cameraName}' kept={result.Count} (dropNoEventSeq={dropNoEventSeq}, dropZeroDur={dropZeroDur}, dropOutside={dropOutside}, clipped={clipped})");
                if (result.Count > 0)
                    Log.Info($"cam='{_cameraName}' first={result[0].Start:O}..{result[0].End:O} last={result[result.Count - 1].Start:O}..{result[result.Count - 1].End:O}");

                return result;
            }
            catch (Exception ex)
            {
                Log.Error($"GetRecordingSegments failed cam='{_cameraName}'", ex);
                return Array.Empty<RecordingSegment>();
            }
        }

        /// <summary>
        /// Short-circuit existence check. Same as GetRecordingSegments(from,to).Count > 0
        /// but stops at the first hit.
        /// </summary>
        public bool AnyRecording(DateTime from, DateTime to)
        {
            if (_disposed) return false;
            if (to <= from) return false;

            var fromUtc = ToUtc(from);
            var toUtc = ToUtc(to);

            try
            {
                var raw = _source.GetData(
                    fromUtc,
                    TimeSpan.FromDays(30), 1,
                    toUtc - fromUtc, 1,
                    DataType.SequenceTypeGuids.RecordingSequence);
                if (raw == null || raw.Count == 0)
                {
                    Log.Info($"AnyRecording cam='{_cameraName}' from={from:O} to={to:O} -> false (empty)");
                    return false;
                }

                foreach (var obj in raw)
                {
                    if (!(obj is SequenceData sd) || sd.EventSequence == null) continue;
                    var s = FromUtc(sd.EventSequence.StartDateTime);
                    var e = FromUtc(sd.EventSequence.EndDateTime);
                    if (e > from && s < to)
                    {
                        Log.Info($"AnyRecording cam='{_cameraName}' -> true (hit {s:O}..{e:O})");
                        return true;
                    }
                }
                Log.Info($"AnyRecording cam='{_cameraName}' -> false (lookback hit but no overlap)");
                return false;
            }
            catch (Exception ex)
            {
                Log.Error($"AnyRecording failed cam='{_cameraName}'", ex);
                return false;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { _source?.Close(); } catch { }
            _source = null;
        }
    }

    /// <summary>
    /// Pure timestamp-list generators. No MIP dependencies - unit-testable.
    /// </summary>
    internal static class TimestampGenerator
    {
        /// <summary>
        /// Continuous mode: walk each segment at <paramref name="interval"/> spacing.
        /// Gaps between segments are naturally skipped.
        /// </summary>
        public static List<DateTime> GenerateContinuous(
            IReadOnlyList<RecordingSegment> segments,
            TimeSpan interval)
        {
            var list = new List<DateTime>();
            if (segments == null || segments.Count == 0) return list;
            if (interval <= TimeSpan.Zero) interval = TimeSpan.FromSeconds(1);

            foreach (var seg in segments)
            {
                for (var t = seg.Start; t <= seg.End; t += interval)
                    list.Add(t);
            }
            return list;
        }

        /// <summary>
        /// Event-based: merge near-adjacent segments, then per event emit the first frame plus
        /// one frame every <paramref name="interval"/>, capped to <paramref name="maxPerEvent"/>
        /// and filled up to <paramref name="minPerEvent"/>.
        /// </summary>
        public static List<DateTime> GenerateEventBased(
            IReadOnlyList<RecordingSegment> segments,
            TimeSpan interval,
            int maxPerEvent,
            int minPerEvent,
            TimeSpan mergeGap)
        {
            var list = new List<DateTime>();
            if (segments == null || segments.Count == 0) return list;
            if (interval <= TimeSpan.Zero) interval = TimeSpan.FromSeconds(1);
            if (maxPerEvent < 1) maxPerEvent = 1;
            if (minPerEvent < 1) minPerEvent = 1;
            if (minPerEvent > maxPerEvent) minPerEvent = maxPerEvent;
            if (mergeGap < TimeSpan.Zero) mergeGap = TimeSpan.Zero;

            var merged = MergeAdjacent(segments, mergeGap);

            foreach (var ev in merged)
            {
                // Always include the first frame.
                var eventStamps = new List<DateTime> { ev.Start };

                // Walk at 'interval' until we hit the cap or run off the end.
                var t = ev.Start + interval;
                while (t <= ev.End && eventStamps.Count < maxPerEvent)
                {
                    eventStamps.Add(t);
                    t += interval;
                }

                // If we didn't reach minPerEvent (very short event), distribute evenly inside.
                if (eventStamps.Count < minPerEvent)
                {
                    eventStamps.Clear();
                    int n = Math.Min(minPerEvent, maxPerEvent);
                    if (n == 1)
                    {
                        eventStamps.Add(ev.Start);
                    }
                    else
                    {
                        var span = ev.End - ev.Start;
                        for (int i = 0; i < n; i++)
                        {
                            var frac = (double)i / (n - 1);
                            eventStamps.Add(ev.Start + TimeSpan.FromTicks((long)(span.Ticks * frac)));
                        }
                    }
                }

                list.AddRange(eventStamps);
            }

            return list;
        }

        /// <summary>
        /// Merges segments whose gap is &lt;= <paramref name="mergeGap"/> into one event.
        /// Assumes input sorted by Start.
        /// </summary>
        public static List<RecordingSegment> MergeAdjacent(
            IReadOnlyList<RecordingSegment> segments,
            TimeSpan mergeGap)
        {
            var result = new List<RecordingSegment>();
            if (segments == null || segments.Count == 0) return result;

            var curStart = segments[0].Start;
            var curEnd = segments[0].End;

            for (int i = 1; i < segments.Count; i++)
            {
                var s = segments[i];
                if (s.Start - curEnd <= mergeGap)
                {
                    if (s.End > curEnd) curEnd = s.End;
                }
                else
                {
                    result.Add(new RecordingSegment(curStart, curEnd));
                    curStart = s.Start;
                    curEnd = s.End;
                }
            }
            result.Add(new RecordingSegment(curStart, curEnd));
            return result;
        }

        /// <summary>
        /// Union of multiple cameras' segment lists into a single merged timeline.
        /// Used for event-mode multi-camera, where all cells share one wall-clock timeline.
        /// </summary>
        public static List<RecordingSegment> Union(IEnumerable<IReadOnlyList<RecordingSegment>> perCamera)
        {
            var all = new List<RecordingSegment>();
            foreach (var list in perCamera)
            {
                if (list == null) continue;
                all.AddRange(list);
            }
            if (all.Count == 0) return all;

            all.Sort((a, b) => a.Start.CompareTo(b.Start));
            return MergeAdjacent(all, TimeSpan.Zero);
        }

        /// <summary>
        /// Sum of segment durations - used for "total recorded time" in the preflight panel.
        /// </summary>
        public static TimeSpan TotalDuration(IReadOnlyList<RecordingSegment> segments)
        {
            if (segments == null) return TimeSpan.Zero;
            long ticks = 0;
            foreach (var s in segments) ticks += s.Duration.Ticks;
            return TimeSpan.FromTicks(ticks);
        }

        /// <summary>
        /// Clips each segment to a per-day time window. <paramref name="dailyStart"/> and
        /// <paramref name="dailyEnd"/> are wall-clock times-of-day. If dailyEnd &lt;= dailyStart,
        /// the window wraps midnight (e.g. 22:00-06:00 means 22:00 -> next day 06:00).
        /// A segment that spans multiple days is split into one piece per day.
        /// </summary>
        public static List<RecordingSegment> ClipToDailyWindow(
            IReadOnlyList<RecordingSegment> segments,
            TimeSpan dailyStart,
            TimeSpan dailyEnd)
        {
            var result = new List<RecordingSegment>();
            if (segments == null || segments.Count == 0) return result;
            if (dailyStart < TimeSpan.Zero) dailyStart = TimeSpan.Zero;
            if (dailyEnd < TimeSpan.Zero) dailyEnd = TimeSpan.Zero;
            if (dailyStart >= TimeSpan.FromDays(1)) dailyStart = TimeSpan.FromDays(1) - TimeSpan.FromMinutes(1);
            if (dailyEnd > TimeSpan.FromDays(1)) dailyEnd = TimeSpan.FromDays(1);

            // Build the list of [windowStart, windowEnd] intervals that cover any segment.
            // Day 'd' contributes one interval; if wrapping, day 'd' contributes
            //   [d + dailyStart, d+1 + dailyEnd]
            // otherwise [d + dailyStart, d + dailyEnd].
            bool wraps = dailyEnd <= dailyStart;

            foreach (var seg in segments)
            {
                // Iterate over each calendar day touched by [seg.Start, seg.End], plus one
                // day before to catch a wrapping window that started the previous day.
                var firstDay = seg.Start.Date.AddDays(wraps ? -1 : 0);
                var lastDay = seg.End.Date;

                for (var day = firstDay; day <= lastDay; day = day.AddDays(1))
                {
                    DateTime winStart, winEnd;
                    if (wraps)
                    {
                        winStart = day + dailyStart;
                        winEnd = day.AddDays(1) + dailyEnd;
                    }
                    else
                    {
                        winStart = day + dailyStart;
                        winEnd = day + dailyEnd;
                    }

                    var s = seg.Start > winStart ? seg.Start : winStart;
                    var e = seg.End < winEnd ? seg.End : winEnd;
                    if (e > s) result.Add(new RecordingSegment(s, e));
                }
            }

            result.Sort((a, b) => a.Start.CompareTo(b.Start));
            return result;
        }

        /// <summary>
        /// True if <paramref name="ts"/> falls inside any segment. O(log n) via binary search.
        /// </summary>
        public static bool Covers(IReadOnlyList<RecordingSegment> segments, DateTime ts)
        {
            if (segments == null || segments.Count == 0) return false;
            int lo = 0, hi = segments.Count - 1;
            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                var s = segments[mid];
                if (ts < s.Start) hi = mid - 1;
                else if (ts > s.End) lo = mid + 1;
                else return true;
            }
            return false;
        }
    }
}
