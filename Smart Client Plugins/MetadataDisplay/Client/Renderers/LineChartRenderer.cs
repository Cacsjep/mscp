using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.Measure;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using LiveChartsCore.SkiaSharpView.WPF;
using SkiaSharp;
using WpfColor = System.Windows.Media.Color;

namespace MetadataDisplay.Client.Renderers
{
    internal sealed class LineChartConfig
    {
        public int WindowSeconds;
        public double? YMin;
        public double? YMax;
        public WpfColor LineColor;
        public bool FillEnabled;
        public bool Smooth;
        public bool ShowMarker;
        public NumericConfig Numeric; // unit + threshold colors + Min/Max for bands

        public static LineChartConfig FromManager(MetadataDisplayViewItemManager m)
        {
            int win = 60;
            if (int.TryParse(m.LineWindowSeconds, NumberStyles.Integer, CultureInfo.InvariantCulture, out var w) && w > 0)
                win = w;

            return new LineChartConfig
            {
                WindowSeconds = win,
                YMin = ParseNullable(m.LineYMin),
                YMax = ParseNullable(m.LineYMax),
                LineColor = ColorUtil.Parse(m.LineColor, WpfColor.FromRgb(0x4F, 0xC3, 0xF7)),
                FillEnabled = string.Equals(m.LineFill, "true", StringComparison.OrdinalIgnoreCase),
                Smooth = string.Equals(m.LineSmoothing, "true", StringComparison.OrdinalIgnoreCase),
                ShowMarker = !string.Equals(m.LineShowMarker, "false", StringComparison.OrdinalIgnoreCase),
                Numeric = NumericConfig.FromManager(m),
            };
        }

        private static double? ParseNullable(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? (double?)v : null;
        }
    }

    // Time-series line chart backed by LiveCharts2 / SkiaSharp. Holds an in-memory
    // ring buffer of (DateTime, double) samples; pruned by latest-sample anchored
    // window so it works for both live (latest = now) and playback (latest = cursor).
    //
    // Threshold bands are drawn as horizontal RectangularSections behind the line.
    internal sealed class LineChartRenderer
    {
        private readonly CartesianChart _chart;
        private readonly LineSeries<DateTimePoint> _series;
        private readonly List<DateTimePoint> _points = new List<DateTimePoint>(1024);
        private readonly Axis _xAxis;
        private readonly Axis _yAxis;

        private LineChartConfig _cfg;
        // Marker for the playback cursor — drawn as a thin vertical RectangularSection.
        private readonly RectangularSection _cursorSection;
        private DateTime? _cursorTime;

        public LineChartRenderer()
        {
            // DateTimePoint's built-in mapping uses DateTime.Ticks as X — pairs with
            // the X axis Labeler that converts ticks back to a HH:mm:ss string.
            _series = new LineSeries<DateTimePoint>
            {
                Values = _points,
                GeometrySize = 0,
                LineSmoothness = 0,
                Stroke = new SolidColorPaint(SKColors.LightSkyBlue) { StrokeThickness = 2 },
                Fill = null,
            };

            _xAxis = new Axis
            {
                Labeler = ticks =>
                {
                    var t = new DateTime((long)ticks, DateTimeKind.Utc).ToLocalTime();
                    return t.ToString("HH:mm:ss");
                },
                LabelsPaint = new SolidColorPaint(SKColors.Gray) { SKFontStyle = SKFontStyle.Normal },
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

            _chart = new CartesianChart
            {
                Series = new ISeries[] { _series },
                XAxes = new[] { _xAxis },
                YAxes = new[] { _yAxis },
                Sections = new RectangularSection[0],
                Background = System.Windows.Media.Brushes.Transparent,
                LegendPosition = LegendPosition.Hidden,
                TooltipPosition = TooltipPosition.Hidden,
                AnimationsSpeed = TimeSpan.FromMilliseconds(0),
                MinHeight = 80,
                MinWidth = 120,
            };
        }

        public UIElement Visual => _chart;

        public void Configure(LineChartConfig cfg)
        {
            _cfg = cfg;
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

        private void ApplyConfig()
        {
            var sk = ToSk(_cfg.LineColor);
            _series.Stroke = new SolidColorPaint(sk) { StrokeThickness = 2 };
            _series.LineSmoothness = _cfg.Smooth ? 0.65 : 0;
            _series.GeometrySize = _cfg.ShowMarker ? 4 : 0;
            _series.GeometryStroke = new SolidColorPaint(sk) { StrokeThickness = 1.5f };
            _series.GeometryFill = new SolidColorPaint(sk);
            _series.Fill = _cfg.FillEnabled
                ? new SolidColorPaint(new SKColor(sk.Red, sk.Green, sk.Blue, 0x40))
                : null;

            _yAxis.MinLimit = _cfg.YMin;
            _yAxis.MaxLimit = _cfg.YMax;

            ApplySections();
            PruneAndRescale();
        }

        private void ApplySections()
        {
            var sections = new List<RectangularSection>();

            if (_cfg != null && _cfg.Numeric != null && _cfg.Numeric.Enabled)
            {
                var n = _cfg.Numeric;
                // Threshold bands span the full X range (null = unlimited) and the
                // configured value range; only draw when both endpoints are set.
                if (n.Min.HasValue && n.Max.HasValue)
                {
                    var lo = Math.Min(n.Min.Value, n.Max.Value);
                    var hi = Math.Max(n.Min.Value, n.Max.Value);
                    var below = n.HighIsBad ? n.ColorOk : n.ColorBad;
                    var middle = n.ColorWarn;
                    var above = n.HighIsBad ? n.ColorBad : n.ColorOk;

                    if (_cfg.YMin.HasValue)
                        sections.Add(MakeBand(_cfg.YMin.Value, lo, below));
                    sections.Add(MakeBand(lo, hi, middle));
                    if (_cfg.YMax.HasValue)
                        sections.Add(MakeBand(hi, _cfg.YMax.Value, above));
                }
            }

            if (_cursorTime.HasValue)
            {
                _cursorSection.Xi = _cursorTime.Value.Ticks;
                _cursorSection.Xj = _cursorTime.Value.Ticks;
                sections.Add(_cursorSection);
            }

            _chart.Sections = sections;
        }

        private static RectangularSection MakeBand(double y1, double y2, WpfColor color)
        {
            var sk = ToSk(color);
            return new RectangularSection
            {
                Yi = Math.Min(y1, y2),
                Yj = Math.Max(y1, y2),
                Fill = new SolidColorPaint(new SKColor(sk.Red, sk.Green, sk.Blue, 0x33)),
                Stroke = null,
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

            var latest = _cursorTime ?? _points[_points.Count - 1].DateTime;
            var cutoff = latest.AddSeconds(-_cfg.WindowSeconds);

            // Drop samples older than the rolling window. List is sorted by time
            // (we enforce that on insert) so a leading-prefix RemoveRange is safe.
            int drop = 0;
            while (drop < _points.Count && _points[drop].DateTime < cutoff) drop++;
            if (drop > 0) _points.RemoveRange(0, drop);

            _xAxis.MinLimit = cutoff.Ticks;
            _xAxis.MaxLimit = latest.Ticks;

            ApplySections();
        }

        private static SKColor ToSk(WpfColor c) => new SKColor(c.R, c.G, c.B, c.A);
    }
}
