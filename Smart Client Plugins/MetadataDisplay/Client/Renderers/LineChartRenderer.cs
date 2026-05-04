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

    internal sealed class LineChartConfig
    {
        public int WindowSeconds;
        public double? YMin;
        public double? YMax;
        public WpfColor LineColor;
        public bool FillEnabled;
        public bool ShowMarker;
        public LineChartType LineType;
        public double LineThickness;
        public bool ZoomEnabled;
        public NumericConfig Numeric; // unit + threshold colors + Min/Max for thresholds

        public static LineChartConfig FromManager(MetadataDisplayViewItemManager m)
        {
            int win = 60;
            if (int.TryParse(m.LineWindowSeconds, NumberStyles.Integer, CultureInfo.InvariantCulture, out var w) && w > 0)
                win = w;

            // Smoothing flag stays for back-compat — when true, force LineType=Smooth.
            var lt = LineChartType.Straight;
            if (string.Equals(m.LineSmoothing, "true", StringComparison.OrdinalIgnoreCase)) lt = LineChartType.Smooth;
            if (string.Equals(m.LineType, "Smooth", StringComparison.OrdinalIgnoreCase)) lt = LineChartType.Smooth;
            else if (string.Equals(m.LineType, "Step", StringComparison.OrdinalIgnoreCase)) lt = LineChartType.Step;
            else if (string.Equals(m.LineType, "Straight", StringComparison.OrdinalIgnoreCase)) lt = LineChartType.Straight;

            double thickness = 2;
            if (double.TryParse(m.LineThickness, NumberStyles.Float, CultureInfo.InvariantCulture, out var t) && t > 0) thickness = t;

            return new LineChartConfig
            {
                WindowSeconds = win,
                YMin = ParseNullable(m.LineYMin),
                YMax = ParseNullable(m.LineYMax),
                LineColor = ColorUtil.Parse(m.LineColor, WpfColor.FromRgb(0x4F, 0xC3, 0xF7)),
                FillEnabled = string.Equals(m.LineFill, "true", StringComparison.OrdinalIgnoreCase),
                ShowMarker = string.Equals(m.LineShowMarker, "true", StringComparison.OrdinalIgnoreCase),
                LineType = lt,
                LineThickness = thickness,
                ZoomEnabled = !string.Equals(m.LineZoomEnabled, "false", StringComparison.OrdinalIgnoreCase),
                Numeric = NumericConfig.FromManager(m),
            };
        }

        private static double? ParseNullable(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? (double?)v : null;
        }
    }

    // Time-series line chart backed by LiveCharts2 / SkiaSharp.
    // - Holds an in-memory ring buffer of (DateTime, double) samples; window is
    //   anchored to the latest sample so it works for both live (latest=now) and
    //   playback (latest=cursor).
    // - Threshold model: dashed horizontal lines at warn (Min) and critical (Max)
    //   instead of filled bands — easier to read against time-series data.
    // - Line-type styles: straight, smooth, step. Picks a different LiveCharts
    //   series type for Step (StepLineSeries) since it's a different geometry.
    // - Optional X-axis zoom + auto-pause: when the user wheels or drags, we stop
    //   sliding the visible window so they can inspect; a "Resume live" overlay
    //   button lets them re-engage live tracking.
    internal sealed class LineChartRenderer
    {
        private readonly Grid _root;
        private CartesianChart _chart;
        private ISeries _series;
        private LineSeries<DateTimePoint> _lineSeries;
        private StepLineSeries<DateTimePoint> _stepSeries;
        // ObservableCollection so LiveCharts auto-redraws when samples are added /
        // removed — needed because we swap series instances when LineType changes
        // (a plain List wouldn't notify the new series of incoming samples).
        private readonly ObservableCollection<DateTimePoint> _points = new ObservableCollection<DateTimePoint>();
        private readonly Axis _xAxis;
        private readonly Axis _yAxis;

        private LineChartConfig _cfg;
        private LineChartType _activeType = LineChartType.Straight;

        // Cursor + threshold sections (rebuilt by ApplySections each Configure).
        private readonly RectangularSection _cursorSection;
        private DateTime? _cursorTime;

        // Pause state — when true, we keep appending samples but stop sliding the
        // X axis. Auto-engaged when the user manipulates the chart (zoom/pan).
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
                    return t.ToString("HH:mm:ss");
                },
                LabelsPaint = new SolidColorPaint(SKColors.Gray),
                TextSize = 10,
                MinStep = TimeSpan.FromSeconds(1).Ticks,
                SeparatorsPaint = new SolidColorPaint(new SKColor(0x33, 0x3B, 0x40)) { StrokeThickness = 0.5f },
            };

            _yAxis = new Axis
            {
                LabelsPaint = new SolidColorPaint(SKColors.Gray),
                TextSize = 10,
                SeparatorsPaint = new SolidColorPaint(new SKColor(0x33, 0x3B, 0x40)) { StrokeThickness = 0.5f },
            };

            _cursorSection = new RectangularSection
            {
                Stroke = new SolidColorPaint(SKColors.White) { StrokeThickness = 1.5f },
                Fill = null,
            };

            BuildSeries(LineChartType.Straight);
            BuildChart();

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

            _root = new Grid();
            _root.Children.Add(_chart);
            _root.Children.Add(_pauseOverlay);
        }

        public UIElement Visual => _root;

        public void Configure(LineChartConfig cfg)
        {
            _cfg = cfg;
            // Switch series type if the line-style category changed.
            if (cfg.LineType != _activeType)
            {
                BuildSeries(cfg.LineType);
                _chart.Series = new[] { _series };
            }
            ApplyConfig();
        }

        // Add a sample. Pruning is anchored to the latest sample so playback (where
        // "latest" walks the cursor) gets the same window behaviour as live.
        public void AddSample(double value, DateTime utc)
        {
            if (_cfg == null) return;
            if (utc.Kind != DateTimeKind.Utc) utc = utc.ToUniversalTime();

            // Drop duplicate / out-of-order samples for the same timestamp — keeps
            // the chart monotonic on X.
            if (_points.Count > 0 && _points[_points.Count - 1].DateTime >= utc)
                _points[_points.Count - 1] = new DateTimePoint(utc, value);
            else
                _points.Add(new DateTimePoint(utc, value));

            // Hard cap the buffer so very long sessions don't grow without bound.
            // Twice the window in samples is plenty of headroom.
            int hardCap = Math.Max(2048, _cfg.WindowSeconds * 60);
            while (_points.Count > hardCap) _points.RemoveAt(0);

            PruneAndRescale();
        }

        public void SetCursor(DateTime? utc)
        {
            _cursorTime = utc;
            if (_cfg != null) PruneAndRescale();
        }

        public void Clear()
        {
            _points.Clear();
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

        // Tooltip formatter shared by both series types. The X-axis tooltip already
        // shows the time; the series tooltip just shows the value to avoid the
        // "08:44:25 / 08:44:25 0.93" duplication the user flagged.
        private static string FormatTooltip(LiveChartsCore.Kernel.ChartPoint point)
            => point.Coordinate.PrimaryValue.ToString("0.##", CultureInfo.InvariantCulture);

        private void BuildSeries(LineChartType type)
        {
            _activeType = type;
            if (type == LineChartType.Step)
            {
                _stepSeries = new StepLineSeries<DateTimePoint>
                {
                    Values = _points,
                    YToolTipLabelFormatter = FormatTooltip,
                };
                _lineSeries = null;
                _series = _stepSeries;
            }
            else
            {
                _lineSeries = new LineSeries<DateTimePoint>
                {
                    Values = _points,
                    YToolTipLabelFormatter = FormatTooltip,
                };
                _stepSeries = null;
                _series = _lineSeries;
            }
        }

        private void BuildChart()
        {
            _chart = new CartesianChart
            {
                Series = new[] { _series },
                XAxes = new[] { _xAxis },
                YAxes = new[] { _yAxis },
                Sections = new RectangularSection[0],
                Background = System.Windows.Media.Brushes.Transparent,
                LegendPosition = LegendPosition.Hidden,
                // Hover tooltip — LiveCharts2 picks the nearest point. The label
                // text is built by the series' YToolTipLabelFormatter. Theme the
                // tooltip to match the Smart Client dark UI (default LiveCharts
                // palette is light-on-grey, which clashed with the dark widget).
                TooltipPosition = TooltipPosition.Top,
                TooltipBackgroundPaint = new SolidColorPaint(new SKColor(0x2A, 0x32, 0x36, 0xF2)),
                TooltipTextPaint = new SolidColorPaint(new SKColor(0xE6, 0xEA, 0xEC)),
                TooltipTextSize = 12,
                AnimationsSpeed = TimeSpan.FromMilliseconds(0),
                MinHeight = 80,
                MinWidth = 120,
            };
            // Auto-pause the rolling window the moment the user starts interacting
            // (wheel zoom or click-drag pan). Without this, samples would keep
            // sliding the X-axis out from under their inspection.
            _chart.PreviewMouseWheel += (s, e) => { if (_cfg != null && _cfg.ZoomEnabled) Pause(); };
            _chart.PreviewMouseDown  += (s, e) =>
            {
                if (_cfg == null || !_cfg.ZoomEnabled) return;
                if (e.ChangedButton == MouseButton.Left || e.ChangedButton == MouseButton.Middle) Pause();
            };
        }

        private void ApplyConfig()
        {
            var sk = ToSk(_cfg.LineColor);
            var stroke = new SolidColorPaint(sk) { StrokeThickness = (float)_cfg.LineThickness };
            var fill = _cfg.FillEnabled
                ? new SolidColorPaint(new SKColor(sk.Red, sk.Green, sk.Blue, 0x40))
                : null;

            if (_lineSeries != null)
            {
                _lineSeries.Stroke = stroke;
                _lineSeries.Fill = fill;
                _lineSeries.LineSmoothness = _cfg.LineType == LineChartType.Smooth ? 0.65 : 0;
                _lineSeries.GeometrySize = _cfg.ShowMarker ? 4 : 0;
                _lineSeries.GeometryStroke = new SolidColorPaint(sk) { StrokeThickness = 1.5f };
                _lineSeries.GeometryFill = new SolidColorPaint(sk);
            }
            if (_stepSeries != null)
            {
                _stepSeries.Stroke = stroke;
                _stepSeries.Fill = fill;
                _stepSeries.GeometrySize = _cfg.ShowMarker ? 4 : 0;
                _stepSeries.GeometryStroke = new SolidColorPaint(sk) { StrokeThickness = 1.5f };
                _stepSeries.GeometryFill = new SolidColorPaint(sk);
            }

            _yAxis.MinLimit = _cfg.YMin;
            _yAxis.MaxLimit = _cfg.YMax;

            // X-axis zoom + click-drag pan.
            _chart.ZoomMode = _cfg.ZoomEnabled ? ZoomAndPanMode.X : ZoomAndPanMode.None;

            ApplySections();
            PruneAndRescale();
        }

        private void ApplySections()
        {
            var sections = new List<RectangularSection>();

            if (_cfg != null && _cfg.Numeric != null && _cfg.Numeric.Enabled)
            {
                var n = _cfg.Numeric;
                // Two dashed horizontal threshold lines — warn boundary (Min) and
                // critical boundary (Max). Replaces the filled-band approach because
                // bands hid the line and competed visually with threshold colors.
                if (n.Min.HasValue) sections.Add(MakeThresholdLine(n.Min.Value, n.HighIsBad ? n.ColorWarn : n.ColorBad));
                if (n.Max.HasValue) sections.Add(MakeThresholdLine(n.Max.Value, n.HighIsBad ? n.ColorBad : n.ColorWarn));
            }

            if (_cursorTime.HasValue)
            {
                _cursorSection.Xi = _cursorTime.Value.Ticks;
                _cursorSection.Xj = _cursorTime.Value.Ticks;
                sections.Add(_cursorSection);
            }

            _chart.Sections = sections;
        }

        private static RectangularSection MakeThresholdLine(double y, WpfColor color)
        {
            var sk = ToSk(color);
            return new RectangularSection
            {
                Yi = y,
                Yj = y,
                Stroke = new SolidColorPaint(sk)
                {
                    StrokeThickness = 1.4f,
                    PathEffect = new DashEffect(new float[] { 6, 4 }),
                },
                Fill = null,
            };
        }

        private void PruneAndRescale()
        {
            if (_points.Count == 0)
            {
                _xAxis.MinLimit = null;
                _xAxis.MaxLimit = null;
                ApplySections();
                return;
            }

            // While paused, leave the X-axis alone so the user keeps their zoom/pan
            // window. We still let new samples accumulate in the buffer.
            if (_paused)
            {
                ApplySections();
                return;
            }

            var latest = _cursorTime ?? _points[_points.Count - 1].DateTime;
            var cutoff = latest.AddSeconds(-_cfg.WindowSeconds);

            // Drop samples older than the rolling window. List is sorted by time
            // (we enforce that on insert) so we always drop from the front.
            while (_points.Count > 0 && _points[0].DateTime < cutoff) _points.RemoveAt(0);

            _xAxis.MinLimit = cutoff.Ticks;
            _xAxis.MaxLimit = latest.Ticks;

            ApplySections();
        }

        private static SKColor ToSk(WpfColor c) => new SKColor(c.R, c.G, c.B, c.A);
    }
}
