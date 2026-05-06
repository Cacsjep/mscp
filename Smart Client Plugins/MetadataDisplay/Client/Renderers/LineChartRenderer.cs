using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.Drawing;
using LiveChartsCore.Measure;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using LiveChartsCore.SkiaSharpView.Painting.Effects;
using LiveChartsCore.SkiaSharpView.WPF;
using SkiaSharp;
using WpfColor = System.Windows.Media.Color;

namespace MetadataDisplay.Client.Renderers
{
    internal enum LineChartType { Straight, Smooth, Step }

    internal enum LineAggregation { Mean, Min, Max }

    // Per-series view-model. The renderer keeps one of these per LineSeries
    // entry in the configuration. Holds visual settings (color/thickness/line
    // type/axis/threshold/visibility) plus the per-series bucket buffer and
    // the live-bound LiveCharts2 series instances. Envelope min/max lines and
    // threshold sections are owned per-series so each line can have its own
    // band on its own Y axis.
    internal sealed class LineSeriesRuntime
    {
        public string Name = string.Empty;
        public WpfColor Color = WpfColor.FromRgb(0x4F, 0xC3, 0xF7);
        public double Thickness = 2;
        public LineChartType Type = LineChartType.Straight;
        public bool FillEnabled;
        public bool ShowMarker;
        public bool Visible = true;
        // "Right" routes this series's points to the right Y axis. The renderer
        // adds the right axis on demand (only when at least one series uses it).
        public bool UseRightAxis;
        // Per-series threshold. Disabled by default; when on, the renderer paints
        // dashed warn / critical lines at this series's Y values on the
        // appropriate axis.
        public NumericConfig Threshold;

        // Live-bound point collections - these back the LiveCharts2 series and
        // are mutated incrementally by AddSample so the chart updates without
        // redrawing the whole list.
        public readonly ObservableCollection<DateTimePoint> MeanPoints = new ObservableCollection<DateTimePoint>();
        public readonly ObservableCollection<DateTimePoint> EnvMinPoints = new ObservableCollection<DateTimePoint>();
        public readonly ObservableCollection<DateTimePoint> EnvMaxPoints = new ObservableCollection<DateTimePoint>();

        // Bucket buffer. Per-series so each can be backfilled / extended at its
        // own cadence without dragging the others.
        public readonly List<Bucket> Buckets = new List<Bucket>(LineChartRenderer.TargetBucketCount * 2);

        // LiveCharts2 series instances. Either MeanLine OR MeanStep is
        // populated depending on Type; the unused one stays null.
        public LineSeries<DateTimePoint> MeanLine;
        public StepLineSeries<DateTimePoint> MeanStep;
        public LineSeries<DateTimePoint> EnvelopeMin;
        public LineSeries<DateTimePoint> EnvelopeMax;
        public ISeries MeanSeries;
    }

    internal sealed class LineChartConfig
    {
        public int WindowSeconds;
        public double? YMin;
        public double? YMax;
        public bool ZoomEnabled;
        public LineAggregation Aggregation;
        public bool EnvelopeEnabled;
        public bool ShowLegend;
        // Set by the view-item when entering playback. Suppresses the pause
        // overlay because there is no live tail to "pause" - the cursor anchors
        // the visible window.
        public bool PlaybackMode;

        // The series this chart should render. Order is significant - index 0
        // is the first series in the configuration, etc. Single-series widgets
        // pass a one-element list; multi-line widgets fill it from
        // LineSeriesParser.LoadFromManager(...).
        public List<LineSeries> Series = new List<LineSeries>();

        // Builds a chart-wide config from the manager. The Series list is
        // populated separately from LineSeriesParser.LoadFromManager (modern
        // widgets) or the legacy migration path (older widgets).
        public static LineChartConfig FromManager(MetadataDisplayViewItemManager m)
        {
            int win = 60;
            if (int.TryParse(m.LineWindowSeconds, NumberStyles.Integer, CultureInfo.InvariantCulture, out var w) && w > 0)
                win = w;

            var cfg = new LineChartConfig
            {
                WindowSeconds = win,
                YMin = ParseNullable(m.LineYMin),
                YMax = ParseNullable(m.LineYMax),
                ZoomEnabled = !string.Equals(m.LineZoomEnabled, "false", StringComparison.OrdinalIgnoreCase),
                Aggregation = ParseAgg(m.LineAggregation),
                EnvelopeEnabled = string.Equals(m.LineEnvelope, "true", StringComparison.OrdinalIgnoreCase),
                ShowLegend = string.Equals(m.LineShowLegend, "true", StringComparison.OrdinalIgnoreCase),
                Series = LineSeriesParser.LoadFromManager(m),
            };
            return cfg;
        }

        private static LineAggregation ParseAgg(string s)
        {
            if (string.Equals(s, "Min", StringComparison.OrdinalIgnoreCase)) return LineAggregation.Min;
            if (string.Equals(s, "Max", StringComparison.OrdinalIgnoreCase)) return LineAggregation.Max;
            return LineAggregation.Mean;
        }

        private static double? ParseNullable(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? (double?)v : null;
        }
    }

    // One time bucket holding the aggregates needed to render mean / min / max
    // / count without keeping the underlying samples around.
    internal sealed class Bucket
    {
        public DateTime StartUtc;
        public double Sum;
        public double Min = double.PositiveInfinity;
        public double Max = double.NegativeInfinity;
        public int Count;

        public void Add(double v)
        {
            Sum += v;
            if (v < Min) Min = v;
            if (v > Max) Max = v;
            Count++;
        }

        public double Pick(LineAggregation agg)
        {
            switch (agg)
            {
                case LineAggregation.Min: return Count > 0 ? Min : 0;
                case LineAggregation.Max: return Count > 0 ? Max : 0;
                case LineAggregation.Mean:
                default:                  return Count > 0 ? Sum / Count : 0;
            }
        }
    }

    // Time-series line chart backed by LiveCharts2 / SkiaSharp. Supports up to
    // LineSeriesParser.MaxSeries lines on the same chart, each with its own
    // color / thickness / line type / threshold band / axis assignment, plus
    // an optional legend strip and a chart-wide rolling window.
    //
    // Storage: each series owns its own bucket buffer sized by the chart-wide
    // window length (target ~300 visible buckets). This keeps the chart cost
    // constant regardless of how long the user wants to look back. Each
    // bucket carries Sum/Min/Max/Count so we can switch aggregation modes
    // without re-fetching.
    //
    // Threshold model: per-series dashed horizontal lines at warn (Min) and
    // critical (Max) value levels, painted on the series's chosen axis.
    //
    // Playback support: ResetWithSamplesForSeries(...) wipes a single series's
    // buckets so the ViewItem can replay a range scan into the chart with
    // cursor anchoring; ResetAllWithSamples is a multi-series convenience.
    internal sealed class LineChartRenderer
    {
        // Aim for this many buckets across the window - chart-render cost is
        // proportional to point count, so we cap at a stable budget regardless
        // of how long the user's window is.
        public const int TargetBucketCount = 300;

        private readonly Grid _root;
        private CartesianChart _chart;
        private readonly Axis _xAxis;
        private readonly Axis _yLeftAxis;
        private readonly Axis _yRightAxis;
        private readonly RectangularSection _cursorSection;
        private readonly StackPanel _legendPanel;
        private readonly Border _legendHost;

        private LineChartConfig _cfg;
        private TimeSpan _bucketSize = TimeSpan.FromSeconds(1);
        private readonly List<LineSeriesRuntime> _runtimes = new List<LineSeriesRuntime>();

        private DateTime? _cursorTime;
        private bool _paused;
        private readonly Border _pauseOverlay;
        private readonly TextBlock _pauseText;

        public LineChartRenderer()
        {
            _xAxis = new Axis
            {
                Labeler = ticks =>
                {
                    var t = new DateTime((long)ticks, DateTimeKind.Utc).ToLocalTime();
                    var span = TimeSpan.FromTicks((long)((_xAxis?.MaxLimit ?? 0) - (_xAxis?.MinLimit ?? 0)));
                    return span.TotalHours >= 1 ? t.ToString("HH:mm") : t.ToString("HH:mm:ss");
                },
                LabelsPaint = new SolidColorPaint(SKColors.Gray),
                TextSize = 10,
                MinStep = TimeSpan.FromSeconds(1).Ticks,
                SeparatorsPaint = new SolidColorPaint(new SKColor(0x33, 0x3B, 0x40)) { StrokeThickness = 0.5f },
            };

            _yLeftAxis = new Axis
            {
                LabelsPaint = new SolidColorPaint(SKColors.Gray),
                TextSize = 10,
                SeparatorsPaint = new SolidColorPaint(new SKColor(0x33, 0x3B, 0x40)) { StrokeThickness = 0.5f },
            };

            // Right axis is constructed up-front but only added to the chart
            // when at least one series uses it - keeps single-axis charts
            // looking identical to the v1 layout.
            _yRightAxis = new Axis
            {
                LabelsPaint = new SolidColorPaint(SKColors.Gray),
                TextSize = 10,
                Position = LiveChartsCore.Measure.AxisPosition.End,
                SeparatorsPaint = null,  // share gridlines with the left axis
            };

            _cursorSection = new RectangularSection
            {
                Stroke = new SolidColorPaint(SKColors.White) { StrokeThickness = 1.5f },
                Fill = null,
            };

            _chart = BuildChartShell();

            _pauseText = new TextBlock
            {
                Text = "Paused (click to resume live)",
                Foreground = new SolidColorBrush(WpfColor.FromRgb(0xF5, 0xF7, 0xF8)),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(8, 3, 8, 3),
            };
            _pauseOverlay = new Border
            {
                Background = new SolidColorBrush(WpfColor.FromArgb(0xCC, 0x1C, 0x23, 0x26)),
                BorderBrush = new SolidColorBrush(WpfColor.FromRgb(0xE6, 0x95, 0x00)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(2),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 6, 0, 0),
                Visibility = Visibility.Collapsed,
                Cursor = Cursors.Hand,
                Child = _pauseText,
            };
            _pauseOverlay.MouseLeftButtonUp += (s, e) => Resume();

            _legendPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
            };
            _legendHost = new Border
            {
                Padding = new Thickness(6, 4, 6, 4),
                Background = new SolidColorBrush(WpfColor.FromArgb(0x88, 0x1F, 0x25, 0x28)),
                Visibility = Visibility.Collapsed,
            };
            _legendHost.Child = _legendPanel;

            _root = new Grid();
            _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            Grid.SetRow(_legendHost, 0);
            Grid.SetRow(_chart, 1);
            Grid.SetRow(_pauseOverlay, 1);
            _root.Children.Add(_legendHost);
            _root.Children.Add(_chart);
            _root.Children.Add(_pauseOverlay);
        }

        public UIElement Visual => _root;

        public int SeriesCount => _runtimes.Count;

        public void Configure(LineChartConfig cfg)
        {
            var prevWindow = _cfg?.WindowSeconds ?? -1;
            _cfg = cfg;

            _bucketSize = ChooseBucketSize(cfg.WindowSeconds);

            // Window changes invalidate every series's bucket layout - drop
            // them all so we don't end up mixing widths.
            if (prevWindow > 0 && prevWindow != cfg.WindowSeconds)
            {
                foreach (var rt in _runtimes) rt.Buckets.Clear();
            }

            RebuildRuntimesFromConfig();
            RebuildAllPoints();
            RebuildChartSeries();
            RebuildLegend();
            ApplyAxes();

            if (cfg.PlaybackMode && _paused) Resume();
            if (cfg.PlaybackMode) _pauseOverlay.Visibility = Visibility.Collapsed;
            PruneAndRescale();
        }

        // Add a single sample to a specific series. Single-series widgets just
        // call AddSample(0, …) - same code path so behavior matches v1.
        public void AddSample(int seriesIndex, double value, DateTime utc)
        {
            if (_cfg == null) return;
            if (seriesIndex < 0 || seriesIndex >= _runtimes.Count) return;
            if (utc.Kind != DateTimeKind.Utc) utc = utc.ToUniversalTime();
            BucketAppend(_runtimes[seriesIndex], value, utc);
            PruneAndRescale();
        }

        // Legacy single-series shorthand - keeps the v1 caller signature
        // working. New callers should prefer the two-arg AddSample(int, …).
        public void AddSample(double value, DateTime utc) => AddSample(0, value, utc);

        // Bulk-load a single series's buckets from a range scan. Wipes existing
        // buckets so the chart shows exactly the requested range.
        public void ResetWithSamplesForSeries(int seriesIndex, IReadOnlyList<(double Value, DateTime Utc)> samples)
        {
            if (_cfg == null) return;
            if (seriesIndex < 0 || seriesIndex >= _runtimes.Count) return;
            var rt = _runtimes[seriesIndex];
            rt.Buckets.Clear();
            foreach (var s in samples)
            {
                var u = s.Utc;
                if (u.Kind != DateTimeKind.Utc) u = u.ToUniversalTime();
                BucketAppend(rt, s.Value, u);
            }
            RebuildPointsForSeries(rt);
            PruneAndRescale();
        }

        // Convenience: bulk-load every series in one shot. The samples array
        // must align with the configured series order; pass an empty list for
        // series the caller didn't fetch this round.
        public void ResetAllWithSamples(IReadOnlyList<IReadOnlyList<(double Value, DateTime Utc)>> samplesPerSeries)
        {
            if (_cfg == null) return;
            int n = Math.Min(_runtimes.Count, samplesPerSeries?.Count ?? 0);
            for (int i = 0; i < n; i++)
            {
                var rt = _runtimes[i];
                rt.Buckets.Clear();
                var src = samplesPerSeries[i];
                if (src != null)
                {
                    foreach (var s in src)
                    {
                        var u = s.Utc;
                        if (u.Kind != DateTimeKind.Utc) u = u.ToUniversalTime();
                        BucketAppend(rt, s.Value, u);
                    }
                }
                RebuildPointsForSeries(rt);
            }
            PruneAndRescale();
        }

        // Legacy single-series shorthand - preserves the v1 caller signature.
        public void ResetWithSamples(IReadOnlyList<(double Value, DateTime Utc)> samples)
            => ResetWithSamplesForSeries(0, samples);

        public void SetCursor(DateTime? utc)
        {
            _cursorTime = utc;
            if (_cfg != null) PruneAndRescale();
        }

        public void Clear()
        {
            foreach (var rt in _runtimes)
            {
                rt.Buckets.Clear();
                rt.MeanPoints.Clear();
                rt.EnvMinPoints.Clear();
                rt.EnvMaxPoints.Clear();
            }
            _cursorTime = null;
            ApplySections();
        }

        public void Pause()
        {
            if (_paused) return;
            _paused = true;
            _pauseOverlay.Visibility = Visibility.Visible;
        }

        public void Resume()
        {
            if (!_paused) return;
            _paused = false;
            _pauseOverlay.Visibility = Visibility.Collapsed;
            PruneAndRescale();
        }

        // Map a configured LineSeries entry to a runtime, preserving any
        // existing buckets when the series identity hasn't changed (so live
        // streaming through a settings tweak doesn't lose history).
        private void RebuildRuntimesFromConfig()
        {
            var oldByKey = new Dictionary<string, LineSeriesRuntime>(StringComparer.Ordinal);
            foreach (var rt in _runtimes)
            {
                if (!oldByKey.ContainsKey(rt.Name)) oldByKey[rt.Name] = rt;
            }
            _runtimes.Clear();

            var seriesList = _cfg?.Series ?? new List<LineSeries>();
            foreach (var s in seriesList)
            {
                var key = string.IsNullOrEmpty(s.Name) ? (s.DataKey ?? string.Empty) : s.Name;
                if (!oldByKey.TryGetValue(key, out var rt))
                    rt = new LineSeriesRuntime();

                rt.Name = string.IsNullOrEmpty(s.Name) ? (s.DataKey ?? string.Empty) : s.Name;
                rt.Color = ColorUtil.Parse(s.Color, WpfColor.FromRgb(0x4F, 0xC3, 0xF7));
                rt.Thickness = s.Thickness > 0 ? s.Thickness : 2;
                rt.Type = ParseLineType(s.LineType);
                rt.FillEnabled = s.FillEnabled;
                rt.ShowMarker = s.ShowMarker;
                rt.UseRightAxis = s.YAxis == LineSeriesAxis.Right;
                rt.Visible = s.Visible;
                // Per-series threshold lifted into the renderer's reusable
                // NumericConfig shape.
                var th = s.Threshold ?? new LineSeriesThreshold();
                rt.Threshold = new NumericConfig
                {
                    Enabled = th.Enabled,
                    Min = th.Min,
                    Max = th.Max,
                    HighIsBad = th.HighIsBad,
                    ColorOk = ColorUtil.Parse(th.ColorOk, WpfColor.FromRgb(0x3C, 0xB3, 0x71)),
                    ColorWarn = ColorUtil.Parse(th.ColorWarn, WpfColor.FromRgb(0xE6, 0x95, 0x00)),
                    ColorBad = ColorUtil.Parse(th.ColorBad, WpfColor.FromRgb(0xD8, 0x39, 0x2C)),
                    Unit = string.Empty,
                };
                BuildSeriesObjects(rt);
                _runtimes.Add(rt);
            }
        }

        private static LineChartType ParseLineType(string s)
        {
            if (string.Equals(s, "Smooth", StringComparison.OrdinalIgnoreCase)) return LineChartType.Smooth;
            if (string.Equals(s, "Step", StringComparison.OrdinalIgnoreCase)) return LineChartType.Step;
            return LineChartType.Straight;
        }

        // (Re)create the LiveCharts2 series instances backing this runtime.
        // Called when the runtime is first added or when the line type changes.
        private void BuildSeriesObjects(LineSeriesRuntime rt)
        {
            var sk = ToSk(rt.Color);
            var stroke = new SolidColorPaint(sk) { StrokeThickness = (float)rt.Thickness };
            SolidColorPaint fill = rt.FillEnabled
                ? new SolidColorPaint(new SKColor(sk.Red, sk.Green, sk.Blue, 0x40))
                : null;

            // Tear down old instances so the chart doesn't render stale shapes.
            rt.MeanLine = null;
            rt.MeanStep = null;

            int axisIndex = rt.UseRightAxis ? 1 : 0;
            string label = string.IsNullOrEmpty(rt.Name) ? "value" : rt.Name;

            if (rt.Type == LineChartType.Step)
            {
                rt.MeanStep = new StepLineSeries<DateTimePoint>
                {
                    Values = rt.MeanPoints,
                    Name = label,
                    Stroke = stroke,
                    Fill = fill,
                    GeometrySize = rt.ShowMarker ? 4 : 0,
                    GeometryStroke = new SolidColorPaint(sk) { StrokeThickness = 1.5f },
                    GeometryFill = new SolidColorPaint(sk),
                    YToolTipLabelFormatter = FormatTooltip,
                    ScalesYAt = axisIndex,
                };
                rt.MeanSeries = rt.MeanStep;
            }
            else
            {
                rt.MeanLine = new LineSeries<DateTimePoint>
                {
                    Values = rt.MeanPoints,
                    Name = label,
                    Stroke = stroke,
                    Fill = fill,
                    LineSmoothness = rt.Type == LineChartType.Smooth ? 0.65 : 0,
                    GeometrySize = rt.ShowMarker ? 4 : 0,
                    GeometryStroke = new SolidColorPaint(sk) { StrokeThickness = 1.5f },
                    GeometryFill = new SolidColorPaint(sk),
                    YToolTipLabelFormatter = FormatTooltip,
                    ScalesYAt = axisIndex,
                };
                rt.MeanSeries = rt.MeanLine;
            }

            // Envelope min/max - same hue, low opacity, thin dashed stroke.
            // Always built; the chart only includes them when the chart-wide
            // EnvelopeEnabled flag is on.
            var envStroke = new SolidColorPaint(new SKColor(sk.Red, sk.Green, sk.Blue, 0x80))
            {
                StrokeThickness = 1f,
                PathEffect = new DashEffect(new float[] { 3, 3 }),
            };
            rt.EnvelopeMin = new LineSeries<DateTimePoint>
            {
                Values = rt.EnvMinPoints,
                Name = label + " min",
                Fill = null,
                GeometrySize = 0,
                LineSmoothness = 0,
                Stroke = envStroke,
                IsVisibleAtLegend = false,
                ScalesYAt = axisIndex,
            };
            rt.EnvelopeMax = new LineSeries<DateTimePoint>
            {
                Values = rt.EnvMaxPoints,
                Name = label + " max",
                Fill = null,
                GeometrySize = 0,
                LineSmoothness = 0,
                Stroke = envStroke,
                IsVisibleAtLegend = false,
                ScalesYAt = axisIndex,
            };
        }

        private void RebuildPointsForSeries(LineSeriesRuntime rt)
        {
            rt.MeanPoints.Clear();
            rt.EnvMinPoints.Clear();
            rt.EnvMaxPoints.Clear();
            for (int i = 0; i < rt.Buckets.Count; i++)
                UpdatePointForBucket(rt, rt.Buckets[i], i);
        }

        private void RebuildAllPoints()
        {
            foreach (var rt in _runtimes) RebuildPointsForSeries(rt);
        }

        // Compose the chart's Series list from the per-runtime lines. When
        // EnvelopeEnabled is on we include both env lines per series; when a
        // series is hidden via the legend toggle its main line is omitted (so
        // the legend toggle has visible effect even on a single-series chart).
        private void RebuildChartSeries()
        {
            var list = new List<ISeries>();
            foreach (var rt in _runtimes)
            {
                if (!rt.Visible) continue;
                if (_cfg != null && _cfg.EnvelopeEnabled)
                {
                    if (rt.EnvelopeMin != null) list.Add(rt.EnvelopeMin);
                    if (rt.EnvelopeMax != null) list.Add(rt.EnvelopeMax);
                }
                if (rt.MeanSeries != null) list.Add(rt.MeanSeries);
            }
            _chart.Series = list;
            ApplySections();
        }

        private CartesianChart BuildChartShell()
        {
            var c = new CartesianChart
            {
                Series = new List<ISeries>(),
                XAxes = new[] { _xAxis },
                YAxes = new[] { _yLeftAxis },
                Sections = new RectangularSection[0],
                Background = System.Windows.Media.Brushes.Transparent,
                LegendPosition = LegendPosition.Hidden,
                TooltipPosition = TooltipPosition.Top,
                TooltipBackgroundPaint = new SolidColorPaint(new SKColor(0x2A, 0x32, 0x36, 0xF2)),
                TooltipTextPaint = new SolidColorPaint(new SKColor(0xE6, 0xEA, 0xEC)),
                TooltipTextSize = 12,
                AnimationsSpeed = TimeSpan.FromMilliseconds(0),
                MinHeight = 80,
                MinWidth = 120,
            };
            c.PreviewMouseWheel += (s, e) =>
            {
                if (_cfg != null && _cfg.ZoomEnabled && !_cfg.PlaybackMode) Pause();
            };
            c.PreviewMouseDown += (s, e) =>
            {
                if (_cfg == null || !_cfg.ZoomEnabled || _cfg.PlaybackMode) return;
                if (e.ChangedButton == MouseButton.Left || e.ChangedButton == MouseButton.Middle) Pause();
            };
            return c;
        }

        private void ApplyAxes()
        {
            if (_cfg == null) return;
            // Right axis is added only when at least one series is assigned
            // to it - keeps single-axis charts visually identical to v1.
            bool wantRight = false;
            foreach (var rt in _runtimes)
            {
                if (rt.UseRightAxis && rt.Visible) { wantRight = true; break; }
            }
            _yLeftAxis.MinLimit = _cfg.YMin;
            _yLeftAxis.MaxLimit = _cfg.YMax;
            // Right axis auto-fits independently - single shared YMin/YMax
            // would be wrong for mixed-unit charts.
            _yRightAxis.MinLimit = null;
            _yRightAxis.MaxLimit = null;
            _chart.YAxes = wantRight
                ? new[] { _yLeftAxis, _yRightAxis }
                : new[] { _yLeftAxis };
            _chart.ZoomMode = _cfg.ZoomEnabled ? ZoomAndPanMode.X : ZoomAndPanMode.None;
        }

        private void RebuildLegend()
        {
            _legendPanel.Children.Clear();
            bool wantLegend = _cfg != null && _cfg.ShowLegend && _runtimes.Count > 0;
            _legendHost.Visibility = wantLegend ? Visibility.Visible : Visibility.Collapsed;
            if (!wantLegend) return;

            for (int i = 0; i < _runtimes.Count; i++)
            {
                var rt = _runtimes[i];
                int idx = i;  // capture for the click handler
                var chip = MakeLegendChip(rt.Color);
                var label = new TextBlock
                {
                    Text = string.IsNullOrEmpty(rt.Name) ? $"series {i + 1}" : rt.Name,
                    Foreground = new SolidColorBrush(WpfColor.FromRgb(0xCF, 0xD7, 0xDA)),
                    FontSize = 11,
                    Margin = new Thickness(4, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    TextDecorations = rt.Visible ? null : System.Windows.TextDecorations.Strikethrough,
                };
                var item = new Border
                {
                    Padding = new Thickness(6, 0, 6, 0),
                    Margin = new Thickness(0, 0, 4, 0),
                    Cursor = Cursors.Hand,
                    Opacity = rt.Visible ? 1.0 : 0.4,
                    Background = System.Windows.Media.Brushes.Transparent,
                };
                var sp = new StackPanel { Orientation = Orientation.Horizontal };
                sp.Children.Add(chip);
                sp.Children.Add(label);
                item.Child = sp;
                item.MouseLeftButtonUp += (s, e) =>
                {
                    if (idx < 0 || idx >= _runtimes.Count) return;
                    _runtimes[idx].Visible = !_runtimes[idx].Visible;
                    RebuildChartSeries();
                    RebuildLegend();
                    PruneAndRescale();
                };
                _legendPanel.Children.Add(item);
            }
        }

        // Tiny colored dot used by the legend.
        private static System.Windows.Shapes.Ellipse MakeLegendChip(WpfColor c)
        {
            return new System.Windows.Shapes.Ellipse
            {
                Width = 10,
                Height = 10,
                Fill = new SolidColorBrush(c),
                VerticalAlignment = VerticalAlignment.Center,
            };
        }

        private static string FormatTooltip(LiveChartsCore.Kernel.ChartPoint point)
            => point.Coordinate.PrimaryValue.ToString("0.##", CultureInfo.InvariantCulture);

        private static TimeSpan ChooseBucketSize(int windowSeconds)
        {
            double secs = Math.Max(1.0, (double)windowSeconds / TargetBucketCount);
            double[] snaps = { 1, 2, 5, 10, 15, 30, 60, 120, 300, 600, 900, 1800, 3600, 7200, 21600, 43200, 86400 };
            foreach (var s in snaps)
            {
                if (secs <= s) return TimeSpan.FromSeconds(s);
            }
            return TimeSpan.FromSeconds(86400);
        }

        private DateTime BucketStart(DateTime utc)
        {
            var ticks = _bucketSize.Ticks;
            return new DateTime((utc.Ticks / ticks) * ticks, DateTimeKind.Utc);
        }

        private void BucketAppend(LineSeriesRuntime rt, double value, DateTime utc)
        {
            var start = BucketStart(utc);
            Bucket b = null;
            if (rt.Buckets.Count > 0 && rt.Buckets[rt.Buckets.Count - 1].StartUtc == start)
                b = rt.Buckets[rt.Buckets.Count - 1];
            else if (rt.Buckets.Count > 0 && rt.Buckets[rt.Buckets.Count - 1].StartUtc > start)
            {
                int idx = rt.Buckets.FindLastIndex(x => x.StartUtc <= start);
                if (idx >= 0 && rt.Buckets[idx].StartUtc == start) b = rt.Buckets[idx];
                else
                {
                    b = new Bucket { StartUtc = start };
                    rt.Buckets.Insert(idx + 1, b);
                }
            }
            else
            {
                b = new Bucket { StartUtc = start };
                rt.Buckets.Add(b);
            }
            b.Add(value);
            UpdatePointForBucket(rt, b, rt.Buckets.Count - 1);
        }

        private void UpdatePointForBucket(LineSeriesRuntime rt, Bucket b, int bucketIndex)
        {
            if (_cfg == null) return;
            var meanPoint = new DateTimePoint(b.StartUtc, b.Pick(_cfg.Aggregation));
            if (bucketIndex < rt.MeanPoints.Count) rt.MeanPoints[bucketIndex] = meanPoint;
            else                                    rt.MeanPoints.Add(meanPoint);

            if (_cfg.EnvelopeEnabled)
            {
                var minPt = new DateTimePoint(b.StartUtc, b.Count > 0 ? b.Min : 0);
                var maxPt = new DateTimePoint(b.StartUtc, b.Count > 0 ? b.Max : 0);
                if (bucketIndex < rt.EnvMinPoints.Count) rt.EnvMinPoints[bucketIndex] = minPt; else rt.EnvMinPoints.Add(minPt);
                if (bucketIndex < rt.EnvMaxPoints.Count) rt.EnvMaxPoints[bucketIndex] = maxPt; else rt.EnvMaxPoints.Add(maxPt);
            }
        }

        // Threshold sections: per-series dashed lines at warn (Min) and
        // critical (Max) Y-values. Rendered on the series's chosen axis via
        // the section's ScalesYAt index. Cursor section spans both axes.
        private void ApplySections()
        {
            var sections = new List<RectangularSection>();
            foreach (var rt in _runtimes)
            {
                if (!rt.Visible) continue;
                var t = rt.Threshold;
                if (t == null || !t.Enabled) continue;
                int yIndex = rt.UseRightAxis ? 1 : 0;
                if (t.Min.HasValue)
                    sections.Add(MakeThresholdLine(t.Min.Value, t.HighIsBad ? t.ColorWarn : t.ColorBad, yIndex));
                if (t.Max.HasValue)
                    sections.Add(MakeThresholdLine(t.Max.Value, t.HighIsBad ? t.ColorBad : t.ColorWarn, yIndex));
            }
            if (_cursorTime.HasValue)
            {
                _cursorSection.Xi = _cursorTime.Value.Ticks;
                _cursorSection.Xj = _cursorTime.Value.Ticks;
                sections.Add(_cursorSection);
            }
            _chart.Sections = sections;
        }

        private static RectangularSection MakeThresholdLine(double y, WpfColor color, int yAxisIndex)
        {
            var sk = ToSk(color);
            return new RectangularSection
            {
                Yi = y,
                Yj = y,
                ScalesYAt = yAxisIndex,
                Stroke = new SolidColorPaint(sk)
                {
                    StrokeThickness = 1.4f,
                    PathEffect = new DashEffect(new float[] { 6, 4 }),
                },
                Fill = null,
            };
        }

        // Find the latest point across every series's buckets - drives the
        // X-axis right edge in live mode and the rolling-window cutoff.
        private DateTime? GetLatestPoint()
        {
            DateTime? latest = null;
            foreach (var rt in _runtimes)
            {
                if (rt.Buckets.Count == 0) continue;
                var t = rt.Buckets[rt.Buckets.Count - 1].StartUtc + _bucketSize;
                if (!latest.HasValue || t > latest.Value) latest = t;
            }
            return latest;
        }

        private void PruneAndRescale()
        {
            if (_cfg == null) return;
            if (_paused) { ApplySections(); return; }

            var latestPoint = GetLatestPoint();
            if (!latestPoint.HasValue && !_cursorTime.HasValue)
            {
                _xAxis.MinLimit = null;
                _xAxis.MaxLimit = null;
                ApplySections();
                return;
            }

            var anchor = _cursorTime ?? latestPoint.Value;
            var cutoff = anchor.AddSeconds(-_cfg.WindowSeconds);

            // Drop stale buckets per series.
            foreach (var rt in _runtimes)
            {
                int drop = 0;
                while (drop < rt.Buckets.Count && rt.Buckets[drop].StartUtc + _bucketSize < cutoff) drop++;
                if (drop > 0)
                {
                    rt.Buckets.RemoveRange(0, drop);
                    for (int i = 0; i < drop && rt.MeanPoints.Count > 0; i++) rt.MeanPoints.RemoveAt(0);
                    if (_cfg.EnvelopeEnabled)
                    {
                        for (int i = 0; i < drop && rt.EnvMinPoints.Count > 0; i++) rt.EnvMinPoints.RemoveAt(0);
                        for (int i = 0; i < drop && rt.EnvMaxPoints.Count > 0; i++) rt.EnvMaxPoints.RemoveAt(0);
                    }
                }
            }

            _xAxis.MinLimit = cutoff.Ticks;
            _xAxis.MaxLimit = anchor.Ticks;
            ApplySections();
        }

        private static SKColor ToSk(WpfColor c) => new SKColor(c.R, c.G, c.B, c.A);
    }
}
