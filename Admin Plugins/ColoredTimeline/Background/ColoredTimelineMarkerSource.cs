using CommunitySDK;
using FontAwesome5;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;
using VideoOS.Platform;
using VideoOS.Platform.Client;
using VideoOS.Platform.Proxy.Alarm;
using VideoOS.Platform.Proxy.AlarmClient;
using VideoOS.Platform.Util;

namespace ColoredTimeline.Background
{
    // Marker variant of the timeline source: stamps an FA icon at each Start (or Stop)
    // timestamp from EventLog and shows MarkerPreviewControl on hover with rule name,
    // event display name, camera, and timestamp.
    public class ColoredTimelineMarkerSource : TimelineSequenceSource, IDisposable
    {
        private readonly object _lock = new object();
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private IAlarmClient _alarmClient;

        private readonly PluginLog _log = new PluginLog("ColoredTimeline - MarkerSource");

        // TimelineDataArea.Id is typed as object in this SDK build, so we key on object too.
        // Smart Client gives us back the same instance via GetPreviewWpfUserControl(dataId).
        private readonly ConcurrentDictionary<object, MarkerInfo> _markers =
            new ConcurrentDictionary<object, MarkerInfo>();

        public FQID CameraFqid { get; }
        public string EventName { get; }       // raw EventLog message
        public MarkerKind Kind { get; }
        public string RuleName { get; }
        public string CameraName { get; }
        public string EventDisplayName { get; }
        public System.Windows.Media.Color AccentColor { get; }

        public override Guid Id { get; }
        public override string Title { get; }
        public override TimelineSequenceSourceType SourceType => TimelineSequenceSourceType.Marker;
        public override System.Windows.Media.Imaging.BitmapSource MarkerIconSource { get; }

        internal ColoredTimelineMarkerSource(FQID cameraFqid, string cameraName,
            ColoredTimelineSmartClientBackgroundPlugin.RuleConfig rule,
            MarkerKind kind, string eventDisplayName)
        {
            Id = Guid.NewGuid();
            Title = (rule.Name ?? "Rule") + " " + (kind == MarkerKind.Start ? "Start" : "Stop");
            CameraFqid = cameraFqid;
            CameraName = cameraName ?? "";
            RuleName = rule.Name ?? "";
            Kind = kind;
            EventName = (kind == MarkerKind.Start ? rule.StartEvent : rule.StopEvent) ?? "";
            EventDisplayName = eventDisplayName ?? EventName;

            var iconName = kind == MarkerKind.Start ? rule.StartIcon : rule.StopIcon;
            var colorHex = kind == MarkerKind.Start ? rule.StartIconColor : rule.StopIconColor;

            EFontAwesomeIcon icon;
            if (!MarkerIconRenderer.TryParseIcon(iconName, out icon))
                icon = kind == MarkerKind.Start ? EFontAwesomeIcon.Solid_Play : EFontAwesomeIcon.Solid_Stop;

            var ruleAccent = System.Windows.Media.Color.FromArgb(
                rule.Color.A, rule.Color.R, rule.Color.G, rule.Color.B);
            AccentColor = MarkerIconRenderer.ParseColor(colorHex, ruleAccent);

            try
            {
                MarkerIconSource = MarkerIconRenderer.Render(icon, AccentColor, 16);
                _log.Info($"Created marker source '{Title}' icon={icon} color=#{AccentColor.R:X2}{AccentColor.G:X2}{AccentColor.B:X2} event='{EventName}' cam={cameraFqid.ObjectId}");
            }
            catch (Exception ex)
            {
                _log.Error($"Failed to render marker icon for '{Title}': {ex.GetType().FullName}: {ex.Message}");
                throw;
            }
        }

        public override void StartGetSequences(IEnumerable<TimeInterval> intervals)
        {
            if (string.IsNullOrEmpty(EventName)) return;

            foreach (var interval in intervals)
            {
                Task.Run(() => QueryAndPublish(interval), _cts.Token)
                    .ContinueWith(t =>
                    {
                        if (t.Exception != null)
                            _log.Error($"Marker task failed for '{Title}': {t.Exception.GetBaseException().Message}");
                    }, TaskContinuationOptions.OnlyOnFaulted);
            }
        }

        public override UserControl GetPreviewWpfUserControl(object dataId)
        {
            if (dataId != null && _markers.TryGetValue(dataId, out var info))
                return new MarkerPreviewControl(info);
            // Fallback - dataId unknown but Smart Client still wants something to render.
            return new MarkerPreviewControl(new MarkerInfo
            {
                RuleName = RuleName,
                Kind = Kind,
                EventName = EventName,
                EventDisplayName = EventDisplayName,
                CameraName = CameraName,
                Timestamp = DateTime.UtcNow,
                AccentColor = AccentColor
            });
        }

        private void QueryAndPublish(TimeInterval interval)
        {
            try
            {
                EnsureAlarmClient();
                if (_alarmClient == null) return;

                if (_cts.IsCancellationRequested) return;

                EventLine[] events;
                try
                {
                    events = _alarmClient.GetEventLines(0, int.MaxValue, BuildFilter(interval))
                             ?? Array.Empty<EventLine>();
                }
                catch (OperationCanceledException) { return; }
                catch (ObjectDisposedException) { return; }
                catch (AggregateException aex) when (aex.InnerExceptions.All(
                    e => e is OperationCanceledException || e is ObjectDisposedException))
                { return; }
                catch (Exception ex)
                {
                    if (ex is AggregateException agg)
                    {
                        foreach (var ie in agg.Flatten().InnerExceptions)
                            _log.Error($"GetEventLines failed for '{Title}': {ie.GetType().FullName}: {ie.Message}");
                    }
                    else
                    {
                        _log.Error($"GetEventLines failed for '{Title}': {ex.GetType().FullName}: {ex.Message}");
                    }
                    lock (_lock) _alarmClient = null;
                    return;
                }

                _log.Info($"Marker '{Title}' cam={CameraFqid.ObjectId} window=" +
                          $"{interval.StartTime.ToLocalTime():HH:mm:ss}..{interval.EndTime.ToLocalTime():HH:mm:ss} -> {events.Length} marker(s)");

                var areas = new List<TimelineDataArea>(events.Length);
                foreach (var e in events)
                {
                    var area = new TimelineDataArea(new TimeInterval(e.Timestamp, e.Timestamp));
                    var info = new MarkerInfo
                    {
                        RuleName = RuleName,
                        Kind = Kind,
                        EventName = EventName,
                        EventDisplayName = EventDisplayName,
                        CameraName = CameraName,
                        Timestamp = e.Timestamp,
                        AccentColor = AccentColor
                    };

                    // TimelineDataArea.Id is null on this SDK build, which would throw
                    // ArgumentNullException on the Dictionary indexer. Try assigning a Guid
                    // ourselves (settable on some SDK versions); guard the write so the
                    // marker is still emitted even if we can't key the per-marker info.
                    object key = null;
                    try
                    {
                        if (area.Id == null)
                        {
                            var idProp = area.GetType().GetProperty("Id");
                            if (idProp != null && idProp.CanWrite)
                                idProp.SetValue(area, Guid.NewGuid());
                        }
                        key = area.Id;
                    }
                    catch { }

                    if (key != null) _markers[key] = info;
                    areas.Add(area);
                }

                var result = new TimelineSourceQueryResult(interval) { Sequences = areas };
                OnSequencesRetrieved(new List<TimelineSourceQueryResult> { result });
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _log.Error($"Marker QueryAndPublish failed for '{Title}': {ex.Message}");
            }
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

        private EventFilter BuildFilter(TimeInterval interval)
        {
            return new EventFilter
            {
                Conditions = new[]
                {
                    new Condition { Target = Target.CameraId,  Operator = Operator.Equals,      Value = CameraFqid.ObjectId },
                    new Condition { Target = Target.Message,   Operator = Operator.Equals,      Value = EventName },
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
            try { _cts.Cancel(); } catch { }
        }
    }
}
