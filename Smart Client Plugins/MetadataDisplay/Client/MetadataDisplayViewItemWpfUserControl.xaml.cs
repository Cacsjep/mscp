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

        private MetadataPlaybackPump _pump;
        private object _playbackTimeReceiver;

        private LampRenderer _lampRenderer;
        private NumberRenderer _numberRenderer;
        private GaugeRenderer _gaugeRenderer;
        private TextRenderer _textRenderer;
        private LineChartRenderer _lineRenderer;

        private DateTime? _lastValueUtc;
        // Playback line-chart backfill bookkeeping: track the last cursor we
        // backfilled around so a small scrub just moves the cursor marker, while
        // a big jump (or first entry) triggers a fresh range scan.
        private DateTime? _lastBackfillCursorUtc;
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
                return;
            }

            setupPanel.Visibility = Visibility.Collapsed;
            _hasReceivedValue = false;

            // LineChart goes into the native-resolution chartRoot; other renderers
            // use the 320-wide renderViewbox. Show the chart immediately (axes +
            // threshold bands are useful empty); other renderers stay hidden behind
            // the "Waiting for data" overlay until a value arrives.
            bool isChart = string.Equals(_viewItemManager.RenderType, "LineChart", StringComparison.Ordinal);
            renderViewbox.Visibility = Visibility.Collapsed;
            chartRoot.Visibility = isChart ? Visibility.Visible : Visibility.Collapsed;
            noDataPanel.Visibility = isChart ? Visibility.Collapsed : Visibility.Visible;
            noDataDetail.Text = mode == Mode.ClientPlayback
                ? "Move the playback cursor to a time when this metadata was recorded."
                : $"Subscribed to {_metadataItem?.Name ?? "channel"}. Waiting for first matching packet.";

            if (mode == Mode.ClientLive)
            {
                // For LineChart with a non-trivial window we backfill the chart
                // from the archive first so the user sees recent history right
                // away instead of waiting N minutes for it to fill. StartLive runs
                // after the backfill resolves (or immediately for short windows /
                // other render types).
                if (isChart && _viewItemManager.RenderType == "LineChart")
                {
                    var cfg = BuildLineChartConfig();
                    if (cfg.WindowSeconds > 60)
                    {
                        BackfillLineChartLive(cfg);
                    }
                    else
                    {
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

        // Chart hosts have their own title TextBlock outside the Viewbox-scaled tree —
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

            // Font size — density scales the title alongside the rest of the widget.
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

        // Live backfill: range-scan the archive for [now - window, now] before
        // opening the live subscription. Same scan code as playback backfill but
        // anchored to wall-clock now. Without this, switching to a "Last 6 hours"
        // line chart starts empty and only fills over the next 6 hours — and
        // every live↔playback flip would reset the chart to empty.
        //
        // The live subscription doesn't open until the backfill resolves; that
        // means we lose any samples emitted in the (~1-3s) scan window itself,
        // which is acceptable for typical metadata cadence.
        private void BackfillLineChartLive(LineChartConfig cfg)
        {
            if (_lineRenderer == null || _metadataItem == null) return;

            // Show "Waiting for data..." while the backfill runs so the empty
            // chart isn't mistaken for "channel is dead".
            noDataPanel.Visibility = Visibility.Visible;
            chartRoot.Visibility = Visibility.Collapsed;
            noDataDetail.Text = $"Loading last {FormatWindow(cfg.WindowSeconds)} from archive...";

            try { _backfillCts?.Cancel(); } catch { }
            _backfillCts = new System.Threading.CancellationTokenSource();
            var ct = _backfillCts.Token;

            var nowUtc = DateTime.UtcNow;
            var fromUtc = nowUtc.AddSeconds(-cfg.WindowSeconds);
            const int maxFrames = 10000;

            // Construct a one-shot pump just for the scan; it owns its own
            // MetadataPlaybackSource and disposes it after the scan finishes.
            var scanPump = new MetadataPlaybackPump(_metadataItem,
                _ => ExtractorConfig.FromManager(_viewItemManager),
                (_, __) => { });

            scanPump.ScanRangeAsync(fromUtc, nowUtc, maxFrames, ct).ContinueWith(t =>
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (ct.IsCancellationRequested) return;
                    if (_lineRenderer == null) return;

                    if (!t.IsCanceled && !t.IsFaulted && t.Result != null)
                    {
                        var samples = new List<(double, DateTime)>(t.Result.Count);
                        foreach (var r in t.Result)
                        {
                            if (double.TryParse(r.Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v))
                                samples.Add((v, r.TimestampUtc));
                        }
                        _lineRenderer.ResetWithSamples(samples);
                        if (samples.Count > 0)
                        {
                            _hasReceivedValue = true;
                            _lastValueUtc = samples[samples.Count - 1].Item2;
                        }
                    }

                    // Restore the chart visibility regardless of scan outcome —
                    // even an empty backfill still leads into live streaming.
                    noDataPanel.Visibility = Visibility.Collapsed;
                    chartRoot.Visibility = Visibility.Visible;
                    StartLive();
                }));
            }, System.Threading.Tasks.TaskScheduler.Default);
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
        private LineChartConfig BuildLineChartConfig()
        {
            var cfg = LineChartConfig.FromManager(_viewItemManager);
            if (_sessionWindowSecondsOverride.HasValue && _sessionWindowSecondsOverride.Value > 0)
                cfg.WindowSeconds = _sessionWindowSecondsOverride.Value;
            return cfg;
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

        private void BuildWindowPickerOptions()
        {
            windowPickerOptions.Children.Clear();
            int currentEffective = BuildLineChartConfig().WindowSeconds;
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
            int savedDefault = LineChartConfig.FromManager(_viewItemManager).WindowSeconds;
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
            if (_lineRenderer == null) return;

            var cfg = BuildLineChartConfig();
            _lineRenderer.Configure(cfg);

            var mode = EnvironmentManager.Instance.Mode;
            if (mode == Mode.ClientLive)
            {
                // Tear down the live subscription and re-enter through the same
                // path ApplyMode uses, so a wide window backfills from archive
                // first and a short window starts streaming immediately.
                StopLive();
                if (cfg.WindowSeconds > 60)
                    BackfillLineChartLive(cfg);
                else
                    StartLive();
            }
            else if (mode == Mode.ClientPlayback)
            {
                // Force the next playback time message to refill via range scan
                // around the new (likely larger or smaller) window.
                _lastBackfillCursorUtc = null;
            }
        }

        private void UpdateWindowPickerLabel()
        {
            int effective = BuildLineChartConfig().WindowSeconds;
            string label = FormatWindow(effective);
            if (_sessionWindowSecondsOverride.HasValue) label += "*";
            windowPickerText.Text = label;
        }

        // Kick off a range scan covering [cursor - window, cursor] and on success
        // bulk-load the results into the line chart. Cancels any previous in-flight
        // scan first so rapid scrubbing doesn't pile up overlapping requests.
        private void BackfillLineChart(DateTime cursorUtc, LineChartConfig cfg)
        {
            if (_pump == null || _lineRenderer == null) return;
            try { _backfillCts?.Cancel(); } catch { }
            _backfillCts = new System.Threading.CancellationTokenSource();
            var ct = _backfillCts.Token;
            var fromUtc = cursorUtc.AddSeconds(-cfg.WindowSeconds);
            _lastBackfillCursorUtc = cursorUtc;

            // Cap pulled frames at a few thousand to keep the scan bounded for
            // very high-cadence sources or wide windows.
            const int maxFrames = 4000;
            _pump.ScanRangeAsync(fromUtc, cursorUtc, maxFrames, ct).ContinueWith(t =>
            {
                if (t.IsCanceled || t.IsFaulted) return;
                var raw = t.Result;
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (ct.IsCancellationRequested) return;
                    if (_lineRenderer == null) return;
                    var samples = new List<(double, DateTime)>(raw.Count);
                    foreach (var r in raw)
                    {
                        if (double.TryParse(r.Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v))
                            samples.Add((v, r.TimestampUtc));
                    }
                    _lineRenderer.ResetWithSamples(samples);
                    _lineRenderer.SetCursor(cursorUtc);
                    if (samples.Count > 0)
                    {
                        _hasReceivedValue = true;
                        noDataPanel.Visibility = Visibility.Collapsed;
                    }
                }));
            }, System.Threading.Tasks.TaskScheduler.Default);
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

                    // LineChart playback: small scrubs just move the cursor, but
                    // jumps bigger than half the visible window (or the first time
                    // we see a cursor) trigger a fresh range scan to refill the
                    // visible buffer with archive samples.
                    if (_lineRenderer != null)
                    {
                        _lineRenderer.SetCursor(utc.Value);
                        var cfg = BuildLineChartConfig();
                        bool needBackfill = !_lastBackfillCursorUtc.HasValue
                            || Math.Abs((utc.Value - _lastBackfillCursorUtc.Value).TotalSeconds) > cfg.WindowSeconds / 2.0;
                        if (needBackfill)
                            BackfillLineChart(utc.Value, cfg);
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
                case "LineChart":
                    _lineRenderer = new LineChartRenderer();
                    _lineRenderer.Configure(BuildLineChartConfig());
                    visual = _lineRenderer.Visual;
                    isChart = true;
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
                // Chart renderer is already visible (shown empty); only the
                // bitmap-scaled host needs the first-value reveal.
                if (_lineRenderer == null)
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
            else if (_lineRenderer != null)
            {
                if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                {
                    var ts = _lastValueUtc ?? DateTime.UtcNow;
                    _lineRenderer.AddSample(v, ts);
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
