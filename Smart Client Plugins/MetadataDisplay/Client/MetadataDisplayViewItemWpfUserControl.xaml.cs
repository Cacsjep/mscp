using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CommunitySDK;
using MetadataDisplay.Client.Renderers;
using VideoOS.Platform;
using VideoOS.Platform.Client;
using VideoOS.Platform.Live;
using VideoOS.Platform.Messaging;

namespace MetadataDisplay.Client
{
    public partial class MetadataDisplayViewItemWpfUserControl : ViewItemWpfUserControl
    {
        private static readonly PluginLog _log = new PluginLog("MetadataDisplay");

        private readonly MetadataDisplayViewItemManager _viewItemManager;
        private object _modeChangedReceiver;

        private MetadataLiveSource _liveSource;
        private Item _metadataItem;
        private ExtractorConfig _extractorCfg;
        // Per-series extractor configs for the multi-line chart. Built from the
        // active LineChartConfig.Series whenever the line renderer is (re)built.
        // Live and backfill paths use this list; non-chart renderers continue
        // using the legacy single _extractorCfg above.
        private List<ExtractorConfig> _lineExtractorCfgs;

        private MetadataPlaybackPump _pump;
        private object _playbackTimeReceiver;

        private LampRenderer _lampRenderer;
        private NumberRenderer _numberRenderer;
        private GaugeRenderer _gaugeRenderer;
        private TextRenderer _textRenderer;
        private LineChartRenderer _lineRenderer;
        private TableRenderer _tableRenderer;
        private TrendRenderer _trendRenderer;
        private Base64ImageRenderer _base64ImageRenderer;

        private DateTime? _lastValueUtc;
        // Playback line-chart backfill bookkeeping: track the last cursor we
        // backfilled around so a small scrub just moves the cursor marker, while
        // a big jump (or first entry) triggers a fresh range scan.
        private DateTime? _lastBackfillCursorUtc;
        // Most recent playback cursor seen via PlaybackCurrentTimeIndication.
        // Lets the in-pane window picker immediately refill the chart at the
        // current cursor instead of forcing the user to scrub the timeline.
        private DateTime? _lastPlaybackCursorUtc;
        private System.Threading.CancellationTokenSource _backfillCts;
        // In-pane window quick-picker: session-only override applied on top of
        // the saved LineWindowSeconds. Cleared by the "Default" entry. Persists
        // across mode toggles within this view-item lifetime; cleared on full
        // Reconfigure (Save).
        private int? _sessionWindowSecondsOverride;
        private bool _hasReceivedValue;
        private DispatcherTimer _staleTicker;
        private int _staleSeconds;

        public MetadataDisplayViewItemWpfUserControl(MetadataDisplayViewItemManager viewItemManager)
        {
            _viewItemManager = viewItemManager;
            InitializeComponent();
        }

        public override void Init()
        {
            _log.Info($"[ViewItem] Init id={_viewItemManager?.MetadataId} render={_viewItemManager?.RenderType} mode={EnvironmentManager.Instance.Mode}");
            _modeChangedReceiver = EnvironmentManager.Instance.RegisterReceiver(
                new MessageReceiver(OnModeChanged),
                new MessageIdFilter(MessageId.System.ModeChangedIndication));
            ApplyMode(EnvironmentManager.Instance.Mode);
        }

        public override void Close()
        {
            _log.Info("[ViewItem] Close");
            if (_modeChangedReceiver != null)
            {
                EnvironmentManager.Instance.UnRegisterReceiver(_modeChangedReceiver);
                _modeChangedReceiver = null;
            }
            StopLive();
            StopPlayback();
            StopStaleTicker();
            StopTrendBaselineRefreshTimer();
        }

        // Called by the configuration window after a Save so the rendered widget
        // picks up the new config without needing a mode toggle. Drops any
        // session window override since the user has now changed the saved value
        // and the override would otherwise mask the new setting.
        internal void Reconfigure()
        {
            _sessionWindowSecondsOverride = null;
            ApplyMode(EnvironmentManager.Instance.Mode);
        }

        private void ApplyMode(Mode mode)
        {
            _log.Info($"[ViewItem] ApplyMode mode={mode}");
            ApplyTitleSettings();
            staleBadge.Visibility = Visibility.Collapsed;
            _lastValueUtc = null;

            _extractorCfg = ExtractorConfig.FromManager(_viewItemManager);
            BuildRenderHost();
            ResolveMetadataItem();

            StopLive();
            StopPlayback();
            StopStaleTicker();

            string overlay = ResolveOverlayMessage(mode);
            if (overlay != null)
            {
                if (mode == Mode.ClientSetup)
                    ApplySetupSummary();
                else
                    setupHint.Text = overlay;
                openConfigButton.Visibility = mode == Mode.ClientSetup ? Visibility.Visible : Visibility.Collapsed;
                setupPanel.Visibility = Visibility.Visible;
                renderViewbox.Visibility = Visibility.Collapsed;
                chartRoot.Visibility = Visibility.Collapsed;
                noDataPanel.Visibility = Visibility.Collapsed;
                UpdateExportBadgeVisibility();
                return;
            }

            setupPanel.Visibility = Visibility.Collapsed;
            _hasReceivedValue = false;

            // LineChart and Table go into the native-resolution chartRoot; other
            // renderers use the 320-wide renderViewbox. Show the chart immediately
            // (axes + threshold bands are useful empty); other renderers stay
            // hidden behind the "Waiting for data" overlay until a value arrives.
            bool isChart = IsArchiveBackedRenderer(_viewItemManager.RenderType);
            renderViewbox.Visibility = Visibility.Collapsed;
            // For non-chart renderers we show the empty chart frame; for chart
            // renderers we hide the chart and show the spinner overlay so the
            // pane has visible feedback while the cold-start backfill runs.
            // ShowLoadingOverlay/StartLive paths flip this back when ready.
            chartRoot.Visibility = Visibility.Collapsed;
            noDataPanel.Visibility = Visibility.Visible;
            // Heading toggles between "Loading..." (active backfill) and
            // "Waiting for data..." (live / playback waiting state) so the
            // detail line below doesn't visually collide with a contradicting
            // header - e.g. "Waiting for data..." sitting above "Loading from
            // archive..." reads as two messages stacked.
            bool isLoadingState = mode == Mode.ClientPlayback && isChart;
            noDataHeading.Text = isLoadingState ? "Loading..." : "Waiting for data...";
            noDataDetail.Text = mode == Mode.ClientPlayback
                ? (isChart ? "Loading recent values from archive..."
                           : "Move the playback cursor to a time when this metadata was recorded.")
                : $"Subscribed to {_metadataItem?.Name ?? "channel"}. Waiting for first matching packet.";

            if (mode == Mode.ClientLive)
            {
                // For archive-backed renderers (LineChart, Table) with a
                // non-trivial window we backfill from the archive first so the
                // user sees recent history right away instead of waiting N
                // minutes for it to fill. StartLive runs after the backfill
                // resolves (or immediately for short windows / other render
                // types).
                if (isChart)
                {
                    int windowSeconds = ResolveArchiveWindowSeconds();
                    if (windowSeconds > 60)
                    {
                        BackfillArchiveLive(windowSeconds);
                    }
                    else
                    {
                        // Short windows skip backfill - reveal the chart and let
                        // the live stream fill it in.
                        HideLoadingOverlay();
                        StartLive();
                    }
                }
                else
                {
                    StartLive();
                }
            }
            else if (mode == Mode.ClientPlayback)
                StartPlayback();

            StartStaleTickerIfEnabled();
            UpdateExportBadgeVisibility();
        }

        // null = no overlay (render the widget). Non-null = show this message instead.
        private string ResolveOverlayMessage(Mode mode)
        {
            // Setup mode always shows a summary panel with the Open-configuration button,
            // regardless of how complete the configuration is.
            if (mode == Mode.ClientSetup)
                return "";

            bool hasChannelId = !string.IsNullOrEmpty(_viewItemManager.MetadataId)
                                && Guid.TryParse(_viewItemManager.MetadataId, out var g)
                                && g != Guid.Empty;
            if (!hasChannelId)
                return "Not configured";

            if (_metadataItem == null)
            {
                var name = _viewItemManager.MetadataName;
                return string.IsNullOrEmpty(name)
                    ? "Metadata channel not found"
                    : $"Metadata channel \"{name}\" not found";
            }

            if (string.IsNullOrEmpty(_extractorCfg?.DataKey))
                return "No data key configured";

            return null;
        }

        // Chart hosts have their own title TextBlock outside the Viewbox-scaled tree -
        // mirror the user's title settings into it whenever the chart renderer is built.
        private void ApplyChartTitle()
        {
            var title = _viewItemManager.Title ?? "";
            bool show = !string.Equals(_viewItemManager.ShowTitle, "false", StringComparison.OrdinalIgnoreCase)
                        && !string.IsNullOrEmpty(title);
            if (!show)
            {
                chartTitleText.Visibility = Visibility.Collapsed;
                chartTitleRow.Height = new GridLength(0);
                return;
            }
            chartTitleText.Text = title;
            chartTitleText.Visibility = Visibility.Visible;
            chartTitleRow.Height = GridLength.Auto;
            switch (_viewItemManager.TitlePosition ?? "Left")
            {
                case "Center": chartTitleText.HorizontalAlignment = HorizontalAlignment.Center; chartTitleText.TextAlignment = TextAlignment.Center; break;
                case "Right":  chartTitleText.HorizontalAlignment = HorizontalAlignment.Right;  chartTitleText.TextAlignment = TextAlignment.Right;  break;
                default:       chartTitleText.HorizontalAlignment = HorizontalAlignment.Left;   chartTitleText.TextAlignment = TextAlignment.Left;   break;
            }
            double baseFs = 14;
            if (double.TryParse(_viewItemManager.TitleFontSize, NumberStyles.Float, CultureInfo.InvariantCulture, out var fs) && fs > 0)
                baseFs = fs;
            chartTitleText.FontSize = baseFs;
            try
            {
                var c = (Color)ColorConverter.ConvertFromString(_viewItemManager.TitleColor);
                chartTitleText.Foreground = new SolidColorBrush(c);
            }
            catch { chartTitleText.Foreground = new SolidColorBrush(Color.FromRgb(0xCF, 0xD7, 0xDA)); }
        }

        private void ApplyTitleSettings()
        {
            var title = _viewItemManager.Title ?? "";
            bool show = !string.Equals(_viewItemManager.ShowTitle, "false", StringComparison.OrdinalIgnoreCase)
                        && !string.IsNullOrEmpty(title);

            if (!show)
            {
                titleText.Visibility = Visibility.Collapsed;
                titleRow.Height = new GridLength(0);
                return;
            }

            titleText.Text = title;
            titleText.Visibility = Visibility.Visible;
            titleRow.Height = GridLength.Auto;

            // Position
            switch (_viewItemManager.TitlePosition ?? "Left")
            {
                case "Center": titleText.HorizontalAlignment = HorizontalAlignment.Center; titleText.TextAlignment = TextAlignment.Center; break;
                case "Right":  titleText.HorizontalAlignment = HorizontalAlignment.Right;  titleText.TextAlignment = TextAlignment.Right;  break;
                default:       titleText.HorizontalAlignment = HorizontalAlignment.Left;   titleText.TextAlignment = TextAlignment.Left;   break;
            }

            // Font size - density scales the title alongside the rest of the widget.
            double baseFs = 14;
            if (double.TryParse(_viewItemManager.TitleFontSize, NumberStyles.Float, CultureInfo.InvariantCulture, out var fs) && fs > 0)
                baseFs = fs;
            titleText.FontSize = baseFs * Renderers.WidgetTheme.DensityScale(_viewItemManager.WidgetDensity);

            // Color
            try
            {
                var c = (Color)ColorConverter.ConvertFromString(_viewItemManager.TitleColor);
                titleText.Foreground = new SolidColorBrush(c);
            }
            catch
            {
                titleText.Foreground = new SolidColorBrush(Color.FromRgb(0xCF, 0xD7, 0xDA));
            }
        }

        private void StartStaleTickerIfEnabled()
        {
            _staleSeconds = 0;
            int.TryParse(_viewItemManager.StaleSeconds ?? "0", NumberStyles.Integer, CultureInfo.InvariantCulture, out _staleSeconds);
            if (_staleSeconds <= 0) return;

            _staleTicker = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _staleTicker.Tick += (s, e) => UpdateStaleVisuals();
            _staleTicker.Start();
        }

        private void StopStaleTicker()
        {
            if (_staleTicker == null) return;
            _staleTicker.Stop();
            _staleTicker = null;
        }

        private void UpdateStaleVisuals()
        {
            if (_staleSeconds <= 0 || !_lastValueUtc.HasValue)
            {
                staleBadge.Visibility = Visibility.Collapsed;
                renderHost.Opacity = 1.0;
                return;
            }
            var age = DateTime.UtcNow - _lastValueUtc.Value;
            if (age.TotalSeconds >= _staleSeconds)
            {
                staleBadge.Visibility = Visibility.Visible;
                renderHost.Opacity = 0.45;
            }
            else
            {
                staleBadge.Visibility = Visibility.Collapsed;
                renderHost.Opacity = 1.0;
            }
        }

        private void StartPlayback()
        {
            if (_metadataItem == null) { _log.Info("[ViewItem] StartPlayback skipped (no channel item)"); return; }
            if (_pump != null) return;
            if (string.IsNullOrEmpty(_extractorCfg?.DataKey)) { _log.Info("[ViewItem] StartPlayback skipped (no DataKey)"); return; }
            _log.Info($"[ViewItem] StartPlayback channel='{_metadataItem.Name}'");

            _pump = new MetadataPlaybackPump(_metadataItem,
                _ => ExtractorConfig.FromManager(_viewItemManager),
                (value, ts) => Dispatcher.BeginInvoke(new Action(() =>
                {
                    _lastValueUtc = ts;
                    RenderValue(value);
                    UpdateStaleVisuals();
                })));
            _pump.Start();

            _playbackTimeReceiver = EnvironmentManager.Instance.RegisterReceiver(
                new MessageReceiver(OnPlaybackTime),
                new MessageIdFilter(MessageId.SmartClient.PlaybackCurrentTimeIndication));

            // PlaybackCurrentTimeIndication only fires when the cursor moves, so
            // entering playback while paused leaves the chart empty until the
            // user scrubs. Query the current cursor explicitly via the request
            // message and seed an immediate backfill if we're showing an
            // archive-backed renderer (line chart or table).
            if (_lineRenderer != null || _tableRenderer != null || _trendRenderer != null)
            {
                try
                {
                    var responses = EnvironmentManager.Instance.SendMessage(
                        new Message(MessageId.SmartClient.GetCurrentPlaybackTimeRequest));
                    DateTime? seed = null;
                    if (responses != null)
                    {
                        foreach (var r in responses)
                        {
                            if (r is DateTime dt && dt != DateTime.MinValue)
                            {
                                seed = dt.Kind == DateTimeKind.Utc ? dt : dt.ToUniversalTime();
                                break;
                            }
                        }
                    }
                    if (seed.HasValue)
                    {
                        _log.Info($"[ViewItem] StartPlayback seeding archive backfill at {seed.Value:O}");
                        _pump.RequestTime(seed.Value);
                        _lastPlaybackCursorUtc = seed.Value;
                        _lineRenderer?.SetCursor(seed.Value);
                        _tableRenderer?.SetCursor(seed.Value);
                        _trendRenderer?.SetCursor(seed.Value);
                        BackfillArchive(seed.Value, ResolveArchiveWindowSeconds());
                    }
                    else
                    {
                        _log.Info("[ViewItem] StartPlayback could not query current playback time; waiting for first time indication");
                    }
                }
                catch (Exception ex)
                {
                    _log.Error($"[ViewItem] StartPlayback seed failed: {ex.Message}");
                }
            }
        }

        private void StopPlayback()
        {
            if (_playbackTimeReceiver == null && _pump == null) return;
            _log.Info("[ViewItem] StopPlayback");
            if (_playbackTimeReceiver != null)
            {
                EnvironmentManager.Instance.UnRegisterReceiver(_playbackTimeReceiver);
                _playbackTimeReceiver = null;
            }
            if (_pump != null)
            {
                _pump.Stop();
                _pump = null;
            }
            // Cancel any in-flight backfill scan; results would land after the
            // mode change and could repaint a chart that's no longer visible.
            try { _backfillCts?.Cancel(); } catch { }
            _backfillCts = null;
            _lastBackfillCursorUtc = null;
        }

        // True for renderers that can backfill from the archive (line chart and
        // table). They share the chartRoot host, the session-window picker, and
        // the same scan + handoff machinery.
        private static bool IsArchiveBackedRenderer(string renderType)
        {
            return string.Equals(renderType, "LineChart", StringComparison.Ordinal)
                || string.Equals(renderType, "Table", StringComparison.Ordinal)
                || string.Equals(renderType, "Trend", StringComparison.Ordinal);
        }

        // The window length the active archive-backed renderer uses for backfills
        // and prune cutoffs. Honors the in-pane session override.
        private int ResolveArchiveWindowSeconds()
        {
            if (_lineRenderer != null) return BuildLineChartConfig().WindowSeconds;
            if (_tableRenderer != null) return BuildTableConfig().WindowSeconds;
            if (_trendRenderer != null)
            {
                var t = TrendConfig.FromManager(_viewItemManager);
                return t.LookbackSeconds;
            }
            return 60;
        }

        // Live backfill: range-scan the archive for [now - window, now] before
        // opening the live subscription. Same scan code as playback backfill but
        // anchored to wall-clock now. Without this, switching to a "Last 6 hours"
        // window starts empty and only fills over the next 6 hours - and every
        // live↔playback flip would reset the buffer to empty.
        //
        // The live subscription doesn't open until the backfill resolves; that
        // means we lose any samples emitted in the (~1-3s) scan window itself,
        // which is acceptable for typical metadata cadence.
        private void BackfillArchiveLive(int windowSeconds)
        {
            if (_metadataItem == null) return;
            if (_lineRenderer == null && _tableRenderer == null && _trendRenderer == null) return;

            // Show "Waiting for data..." while the backfill runs so the empty
            // pane isn't mistaken for "channel is dead".
            ShowLoadingOverlay($"Loading last {FormatWindow(windowSeconds)} from archive...");

            try { _backfillCts?.Cancel(); } catch { }
            _backfillCts = new System.Threading.CancellationTokenSource();
            var ct = _backfillCts.Token;

            var nowUtc = DateTime.UtcNow;
            var fromUtc = nowUtc.AddSeconds(-windowSeconds);
            const int maxFrames = 10000;

            // Construct a one-shot pump just for the scan; it owns its own
            // MetadataPlaybackSource and disposes it after the scan finishes.
            var scanPump = new MetadataPlaybackPump(_metadataItem,
                _ => ExtractorConfig.FromManager(_viewItemManager),
                (_, __) => { });

            _log.Info($"[ViewItem] BackfillArchive(live) window={windowSeconds}s from={fromUtc:O} to={nowUtc:O}");

            // Multi-series line chart: dispatch every series in a single scan.
            if (_lineRenderer != null && _lineExtractorCfgs != null && _lineExtractorCfgs.Count > 1)
            {
                var cfgs = _lineExtractorCfgs;
                scanPump.ScanRangeManyAsync(fromUtc, nowUtc, cfgs, maxFrames, ct).ContinueWith(t =>
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (ct.IsCancellationRequested) return;
                        if (_lineRenderer == null) return;
                        if (t.IsCanceled) _log.Info("[ViewItem] BackfillArchive(live, multi) cancelled");
                        else if (t.IsFaulted) _log.Error($"[ViewItem] BackfillArchive(live, multi) faulted: {t.Exception?.GetBaseException().Message}");
                        else if (t.Result != null) ApplyMultiSeriesBackfillResults(t.Result);

                        HideLoadingOverlay();
                        StartLive();
                    }));
                }, System.Threading.Tasks.TaskScheduler.Default);
                return;
            }

            scanPump.ScanRangeAsync(fromUtc, nowUtc, maxFrames, ct).ContinueWith(t =>
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (ct.IsCancellationRequested) return;
                    if (_lineRenderer == null && _tableRenderer == null && _trendRenderer == null) return;

                    if (t.IsCanceled)
                    {
                        _log.Info("[ViewItem] BackfillArchive(live) cancelled");
                    }
                    else if (t.IsFaulted)
                    {
                        _log.Error($"[ViewItem] BackfillArchive(live) faulted: {t.Exception?.GetBaseException().Message}");
                    }
                    else if (t.Result != null)
                    {
                        ApplyBackfillResults(t.Result);
                    }

                    // Restore the chart visibility regardless of scan outcome -
                    // even an empty backfill still leads into live streaming.
                    HideLoadingOverlay();
                    StartLive();
                    // Trend comparison baseline: kick off the period-over-period
                    // archive scan now that the renderer is hydrated. Anchors
                    // at wall-clock now in live mode and refreshes hourly.
                    if (_trendRenderer != null)
                    {
                        FetchTrendComparisonBaselineAsync(DateTime.UtcNow);
                        StartTrendBaselineRefreshTimer();
                    }
                }));
            }, System.Threading.Tasks.TaskScheduler.Default);
        }

        // Multi-series scan applier. Parses each series's strings to doubles
        // and seeds the line renderer in one shot via ResetAllWithSamples.
        private void ApplyMultiSeriesBackfillResults(List<List<(string Value, DateTime TimestampUtc)>> raw)
        {
            if (_lineRenderer == null || raw == null) return;
            var perSeries = new List<IReadOnlyList<(double, DateTime)>>(raw.Count);
            int total = 0;
            DateTime? latest = null;
            foreach (var list in raw)
            {
                var parsed = new List<(double, DateTime)>(list?.Count ?? 0);
                if (list != null)
                {
                    foreach (var r in list)
                    {
                        if (double.TryParse(r.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                        {
                            parsed.Add((v, r.TimestampUtc));
                            if (!latest.HasValue || r.TimestampUtc > latest.Value) latest = r.TimestampUtc;
                        }
                    }
                }
                perSeries.Add(parsed);
                total += parsed.Count;
            }
            _log.Info($"[ViewItem] Backfill apply (line, multi) series={raw.Count} samples={total}");
            _lineRenderer.ResetAllWithSamples(perSeries);
            if (total > 0)
            {
                _hasReceivedValue = true;
                _lastValueUtc = latest;
            }
        }

        // Push the scan result into whichever archive-backed renderer is active.
        // Line chart needs numeric parse; table keeps strings.
        private void ApplyBackfillResults(System.Collections.Generic.List<(string Value, DateTime TimestampUtc)> raw)
        {
            if (_lineRenderer != null)
            {
                var samples = new List<(double, DateTime)>(raw.Count);
                foreach (var r in raw)
                {
                    if (double.TryParse(r.Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v))
                        samples.Add((v, r.TimestampUtc));
                }
                _log.Info($"[ViewItem] Backfill apply (line) raw={raw.Count} parsed={samples.Count}");
                _lineRenderer.ResetWithSamples(samples);
                if (samples.Count > 0)
                {
                    _hasReceivedValue = true;
                    _lastValueUtc = samples[samples.Count - 1].Item2;
                }
            }
            if (_tableRenderer != null)
            {
                var samples = new List<(string, DateTime)>(raw.Count);
                foreach (var r in raw) samples.Add((r.Value, r.TimestampUtc));
                _log.Info($"[ViewItem] Backfill apply (table) rows={samples.Count}");
                _tableRenderer.ResetWithSamples(samples);
                if (samples.Count > 0)
                {
                    _hasReceivedValue = true;
                    _lastValueUtc = samples[samples.Count - 1].Item2;
                }
            }
            if (_trendRenderer != null)
            {
                var samples = new List<(double, DateTime)>(raw.Count);
                foreach (var r in raw)
                {
                    if (double.TryParse(r.Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v))
                        samples.Add((v, r.TimestampUtc));
                }
                _log.Info($"[ViewItem] Backfill apply (trend) raw={raw.Count} parsed={samples.Count}");
                _trendRenderer.ResetWithSamples(samples);
                if (samples.Count > 0)
                {
                    _hasReceivedValue = true;
                    _lastValueUtc = samples[samples.Count - 1].Item2;
                }
            }
        }

        private static string FormatWindow(int seconds)
        {
            if (seconds >= 86400) return $"{seconds / 86400}d";
            if (seconds >= 3600)  return $"{seconds / 3600}h";
            if (seconds >= 60)    return $"{seconds / 60}m";
            return $"{seconds}s";
        }

        // Builds the runtime LineChartConfig honoring any in-pane window override.
        // All chart code paths must go through this rather than calling
        // LineChartConfig.FromManager directly so the session window picker takes effect.
        // In playback mode zoom/pan is always enabled regardless of the saved
        // setting - there is no live stream that could fight with the pan-pause
        // behaviour, and users routinely want to scrub a region of the archive.
        private LineChartConfig BuildLineChartConfig()
        {
            var cfg = LineChartConfig.FromManager(_viewItemManager);
            if (_sessionWindowSecondsOverride.HasValue && _sessionWindowSecondsOverride.Value > 0)
                cfg.WindowSeconds = _sessionWindowSecondsOverride.Value;
            if (EnvironmentManager.Instance.Mode == Mode.ClientPlayback)
            {
                cfg.ZoomEnabled = true;
                cfg.PlaybackMode = true;
            }
            return cfg;
        }

        // Period-over-period baseline scanner for the Trend. For all
        // ComparisonModes other than RollingLookback, computes a list of
        // target UTC instants (yesterday, last week, etc.), scans the archive
        // for each ±AverageHalfWindow, averages samples per anchor, then
        // averages across anchors. Pushes the result into the trend renderer
        // via SetComparisonBaseline. Does nothing for Rolling mode.
        private System.Threading.CancellationTokenSource _trendBaselineCts;
        private DispatcherTimer _trendBaselineRefreshTimer;

        private void FetchTrendComparisonBaselineAsync(DateTime anchorUtc)
        {
            if (_trendRenderer == null || _metadataItem == null) return;
            var trendCfg = BuildTrendConfig();

            var targets = ComputeTrendComparisonAnchors(anchorUtc, trendCfg.ComparisonMode);
            if (targets == null || targets.Count == 0) return;

            try { _trendBaselineCts?.Cancel(); } catch { }
            _trendBaselineCts = new System.Threading.CancellationTokenSource();
            var ct = _trendBaselineCts.Token;
            // The in-pane window picker drives the averaging window: a
            // 60s picker gets ±60s of archive (2 min total) around the
            // historical anchor, a 1h picker gets ±1h, etc.
            int halfWindow = Math.Max(30, trendCfg.LookbackSeconds);

            // Pre-publish the scan window so the operator sees which
            // historical period we're checking even during the brief wait
            // before the archive returns. Spans the union of all targets.
            DateTime? prePublishFrom = null;
            DateTime? prePublishTo = null;
            foreach (var target in targets)
            {
                var from = target.AddSeconds(-halfWindow);
                var to = target.AddSeconds(halfWindow);
                if (!prePublishFrom.HasValue || from < prePublishFrom.Value) prePublishFrom = from;
                if (!prePublishTo.HasValue || to > prePublishTo.Value) prePublishTo = to;
            }
            _trendRenderer.SetComparisonWindow(prePublishFrom, prePublishTo);

            var scanPump = new MetadataPlaybackPump(_metadataItem,
                _ => ExtractorConfig.FromManager(_viewItemManager),
                (_, __) => { });

            System.Threading.Tasks.Task.Run(async () =>
            {
                double sum = 0;
                int countAnchors = 0;
                DateTime? scanFromUtc = null;
                DateTime? scanToUtc = null;
                foreach (var target in targets)
                {
                    if (ct.IsCancellationRequested) return;
                    var from = target.AddSeconds(-halfWindow);
                    var to = target.AddSeconds(halfWindow);
                    if (!scanFromUtc.HasValue || from < scanFromUtc.Value) scanFromUtc = from;
                    if (!scanToUtc.HasValue || to > scanToUtc.Value) scanToUtc = to;
                    List<(string Value, DateTime TimestampUtc)> raw = null;
                    try
                    {
                        raw = await scanPump.ScanRangeAsync(from, to, 2000, ct).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _log.Error($"[Trend] baseline scan threw for anchor {target:O}: {ex.Message}");
                        continue;
                    }
                    if (raw == null || raw.Count == 0) continue;

                    double anchorSum = 0; int anchorCount = 0;
                    foreach (var r in raw)
                    {
                        if (double.TryParse(r.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                        {
                            anchorSum += v; anchorCount++;
                        }
                    }
                    if (anchorCount == 0) continue;
                    sum += anchorSum / anchorCount;
                    countAnchors++;
                }

                double? baseline = countAnchors > 0 ? (double?)(sum / countAnchors) : null;
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (ct.IsCancellationRequested) return;
                    if (_trendRenderer == null) return;
                    _trendRenderer.SetComparisonBaseline(baseline, scanFromUtc, scanToUtc);
                    _log.Info($"[Trend] comparison baseline mode={trendCfg.ComparisonMode} anchors={countAnchors}/{targets.Count} value={(baseline.HasValue ? baseline.Value.ToString("0.###", CultureInfo.InvariantCulture) : "null")}");
                }));
            }, ct);
        }

        // Returns the UTC instant to scan for the configured comparison mode.
        // Always returns a single-element list now (no averaging across
        // multiple historical anchors).
        private static List<DateTime> ComputeTrendComparisonAnchors(DateTime anchorUtc, TrendComparisonMode mode)
        {
            var list = new List<DateTime>();
            switch (mode)
            {
                case TrendComparisonMode.SameTimeYesterday:
                    list.Add(anchorUtc.AddDays(-1));
                    break;
                case TrendComparisonMode.SameTimeLastWeek:
                    list.Add(anchorUtc.AddDays(-7));
                    break;
                case TrendComparisonMode.SameTimeLastMonth:
                    list.Add(anchorUtc.AddMonths(-1));
                    break;
                default:
                    list.Add(anchorUtc.AddDays(-1));
                    break;
            }
            return list;
        }

        private void StartTrendBaselineRefreshTimer()
        {
            if (_trendBaselineRefreshTimer != null) return;
            // Re-fetch the comparison baseline every hour. The anchor for "yesterday"
            // rolls forward in real time; without a refresh the widget would
            // compare today's current value against an anchor that drifts older.
            _trendBaselineRefreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromHours(1),
            };
            _trendBaselineRefreshTimer.Tick += (s, e) =>
            {
                FetchTrendComparisonBaselineAsync(DateTime.UtcNow);
            };
            _trendBaselineRefreshTimer.Start();
        }

        private void StopTrendBaselineRefreshTimer()
        {
            if (_trendBaselineRefreshTimer == null) return;
            _trendBaselineRefreshTimer.Stop();
            _trendBaselineRefreshTimer = null;
            try { _trendBaselineCts?.Cancel(); } catch { }
        }

        // Build the Trend config and stamp PlaybackMode so the renderer
        // anchors the lookback at the cursor instead of wall-clock now.
        // Honors the in-pane session window override on LookbackSeconds; the
        // archive-baseline scan uses the same value as its averaging window.
        private TrendConfig BuildTrendConfig()
        {
            var cfg = TrendConfig.FromManager(_viewItemManager);
            if (_sessionWindowSecondsOverride.HasValue && _sessionWindowSecondsOverride.Value > 0)
                cfg.LookbackSeconds = _sessionWindowSecondsOverride.Value;
            if (EnvironmentManager.Instance.Mode == Mode.ClientPlayback)
                cfg.PlaybackMode = true;
            return cfg;
        }

        // Map each LineSeries to its ExtractorConfig in chart order. Live and
        // backfill paths feed this list to TryExtractMany / ScanRangeManyAsync
        // so a single XML walk fans out to every matching series.
        private static List<ExtractorConfig> BuildLineExtractorConfigs(LineChartConfig cfg)
        {
            var list = new List<ExtractorConfig>();
            if (cfg?.Series == null) return list;
            foreach (var s in cfg.Series)
            {
                list.Add(s != null ? s.ToExtractorConfig() : null);
            }
            return list;
        }

        private static readonly (string Label, int Seconds)[] _windowPresets = new (string, int)[]
        {
            ("Last 60 seconds",  60),
            ("Last 5 minutes",   300),
            ("Last 10 minutes",  600),
            ("Last 30 minutes",  1800),
            ("Last 1 hour",      3600),
            ("Last 6 hours",     21600),
            ("Last 24 hours",    86400),
        };

        private void OnWindowPickerClick(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            BuildWindowPickerOptions();
            windowPickerPopup.IsOpen = !windowPickerPopup.IsOpen;
        }

        // Reveals the right Export badge (chart-row variant for Line/Table, the
        // floating one anchored to the parent Grid for everything else) when in
        // Playback and the operator hasn't disabled the feature for this widget.
        // Called from ApplyMode and after BuildRenderHost so badge state tracks
        // both mode changes and render-type swaps.
        private void UpdateExportBadgeVisibility()
        {
            var mode = EnvironmentManager.Instance.Mode;
            bool enabled = !string.Equals(_viewItemManager.EnableExport, "false", StringComparison.OrdinalIgnoreCase);
            bool channelReady = _metadataItem != null
                                && _extractorCfg != null
                                && !string.IsNullOrEmpty(_extractorCfg.DataKey);
            bool show = mode == Mode.ClientPlayback && enabled && channelReady;
            bool isChart = IsArchiveBackedRenderer(_viewItemManager.RenderType);
            // Hide both first; then turn on the one matching the active render host.
            exportBadgeChart.Visibility = Visibility.Collapsed;
            exportBadgeRender.Visibility = Visibility.Collapsed;
            if (!show) return;
            if (isChart) exportBadgeChart.Visibility = Visibility.Visible;
            else exportBadgeRender.Visibility = Visibility.Visible;
        }

        private void OnExportClick(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            try
            {
                if (_metadataItem == null)
                {
                    _log.Info("[Export] open dialog skipped - no metadata channel resolved.");
                    return;
                }
                if (_extractorCfg == null || string.IsNullOrEmpty(_extractorCfg.DataKey))
                {
                    _log.Info("[Export] open dialog skipped - no DataKey configured.");
                    return;
                }
                // Default range: last 24 hours from the playback cursor. The
                // operator can narrow or widen via the date pickers / quick-range
                // buttons before clicking Load Data.
                const int windowSeconds = 86400;

                DateTime cursorUtc = _lastPlaybackCursorUtc ?? DateTime.UtcNow;
                DateTime defaultToUtc = cursorUtc;
                DateTime defaultFromUtc = cursorUtc.AddSeconds(-windowSeconds);

                // For multi-series Line Charts, pass the full series list so the
                // dialog walks every line and emits a wide-format CSV.
                IReadOnlyList<LineSeries> multiSeries = null;
                if (_lineRenderer != null)
                {
                    var lineCfg = BuildLineChartConfig();
                    if (lineCfg?.Series != null && lineCfg.Series.Count > 1)
                        multiSeries = lineCfg.Series;
                }

                var dlg = new Export.ExportDialog(
                    metadataItem: _metadataItem,
                    extractorCfg: _extractorCfg,
                    channelName: _metadataItem.Name,
                    renderType: _viewItemManager.RenderType,
                    lampMap: _viewItemManager.LampMap,
                    defaultFromUtc: defaultFromUtc,
                    defaultToUtc: defaultToUtc,
                    multiSeries: multiSeries);
                var owner = Window.GetWindow(this);
                if (owner != null) dlg.Owner = owner;
                dlg.ShowDialog();
            }
            catch (Exception ex)
            {
                _log.Error($"OnExportClick threw: {ex.Message}", ex);
            }
        }

        private void BuildWindowPickerOptions()
        {
            windowPickerOptions.Children.Clear();
            int currentEffective = ResolveArchiveWindowSeconds();
            foreach (var p in _windowPresets)
            {
                int sec = p.Seconds;
                bool isCurrent = sec == currentEffective;
                windowPickerOptions.Children.Add(CreateWindowPickerItem(p.Label, isCurrent, () => SetSessionWindow(sec)));
            }
            // Separator + Default entry. "Default" reverts the override and
            // re-reads the saved configuration.
            windowPickerOptions.Children.Add(new System.Windows.Controls.Border
            {
                Height = 1,
                Background = new SolidColorBrush(Color.FromRgb(0x33, 0x3E, 0x42)),
                Margin = new Thickness(4, 2, 4, 2),
            });
            int savedDefault = _tableRenderer != null
                ? TableConfig.FromManager(_viewItemManager).WindowSeconds
                : LineChartConfig.FromManager(_viewItemManager).WindowSeconds;
            string defaultLabel = $"Default ({FormatWindow(savedDefault)})";
            windowPickerOptions.Children.Add(CreateWindowPickerItem(defaultLabel, !_sessionWindowSecondsOverride.HasValue, () => SetSessionWindow(null)));
        }

        private System.Windows.FrameworkElement CreateWindowPickerItem(string label, bool isCurrent, Action onClick)
        {
            var b = new System.Windows.Controls.Border
            {
                Padding = new Thickness(10, 4, 10, 4),
                Cursor = System.Windows.Input.Cursors.Hand,
                Background = isCurrent
                    ? (Brush)new SolidColorBrush(Color.FromRgb(0x2A, 0x35, 0x39))
                    : System.Windows.Media.Brushes.Transparent,
            };
            var tb = new System.Windows.Controls.TextBlock
            {
                Text = label,
                Foreground = new SolidColorBrush(Color.FromRgb(0xCF, 0xD7, 0xDA)),
                FontSize = 12,
                FontWeight = isCurrent ? FontWeights.SemiBold : FontWeights.Normal,
            };
            b.Child = tb;
            b.MouseEnter += (s, e) => b.Background = new SolidColorBrush(Color.FromRgb(0x33, 0x3E, 0x42));
            b.MouseLeave += (s, e) =>
                b.Background = isCurrent
                    ? (Brush)new SolidColorBrush(Color.FromRgb(0x2A, 0x35, 0x39))
                    : System.Windows.Media.Brushes.Transparent;
            b.MouseLeftButtonUp += (s, e) =>
            {
                e.Handled = true;
                windowPickerPopup.IsOpen = false;
                onClick();
            };
            return b;
        }

        private void SetSessionWindow(int? overrideSeconds)
        {
            _sessionWindowSecondsOverride = overrideSeconds;
            UpdateWindowPickerLabel();
            if (_lineRenderer == null && _tableRenderer == null && _trendRenderer == null) return;

            int effectiveWindow;
            var mode = EnvironmentManager.Instance.Mode;
            if (_lineRenderer != null)
            {
                var cfg = BuildLineChartConfig();
                effectiveWindow = cfg.WindowSeconds;
                _log.Info($"[ViewItem] WindowPicker override={(overrideSeconds.HasValue ? overrideSeconds.Value.ToString() : "default")} effective={effectiveWindow}s mode={mode} render=Line");
                _lineRenderer.Configure(cfg);
                _lineExtractorCfgs = BuildLineExtractorConfigs(cfg);
            }
            else if (_tableRenderer != null)
            {
                var cfg = BuildTableConfig();
                effectiveWindow = cfg.WindowSeconds;
                _log.Info($"[ViewItem] WindowPicker override={(overrideSeconds.HasValue ? overrideSeconds.Value.ToString() : "default")} effective={effectiveWindow}s mode={mode} render=Table");
                _tableRenderer.Configure(cfg);
            }
            else
            {
                var cfg = BuildTrendConfig();
                effectiveWindow = cfg.LookbackSeconds;
                _log.Info($"[ViewItem] WindowPicker override={(overrideSeconds.HasValue ? overrideSeconds.Value.ToString() : "default")} effective={effectiveWindow}s mode={mode} render=Trend");
                _trendRenderer.Configure(cfg);
            }

            if (mode == Mode.ClientLive)
            {
                // Tear down the live subscription and re-enter through the same
                // path ApplyMode uses, so a wide window backfills from archive
                // first and a short window starts streaming immediately.
                StopLive();
                if (effectiveWindow > 60)
                    BackfillArchiveLive(effectiveWindow);
                else
                    StartLive();
            }
            else if (mode == Mode.ClientPlayback)
            {
                // Refill immediately around the most recent cursor instead of
                // waiting for the user to scrub the timeline. _lastBackfillCursorUtc
                // is reset so a future scrub still re-evaluates against the new window.
                _lastBackfillCursorUtc = null;
                if (_lastPlaybackCursorUtc.HasValue && _pump != null)
                    BackfillArchive(_lastPlaybackCursorUtc.Value, effectiveWindow);
            }
        }

        private void UpdateWindowPickerLabel()
        {
            int effective = ResolveArchiveWindowSeconds();
            string label = FormatWindow(effective);
            if (_sessionWindowSecondsOverride.HasValue) label += "*";
            windowPickerText.Text = label;
        }

        // Builds the runtime TableConfig honoring any in-pane window override.
        // Mirrors BuildLineChartConfig so all window-picker code paths converge.
        private TableConfig BuildTableConfig()
        {
            var cfg = TableConfig.FromManager(_viewItemManager);
            if (_sessionWindowSecondsOverride.HasValue && _sessionWindowSecondsOverride.Value > 0)
                cfg.WindowSeconds = _sessionWindowSecondsOverride.Value;
            if (EnvironmentManager.Instance.Mode == Mode.ClientPlayback)
                cfg.PlaybackMode = true;
            return cfg;
        }

        // Kick off a range scan covering [cursor - window, cursor] and on success
        // bulk-load the results into the active archive-backed renderer. Cancels
        // any previous in-flight scan first so rapid scrubbing doesn't pile up
        // overlapping requests.
        private void BackfillArchive(DateTime cursorUtc, int windowSeconds)
        {
            if (_pump == null) return;
            if (_lineRenderer == null && _tableRenderer == null && _trendRenderer == null) return;
            try { _backfillCts?.Cancel(); } catch { }
            _backfillCts = new System.Threading.CancellationTokenSource();
            var ct = _backfillCts.Token;
            var fromUtc = cursorUtc.AddSeconds(-windowSeconds);
            _lastBackfillCursorUtc = cursorUtc;

            // Show the same loading overlay live mode uses so window switches and
            // wide scrubs in playback give visible feedback while the scan runs.
            ShowLoadingOverlay($"Loading {FormatWindow(windowSeconds)} from archive...");

            // Cap pulled frames at a few thousand to keep the scan bounded for
            // very high-cadence sources or wide windows.
            const int maxFrames = 4000;
            _log.Info($"[ViewItem] BackfillArchive(playback) cursor={cursorUtc:O} window={windowSeconds}s from={fromUtc:O}");

            // Multi-series line chart fans out via ScanRangeManyAsync - one
            // sweep, N parallel series populated.
            if (_lineRenderer != null && _lineExtractorCfgs != null && _lineExtractorCfgs.Count > 1)
            {
                var cfgs = _lineExtractorCfgs;
                _pump.ScanRangeManyAsync(fromUtc, cursorUtc, cfgs, maxFrames, ct).ContinueWith(t =>
                {
                    if (t.IsCanceled) { _log.Info("[ViewItem] BackfillArchive(playback, multi) cancelled"); return; }
                    if (t.IsFaulted) { _log.Error($"[ViewItem] BackfillArchive(playback, multi) faulted: {t.Exception?.GetBaseException().Message}"); return; }
                    var raw = t.Result;
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (ct.IsCancellationRequested) return;
                        if (_lineRenderer == null) return;
                        ApplyMultiSeriesBackfillResults(raw);
                        _lineRenderer.SetCursor(cursorUtc);
                        HideLoadingOverlay();
                    }));
                }, System.Threading.Tasks.TaskScheduler.Default);
                return;
            }

            _pump.ScanRangeAsync(fromUtc, cursorUtc, maxFrames, ct).ContinueWith(t =>
            {
                if (t.IsCanceled)
                {
                    _log.Info("[ViewItem] BackfillArchive(playback) cancelled");
                    return;
                }
                if (t.IsFaulted)
                {
                    _log.Error($"[ViewItem] BackfillArchive(playback) faulted: {t.Exception?.GetBaseException().Message}");
                    return;
                }
                var raw = t.Result;
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (ct.IsCancellationRequested) return;
                    if (_lineRenderer == null && _tableRenderer == null && _trendRenderer == null) return;
                    ApplyBackfillResults(raw);
                    _lineRenderer?.SetCursor(cursorUtc);
                    _tableRenderer?.SetCursor(cursorUtc);
                    _trendRenderer?.SetCursor(cursorUtc);
                    HideLoadingOverlay();
                    // Trend comparison baseline: anchor at the playback cursor
                    // so "yesterday" means 24h before the cursor, not before
                    // wall-clock now. Refresh timer kicks off once and keeps
                    // running until Close (it re-fetches against UtcNow on
                    // tick; live mode only - in playback the cursor controls
                    // when this code path runs again).
                    if (_trendRenderer != null)
                    {
                        FetchTrendComparisonBaselineAsync(cursorUtc);
                    }
                }));
            }, System.Threading.Tasks.TaskScheduler.Default);
        }

        // Centralised loading-overlay show/hide for chart backfills. Hides the
        // chart and shows the spinner panel ("Waiting for data..." styling) with
        // a custom detail message; HideLoadingOverlay restores the chart.
        private void ShowLoadingOverlay(string detail)
        {
            noDataPanel.Visibility = Visibility.Visible;
            chartRoot.Visibility = Visibility.Collapsed;
            // Loading state has its own heading so it doesn't collide with the
            // generic "Waiting for data..." placeholder text.
            noDataHeading.Text = "Loading...";
            noDataDetail.Text = detail ?? "";
        }

        private void HideLoadingOverlay()
        {
            noDataPanel.Visibility = Visibility.Collapsed;
            chartRoot.Visibility = Visibility.Visible;
        }

        private object OnPlaybackTime(Message message, FQID destination, FQID sender)
        {
            try
            {
                if (_pump == null) return null;

                DateTime? utc = null;
                if (message.Data is DateTime dt)
                {
                    utc = dt.Kind == DateTimeKind.Utc ? dt : dt.ToUniversalTime();
                }
                else if (message.Data != null)
                {
                    var prop = message.Data.GetType().GetProperty("DateTime");
                    if (prop != null && prop.GetValue(message.Data) is DateTime dt2)
                        utc = dt2.Kind == DateTimeKind.Utc ? dt2 : dt2.ToUniversalTime();
                }

                if (utc.HasValue)
                {
                    _pump.RequestTime(utc.Value);
                    _lastPlaybackCursorUtc = utc.Value;

                    // Archive-backed playback: small scrubs just move the cursor,
                    // but jumps bigger than half the visible window (or the first
                    // time we see a cursor) trigger a fresh range scan to refill
                    // the visible buffer with archive samples.
                    if (_lineRenderer != null || _tableRenderer != null || _trendRenderer != null)
                    {
                        _lineRenderer?.SetCursor(utc.Value);
                        _tableRenderer?.SetCursor(utc.Value);
                        _trendRenderer?.SetCursor(utc.Value);
                        int windowSeconds = ResolveArchiveWindowSeconds();
                        bool needBackfill = !_lastBackfillCursorUtc.HasValue
                            || Math.Abs((utc.Value - _lastBackfillCursorUtc.Value).TotalSeconds) > windowSeconds / 2.0;
                        if (needBackfill)
                            BackfillArchive(utc.Value, windowSeconds);
                    }
                }
            }
            catch (Exception ex)
            {
                _log.Error($"OnPlaybackTime threw: {ex.Message}");
            }
            return null;
        }

        private void BuildRenderHost()
        {
            renderHost.Children.Clear();
            chartHost.Children.Clear();
            _lampRenderer = null;
            _numberRenderer = null;
            _gaugeRenderer = null;
            _textRenderer = null;
            _lineRenderer = null;
            _tableRenderer = null;
            _trendRenderer = null;
            _base64ImageRenderer = null;
            _lineExtractorCfgs = null;

            UIElement visual;
            bool isChart = false;
            switch (_viewItemManager.RenderType)
            {
                case "Number":
                    _numberRenderer = new NumberRenderer();
                    visual = _numberRenderer.Visual;
                    _numberRenderer.Clear();
                    break;
                case "Gauge":
                    _gaugeRenderer = new GaugeRenderer();
                    visual = _gaugeRenderer.Visual;
                    _gaugeRenderer.Clear();
                    break;
                case "Text":
                    _textRenderer = new TextRenderer();
                    visual = _textRenderer.Visual;
                    _textRenderer.Clear();
                    break;
                case "Base64Image":
                    _base64ImageRenderer = new Base64ImageRenderer();
                    visual = _base64ImageRenderer.Visual;
                    _base64ImageRenderer.Clear();
                    break;
                case "LineChart":
                    _lineRenderer = new LineChartRenderer();
                    var lineCfg = BuildLineChartConfig();
                    _lineRenderer.Configure(lineCfg);
                    _lineExtractorCfgs = BuildLineExtractorConfigs(lineCfg);
                    visual = _lineRenderer.Visual;
                    isChart = true;
                    break;
                case "Table":
                    _tableRenderer = new TableRenderer();
                    _tableRenderer.Configure(BuildTableConfig());
                    visual = _tableRenderer.Visual;
                    isChart = true;
                    break;
                case "Trend":
                    _trendRenderer = new TrendRenderer();
                    _trendRenderer.Configure(BuildTrendConfig());
                    visual = _trendRenderer.Visual;
                    isChart = true; // native-resolution host: the sparkline must size with the pane.
                    break;
                case "Lamp":
                default:
                    _lampRenderer = new LampRenderer();
                    visual = _lampRenderer.Visual;
                    _lampRenderer.Clear();
                    break;
            }

            // Chart renderers go into the native-resolution host (no Viewbox scaling).
            // All other renderers stay in the 320-wide Viewbox host so they keep their
            // existing scale-to-fit behaviour.
            if (isChart)
            {
                chartHost.Children.Add(visual);
                ApplyChartTitle();
                UpdateWindowPickerLabel();
            }
            else
            {
                renderHost.Children.Add(visual);
            }
        }

        private void ResolveMetadataItem()
        {
            _metadataItem = null;
            if (Guid.TryParse(_viewItemManager.MetadataId, out var id) && id != Guid.Empty)
            {
                try
                {
                    _metadataItem = Configuration.Instance.GetItem(id, Kind.Metadata);
                }
                catch (Exception ex)
                {
                    _log.Error($"Resolve metadata item {id} failed: {ex.Message}");
                }
            }
        }

        private int _livePacketsSeen;

        private void StartLive()
        {
            if (_metadataItem == null)
            {
                _log.Info("[ViewItem] StartLive skipped (no channel item)");
                return;
            }
            if (_liveSource != null) return;
            if (string.IsNullOrEmpty(_extractorCfg?.DataKey))
            {
                _log.Info("[ViewItem] StartLive skipped (no DataKey)");
                return;
            }

            try
            {
                _livePacketsSeen = 0;
                _log.Info($"[ViewItem] StartLive channel='{_metadataItem.Name}' id={_metadataItem.FQID.ObjectId} key={_extractorCfg.DataKey}");
                _liveSource = new MetadataLiveSource(_metadataItem) { LiveModeStart = true };
                _liveSource.Init();
                _liveSource.LiveContentEvent += OnLiveContent;
                _liveSource.ErrorEvent += OnLiveError;
                _log.Info("[ViewItem] StartLive subscribed OK");
            }
            catch (Exception ex)
            {
                _log.Error($"[ViewItem] StartLive failed: {ex.Message}", ex);
                _liveSource = null;
            }
        }

        private void StopLive()
        {
            // Cancel in-flight live-mode chart backfill; we don't want it to land
            // on a mode that's no longer live.
            try { _backfillCts?.Cancel(); } catch { }
            _backfillCts = null;
            if (_liveSource == null) return;
            _log.Info($"[ViewItem] StopLive packets={_livePacketsSeen}");
            try
            {
                _liveSource.LiveContentEvent -= OnLiveContent;
                _liveSource.ErrorEvent -= OnLiveError;
                _liveSource.Close();
            }
            catch { }
            _liveSource = null;
        }

        private void OnLiveContent(MetadataLiveSource source, MetadataLiveContent content)
        {
            try
            {
                _livePacketsSeen++;
                string xml = content?.Content?.GetMetadataString();
                if (string.IsNullOrEmpty(xml))
                {
                    _log.Info($"[ViewItem] Live packet #{_livePacketsSeen} empty");
                    return;
                }

                if (_metadataItem != null)
                    LastXmlCache.Put(_metadataItem.FQID.ObjectId, xml);

                // Multi-series line chart: walk the XML once and dispatch every
                // matching series in a single pass. Other render types fall
                // through to the legacy single-key TryExtract path below.
                if (_lineRenderer != null && _lineExtractorCfgs != null && _lineExtractorCfgs.Count > 0)
                {
                    var hits = MetadataExtractor.TryExtractMany(xml, _lineExtractorCfgs);
                    if (hits.Count == 0) return;
                    var localHits = hits;
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (_lineRenderer == null) return;
                        DateTime? latest = null;
                        foreach (var kv in localHits)
                        {
                            var h = kv.Value;
                            if (h == null) continue;
                            if (double.TryParse(h.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                                _lineRenderer.AddSample(kv.Key, v, h.TimestampUtc);
                            if (!latest.HasValue || h.TimestampUtc > latest.Value)
                                latest = h.TimestampUtc;
                        }
                        if (latest.HasValue)
                        {
                            _lastValueUtc = latest;
                            if (!_hasReceivedValue)
                            {
                                _hasReceivedValue = true;
                                noDataPanel.Visibility = Visibility.Collapsed;
                                chartRoot.Visibility = Visibility.Visible;
                            }
                            UpdateStaleVisuals();
                        }
                    }));
                    return;
                }

                var hit = MetadataExtractor.TryExtract(xml, _extractorCfg);
                if (_livePacketsSeen <= 3 || _livePacketsSeen % 50 == 0)
                {
                    _log.Info($"[ViewItem] Live packet #{_livePacketsSeen} bytes={xml.Length} extracted={(hit?.Value ?? "(no match)")}");
                    if (hit == null)
                    {
                        var msgs = MetadataExtractor.Observe(xml).Take(3).ToList();
                        foreach (var m in msgs)
                        {
                            var keys = string.Join(",", m.Data?.Select(kv => kv.Key + "=" + kv.Value) ?? new string[0]);
                            _log.Info($"[ViewItem]   present topic='{m.Topic}' data=[{keys}]");
                        }
                    }
                }
                if (hit == null) return;

                Dispatcher.BeginInvoke(new Action(() =>
                {
                    _lastValueUtc = hit.TimestampUtc;
                    RenderValue(hit.Value);
                    UpdateStaleVisuals();
                }));
            }
            catch (Exception ex)
            {
                _log.Error($"OnLiveContent threw: {ex.Message}", ex);
            }
        }

        private void OnLiveError(MetadataLiveSource source, Exception ex)
        {
            _log.Error($"[ViewItem] LiveSource error: {ex?.GetType().Name}: {ex?.Message}");
        }

        private void RenderValue(string value)
        {
            if (!_hasReceivedValue)
            {
                _hasReceivedValue = true;
                noDataPanel.Visibility = Visibility.Collapsed;
                // Archive-backed renderers (line, table, trend) are already
                // visible (shown empty); only the bitmap-scaled host needs the
                // first-value reveal.
                if (_lineRenderer == null && _tableRenderer == null && _trendRenderer == null)
                    renderViewbox.Visibility = Visibility.Visible;
            }

            string density = _viewItemManager.WidgetDensity ?? "Comfortable";

            if (_lampRenderer != null)
            {
                _lampRenderer.Density = density;
                _lampRenderer.IconSize = ParseDouble(_viewItemManager.LampIconSize, 96);
                _lampRenderer.Update(value, LampMapParser.Parse(_viewItemManager.LampMap));
            }
            else if (_numberRenderer != null)
            {
                _numberRenderer.Density = density;
                _numberRenderer.Update(value, NumericConfig.FromManager(_viewItemManager));
            }
            else if (_gaugeRenderer != null)
            {
                _gaugeRenderer.Density = density;
                _gaugeRenderer.Update(value, GaugeConfig.FromManager(_viewItemManager));
            }
            else if (_textRenderer != null)
            {
                _textRenderer.FontSize = ParseDouble(_viewItemManager.TextFontSize, 28);
                _textRenderer.Update(value);
            }
            else if (_base64ImageRenderer != null)
            {
                _base64ImageRenderer.Update(value);
            }
            else if (_lineRenderer != null)
            {
                if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                {
                    var ts = _lastValueUtc ?? DateTime.UtcNow;
                    _lineRenderer.AddSample(v, ts);
                }
            }
            else if (_tableRenderer != null)
            {
                var ts = _lastValueUtc ?? DateTime.UtcNow;
                _tableRenderer.AddSample(value, ts);
            }
            else if (_trendRenderer != null)
            {
                if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                {
                    var ts = _lastValueUtc ?? DateTime.UtcNow;
                    _trendRenderer.AddSample(v, ts);
                }
            }
        }

        private static double ParseDouble(string s, double fallback)
        {
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;
        }

        private object OnModeChanged(Message message, FQID destination, FQID sender)
        {
            Dispatcher.BeginInvoke(new Action(() => ApplyMode((Mode)message.Data)));
            return null;
        }

        private void OnMouseLeftUp(object sender, MouseButtonEventArgs e) => FireClickEvent();
        private void OnMouseDoubleClick(object sender, MouseButtonEventArgs e) => FireDoubleClickEvent();

        private void ApplySetupSummary()
        {
            string channel = !string.IsNullOrEmpty(_viewItemManager.MetadataName)
                ? _viewItemManager.MetadataName
                : (string.IsNullOrEmpty(_viewItemManager.MetadataId) ? "(not selected)" : "(channel missing)");
            string render = string.IsNullOrEmpty(_viewItemManager.RenderType) ? "Lamp" : _viewItemManager.RenderType;
            string topic = string.IsNullOrEmpty(_viewItemManager.Topic) ? "(any)" : _viewItemManager.Topic;
            string key = string.IsNullOrEmpty(_viewItemManager.DataKey) ? "(not set)" : _viewItemManager.DataKey;

            bool isComplete = !string.IsNullOrEmpty(_viewItemManager.MetadataId)
                              && !string.IsNullOrEmpty(_viewItemManager.DataKey);

            summaryChannel.Text = channel;
            summaryRender.Text = render;
            summaryTopic.Text = topic;
            summaryDataKey.Text = key;

            if (isComplete)
            {
                setupSubheader.Text = "Configured. Click to edit.";
                setupSubheader.Foreground = new SolidColorBrush(Color.FromRgb(0x7A, 0x83, 0x88));
            }
            else
            {
                setupSubheader.Text = "Not fully configured.";
                setupSubheader.Foreground = new SolidColorBrush(Color.FromRgb(0xE6, 0x95, 0x00));
            }
        }

        private void OnOpenConfigClick(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            try
            {
                var win = new MetadataDisplayConfigurationWindow(_viewItemManager);
                var owner = Window.GetWindow(this);
                if (owner != null) win.Owner = owner;
                var result = win.ShowDialog();
                _log.Info($"[ViewItem] Configuration window closed result={result}");
                if (result == true)
                {
                    _viewItemManager.Save();
                    Reconfigure();
                }
            }
            catch (Exception ex)
            {
                _log.Error($"OnOpenConfigClick threw: {ex.Message}", ex);
            }
        }

        public override bool Maximizable => true;
        public override bool Selectable => true;
        public override bool ShowToolbar => false;
    }
}
