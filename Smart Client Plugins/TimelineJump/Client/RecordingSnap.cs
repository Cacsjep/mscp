using System;
using VideoOS.Platform;
using VideoOS.Platform.Data;

namespace TimelineJump.Client
{
    /// <summary>
    /// Snaps a target playback time onto the camera's nearest recorded sequence in the
    /// direction of the jump. If the target already falls inside a recording, returns
    /// it unchanged. If not, jumping forward lands on the start of the next recording,
    /// jumping backward lands on the last instant of the previous recording. Used so a
    /// "+10s" chip in a gap visibly moves the operator to the next event instead of
    /// freezing on the last decoded frame.
    /// </summary>
    internal static class RecordingSnap
    {
        // 30-day window each side of the target. Easily covers chip-scale jumps and
        // even multi-day customs without chasing rare edge cases on huge archives.
        private static readonly TimeSpan LookWindow = TimeSpan.FromDays(30);

        public static DateTime SnapToRecording(Item cameraItem, DateTime target, TimeSpan delta)
        {
            if (cameraItem == null || delta == TimeSpan.Zero) return target;

            // SequenceDataSource is not IDisposable but it does open server-side
            // resources via Init() that we must release with Close() on every path.
            SequenceDataSource src = null;
            try
            {
                src = new SequenceDataSource(cameraItem);
                src.Init();

                // Pull at most one sequence before and one after the target. If the
                // target falls inside an existing sequence the "before" hit will
                // contain it; otherwise we have explicit prev/next neighbours.
                var raw = src.GetData(
                    target,
                    LookWindow, 1,
                    LookWindow, 1,
                    DataType.SequenceTypeGuids.RecordingSequence);
                if (raw == null || raw.Count == 0) return target;

                DateTime? prevEnd = null;
                DateTime? nextStart = null;

                foreach (var obj in raw)
                {
                    if (!(obj is SequenceData sd) || sd.EventSequence == null) continue;
                    var s = sd.EventSequence.StartDateTime;
                    var e = sd.EventSequence.EndDateTime;
                    if (e <= s) continue;

                    if (s <= target && target <= e) return target; // already inside a recording

                    if (e < target)
                    {
                        if (prevEnd == null || e > prevEnd.Value) prevEnd = e;
                    }
                    else if (s > target)
                    {
                        if (nextStart == null || s < nextStart.Value) nextStart = s;
                    }
                }

                if (delta.Ticks > 0 && nextStart.HasValue) return nextStart.Value;
                if (delta.Ticks < 0 && prevEnd.HasValue) return prevEnd.Value;
                return target;
            }
            catch (Exception ex)
            {
                TimelineJumpDefinition.Log.Error("Recording snap failed", ex);
                return target;
            }
            finally
            {
                try { src?.Close(); } catch { }
            }
        }
    }
}
