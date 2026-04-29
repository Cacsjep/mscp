using CommunitySDK;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VideoOS.Platform;
using VideoOS.Platform.Client;
using VideoOS.Platform.Proxy.Alarm;
using VideoOS.Platform.Proxy.AlarmClient;
using VideoOS.Platform.Util;

namespace ColoredTimeline.Background
{
    public class ColoredTimelineSequenceSource : TimelineSequenceSource, IDisposable
    {
        private readonly object _lock = new object();
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private IAlarmClient _alarmClient;

        private readonly PluginLog _log = new PluginLog("ColoredTimeline - SequenceSource");

        public FQID CameraFqid { get; }
        public string StartEvent { get; }
        public string StopEvent { get; }
        public bool AutoCloseEnabled { get; }
        public TimeSpan AutoCloseAfter { get; }

        public override Guid Id { get; }
        public override string Title { get; }
        public override TimelineSequenceSourceType SourceType => TimelineSequenceSourceType.Ribbon;
        public override System.Windows.Media.Brush RibbonContentColorBrush { get; }

        internal ColoredTimelineSequenceSource(FQID cameraFqid, ColoredTimelineSmartClientBackgroundPlugin.RuleConfig rule)
        {
            Id = Guid.NewGuid();
            Title = rule.Name;
            CameraFqid = cameraFqid;
            StartEvent = rule.StartEvent ?? "";
            StopEvent = rule.StopEvent ?? "";
            AutoCloseEnabled = rule.AutoCloseEnabled;
            AutoCloseAfter = rule.AutoCloseAfter;
            RibbonContentColorBrush = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromArgb(rule.Color.A, rule.Color.R, rule.Color.G, rule.Color.B));
            RibbonContentColorBrush.Freeze();
        }

        public override void StartGetSequences(IEnumerable<TimeInterval> intervals)
        {
            if (string.IsNullOrEmpty(StartEvent)) return;
            // Stop event is optional only when AutoClose is on (the rule's timeout closes
            // every unmatched Start). Without a Stop and without AutoClose there's nothing
            // to paint, so bail.
            if (string.IsNullOrEmpty(StopEvent) && !AutoCloseEnabled) return;

            foreach (var interval in intervals)
            {
                Task.Run(() => QueryAndPublish(interval), _cts.Token)
                    .ContinueWith(t =>
                    {
                        if (t.Exception != null)
                            _log.Error($"Sequence task failed for '{Title}': {t.Exception.GetBaseException().Message}");
                    }, TaskContinuationOptions.OnlyOnFaulted);
            }
        }

        private void QueryAndPublish(TimeInterval interval)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                EnsureAlarmClient();
                if (_alarmClient == null) return;

                var startFilter = BuildFilter(StartEvent, interval);
                var hasStopEvent = !string.IsNullOrEmpty(StopEvent);
                var stopFilter = hasStopEvent ? BuildFilter(StopEvent, interval) : null;

                _log.Info($"Query '{Title}' cam={CameraFqid.ObjectId} " +
                          $"start='{StartEvent}' stop='{(hasStopEvent ? StopEvent : "<auto-close>")}' " +
                          $"window={interval.StartTime.ToLocalTime():yyyy-MM-dd HH:mm:ss}..{interval.EndTime.ToLocalTime():HH:mm:ss}");

                if (_cts.IsCancellationRequested) return;

                EventLine[] startEvents;
                EventLine[] stopEvents;
                try
                {
                    var startTask = Task.Run(() => _alarmClient.GetEventLines(0, int.MaxValue, startFilter), _cts.Token);
                    var stopTask = hasStopEvent
                        ? Task.Run(() => _alarmClient.GetEventLines(0, int.MaxValue, stopFilter), _cts.Token)
                        : Task.FromResult(Array.Empty<EventLine>());
                    Task.WaitAll(new Task[] { startTask, stopTask }, _cts.Token);
                    startEvents = startTask.Result;
                    stopEvents = stopTask.Result;
                }
                catch (OperationCanceledException) { return; }
                catch (ObjectDisposedException) { return; }
                catch (AggregateException aex) when (aex.InnerExceptions.All(
                    e => e is OperationCanceledException || e is ObjectDisposedException))
                {
                    return;
                }
                catch (Exception ex)
                {
                    // EdgeMotionTimeline issue #3: WCF FaultException("verifying security") on
                    // first call against some Milestone versions. Discard the cached client and
                    // try once more on the next pan/zoom rather than poisoning this source.
                    if (ex is AggregateException agg)
                    {
                        foreach (var ie in agg.Flatten().InnerExceptions)
                            _log.Error($"GetEventLines failed for '{Title}': {ie.GetType().FullName}: {ie.Message}");
                    }
                    else
                    {
                        _log.Error($"GetEventLines failed for '{Title}': {ex.GetType().FullName}: {ex.Message}");
                    }
                    _log.Error($"  Resetting AlarmClient for '{Title}'.");
                    lock (_lock) _alarmClient = null;
                    return;
                }

                var startCount = startEvents?.Length ?? 0;
                var stopCount = stopEvents?.Length ?? 0;
                _log.Info($"  raw: {startCount} start event(s), {stopCount} stop event(s) for '{Title}' cam={CameraFqid.ObjectId}");

                if (startCount > 0)
                    foreach (var e in startEvents)
                        _log.Info($"    START  [{e.Timestamp.ToLocalTime():HH:mm:ss.fff}] '{e.Message}' (UTC {e.Timestamp:HH:mm:ss.fff})");
                if (stopCount > 0)
                    foreach (var e in stopEvents)
                        _log.Info($"    STOP   [{e.Timestamp.ToLocalTime():HH:mm:ss.fff}] '{e.Message}' (UTC {e.Timestamp:HH:mm:ss.fff})");

                var sequences = PairStartStop(startEvents ?? Array.Empty<EventLine>(),
                                              stopEvents ?? Array.Empty<EventLine>(),
                                              interval, out var pairLog);

                var result = new TimelineSourceQueryResult(interval) { Sequences = sequences };
                OnSequencesRetrieved(new List<TimelineSourceQueryResult> { result });

                foreach (var line in pairLog)
                    _log.Info("    " + line);

                _log.Info($"  -> {sequences.Count} segment(s) paired in {sw.Elapsed.TotalSeconds:N2}s");
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _log.Error($"QueryAndPublish failed for '{Title}': {ex.Message}");
            }
        }

        // When a Start has no matching Stop in the queried window, the rule's AutoClose
        // settings decide whether to paint a short capped segment or skip it entirely.
        // ONVIF analytics events sometimes only fire a Rising edge (no Falling counterpart)
        // or the Falling event hasn't been logged yet when the user pans the timeline.

        private List<TimelineDataArea> PairStartStop(EventLine[] startEvents, EventLine[] stopEvents,
                                                     TimeInterval interval, out List<string> pairLog)
        {
            pairLog = new List<string>();
            var sequences = new List<TimelineDataArea>();
            if (startEvents.Length == 0) return sequences;

            // stopEvents are ordered ascending. Walk start events; for each start, advance the
            // stop pointer to the first stop >= start. Unmatched starts get capped (see above).
            int stopIdx = 0;
            foreach (var start in startEvents)
            {
                var startTs = start.Timestamp;

                // Skip overlapped starts (one already inside the previous segment)
                if (sequences.Count > 0 && startTs < sequences[sequences.Count - 1].Interval.EndTime)
                {
                    pairLog.Add($"SKIP overlapped start [{startTs.ToLocalTime():HH:mm:ss.fff}]");
                    continue;
                }

                while (stopIdx < stopEvents.Length && stopEvents[stopIdx].Timestamp < startTs)
                {
                    pairLog.Add($"SKIP orphan stop  [{stopEvents[stopIdx].Timestamp.ToLocalTime():HH:mm:ss.fff}] (before next start)");
                    stopIdx++;
                }

                DateTime endTs;
                bool open;
                if (stopIdx < stopEvents.Length)
                {
                    endTs = stopEvents[stopIdx].Timestamp;
                    open = false;
                    stopIdx++;
                }
                else
                {
                    // No matching stop. If the rule has AutoClose disabled, drop this start
                    // entirely (no segment painted). Otherwise cap at startTs + AutoCloseAfter,
                    // never past the queried window or the present moment.
                    if (!AutoCloseEnabled)
                    {
                        pairLog.Add($"SKIP unmatched start [{startTs.ToLocalTime():HH:mm:ss.fff}] (auto-close disabled)");
                        continue;
                    }
                    var cap = startTs + AutoCloseAfter;
                    var now = DateTime.UtcNow;
                    endTs = cap;
                    if (endTs > interval.EndTime) endTs = interval.EndTime;
                    if (endTs > now) endTs = now;
                    if (endTs <= startTs) endTs = startTs.AddSeconds(1);
                    open = true;
                }

                if (endTs <= startTs) continue;
                sequences.Add(new TimelineDataArea(new TimeInterval(startTs, endTs)));
                pairLog.Add(open
                    ? $"PAIR  [{startTs.ToLocalTime():HH:mm:ss.fff}] -> [{endTs.ToLocalTime():HH:mm:ss.fff}] (no stop, capped at {AutoCloseAfter.TotalSeconds:N0}s)"
                    : $"PAIR  [{startTs.ToLocalTime():HH:mm:ss.fff}] -> [{endTs.ToLocalTime():HH:mm:ss.fff}]");
            }
            return sequences;
        }

        private void EnsureAlarmClient()
        {
            lock (_lock)
            {
                if (_alarmClient != null) return;
                try
                {
                    _alarmClient = new AlarmClientManager().GetAlarmClient(CameraFqid.ServerId);
                }
                catch (Exception ex)
                {
                    _log.Error($"GetAlarmClient failed for '{Title}': {ex.Message}");
                    _alarmClient = null;
                }
            }
        }

        private EventFilter BuildFilter(string message, TimeInterval interval)
        {
            return new EventFilter
            {
                Conditions = new[]
                {
                    new Condition { Target = Target.CameraId,  Operator = Operator.Equals,      Value = CameraFqid.ObjectId },
                    new Condition { Target = Target.Message,   Operator = Operator.Equals,      Value = message },
                    new Condition { Target = Target.Timestamp, Operator = Operator.GreaterThan, Value = interval.StartTime },
                    new Condition { Target = Target.Timestamp, Operator = Operator.LessThan,    Value = interval.EndTime }
                },
                Orders = new[]
                {
                    new OrderBy { Target = Target.Timestamp, Order = Order.Ascending }
                }
            };
        }

        public void Dispose()
        {
            // Cancel only - don't Dispose. In-flight QueryAndPublish tasks still reference
            // _cts.Token; disposing the CTS while they run would throw ObjectDisposedException.
            try { _cts.Cancel(); } catch { }
        }
    }
}
