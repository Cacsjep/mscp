using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace MetadataDisplay.Client.Renderers
{
    internal enum GaugeStyle { Arc180, Arc270, Bar, Modern180, Modern270 }

    internal sealed class GaugeConfig
    {
        public double RangeMin;
        public double RangeMax;
        public GaugeStyle Style;
        public bool ShowValue;
        public double ValueFontSize;
        public NumericConfig Numeric; // reuses Min/Max thresholds + colors + unit

        public static GaugeConfig FromManager(MetadataDisplayViewItemManager m)
        {
            var rmin = ParseDouble(m.GaugeRangeMin, 0);
            var rmax = ParseDouble(m.GaugeRangeMax, 100);
            if (rmax <= rmin) rmax = rmin + 1;

            var style = GaugeStyle.Arc180;
            if (string.Equals(m.GaugeStyle, "Arc270", StringComparison.OrdinalIgnoreCase)) style = GaugeStyle.Arc270;
            else if (string.Equals(m.GaugeStyle, "Bar", StringComparison.OrdinalIgnoreCase)) style = GaugeStyle.Bar;
            else if (string.Equals(m.GaugeStyle, "Modern180", StringComparison.OrdinalIgnoreCase)) style = GaugeStyle.Modern180;
            else if (string.Equals(m.GaugeStyle, "Modern270", StringComparison.OrdinalIgnoreCase)) style = GaugeStyle.Modern270;

            return new GaugeConfig
            {
                RangeMin = rmin,
                RangeMax = rmax,
                Style = style,
                ShowValue = !string.Equals(m.GaugeShowValue, "false", StringComparison.OrdinalIgnoreCase),
                ValueFontSize = ParseDouble(m.GaugeValueFontSize, 34),
                Numeric = NumericConfig.FromManager(m),
            };
        }

        private static double ParseDouble(string s, double fallback)
        {
            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) return v;
            return fallback;
        }
    }

    internal sealed class GaugeRenderer
    {
        private readonly Grid _root;
        private readonly Canvas _canvas;
        private readonly TextBlock _valueText;
        private readonly TextBlock _unitText;
        private readonly StackPanel _labelStack;

        // Arc geometry constants (logical canvas size 320x200; outer Viewbox scales).
        // The arc is anchored near the top of the canvas so we don't waste vertical
        // pixels — the value/unit labels sit in the lower portion below the hub.
        private const double LogicalW = 320;
        private const double LogicalH = 200;

        public GaugeRenderer()
        {
            _canvas = new Canvas
            {
                Width = LogicalW,
                Height = LogicalH,
                Background = Brushes.Transparent,
                ClipToBounds = false,
            };
            _valueText = new TextBlock
            {
                Text = "—",
                Foreground = new SolidColorBrush(Color.FromRgb(0xF5, 0xF7, 0xF8)),
                FontSize = 34,
                FontWeight = FontWeights.SemiBold,
                TextAlignment = TextAlignment.Center,
            };
            _unitText = new TextBlock
            {
                Text = "",
                Foreground = new SolidColorBrush(Color.FromRgb(0xCF, 0xD7, 0xDA)),
                FontSize = 14,
                TextAlignment = TextAlignment.Center,
            };
            _labelStack = new StackPanel { Orientation = Orientation.Vertical, Width = LogicalW };
            _valueText.HorizontalAlignment = HorizontalAlignment.Center;
            _unitText.HorizontalAlignment = HorizontalAlignment.Center;
            _labelStack.Children.Add(_valueText);
            _labelStack.Children.Add(_unitText);
            _canvas.Children.Add(_labelStack);

            _root = new Grid();
            _root.Children.Add(_canvas);
        }

        public UIElement Visual => _root;

        public void Update(string rawValue, GaugeConfig cfg)
        {
            // Clear all canvas children except the persistent label stack.
            for (int i = _canvas.Children.Count - 1; i >= 0; i--)
            {
                if (!ReferenceEquals(_canvas.Children[i], _labelStack))
                    _canvas.Children.RemoveAt(i);
            }
            _unitText.Text = cfg.Numeric.Unit ?? "";
            _valueText.FontSize = cfg.ValueFontSize > 0 ? cfg.ValueFontSize : 26;
            _labelStack.Visibility = cfg.ShowValue ? Visibility.Visible : Visibility.Collapsed;

            double? v = ParseValue(rawValue);
            _valueText.Text = v.HasValue ? FormatNumber(v.Value) : (rawValue ?? "—");

            switch (cfg.Style)
            {
                case GaugeStyle.Bar:
                    DrawBar(v, cfg);
                    Canvas.SetLeft(_labelStack, 0);
                    Canvas.SetTop(_labelStack, 8);
                    break;
                case GaugeStyle.Arc270:
                    DrawArc(v, cfg, sweepDegrees: 270);
                    Canvas.SetLeft(_labelStack, 0);
                    Canvas.SetTop(_labelStack, 130);
                    break;
                case GaugeStyle.Modern270:
                    DrawModernArc(v, cfg, sweepDegrees: 270);
                    Canvas.SetLeft(_labelStack, 0);
                    Canvas.SetTop(_labelStack, 75);
                    break;
                case GaugeStyle.Modern180:
                    DrawModernArc(v, cfg, sweepDegrees: 180);
                    Canvas.SetLeft(_labelStack, 0);
                    Canvas.SetTop(_labelStack, 100);
                    break;
                case GaugeStyle.Arc180:
                default:
                    DrawArc(v, cfg, sweepDegrees: 180);
                    Canvas.SetLeft(_labelStack, 0);
                    Canvas.SetTop(_labelStack, 130);
                    break;
            }
        }

        public void Clear()
        {
            for (int i = _canvas.Children.Count - 1; i >= 0; i--)
            {
                if (!ReferenceEquals(_canvas.Children[i], _labelStack))
                    _canvas.Children.RemoveAt(i);
            }
            _valueText.Text = "—";
            _unitText.Text = "";
        }

        // ───────── Arc drawing ─────────

        private void DrawArc(double? value, GaugeConfig cfg, double sweepDegrees)
        {
            // Centered arc in the upper portion of the canvas.
            // Arc180 sweeps from 180° (left) to 360° (right) — top half.
            // Arc270 sweeps from 135° to 405° — leaves a 90° opening at the bottom.
            double cx = LogicalW / 2.0;
            double cy = sweepDegrees >= 270 ? 95 : 110;
            double radius = sweepDegrees >= 270 ? 70 : 88;
            double thickness = 16;

            double startAngle = sweepDegrees >= 270 ? 135 : 180;
            double endAngle = startAngle + sweepDegrees;

            // Determine threshold breakpoints in the value domain.
            double rmin = cfg.RangeMin;
            double rmax = cfg.RangeMax;
            double tMin = cfg.Numeric.Min ?? rmin;
            double tMax = cfg.Numeric.Max ?? rmax;
            tMin = Clamp(tMin, rmin, rmax);
            tMax = Clamp(tMax, rmin, rmax);
            if (tMax < tMin) tMax = tMin;

            Color cOk = cfg.Numeric.ColorOk;
            Color cWarn = cfg.Numeric.ColorWarn;
            Color cBad = cfg.Numeric.ColorBad;

            // Three bands per direction. With HighIsBad: [rmin..tMin]=Ok, [tMin..tMax]=Warn, [tMax..rmax]=Bad.
            // With LowIsBad: [rmin..tMin]=Bad, [tMin..tMax]=Warn, [tMax..rmax]=Ok.
            Color band1 = cfg.Numeric.HighIsBad ? cOk : cBad;
            Color band2 = cWarn;
            Color band3 = cfg.Numeric.HighIsBad ? cBad : cOk;

            DrawArcSegment(cx, cy, radius, thickness, ValueToAngle(rmin, rmin, rmax, startAngle, endAngle), ValueToAngle(tMin, rmin, rmax, startAngle, endAngle), band1);
            DrawArcSegment(cx, cy, radius, thickness, ValueToAngle(tMin, rmin, rmax, startAngle, endAngle), ValueToAngle(tMax, rmin, rmax, startAngle, endAngle), band2);
            DrawArcSegment(cx, cy, radius, thickness, ValueToAngle(tMax, rmin, rmax, startAngle, endAngle), ValueToAngle(rmax, rmin, rmax, startAngle, endAngle), band3);

            DrawScaleLabel(cx, cy, radius + thickness, startAngle, FormatNumber(rmin));
            DrawScaleLabel(cx, cy, radius + thickness, endAngle, FormatNumber(rmax));

            // Needle + hub
            if (value.HasValue)
            {
                var v = Clamp(value.Value, rmin, rmax);
                var ang = ValueToAngle(v, rmin, rmax, startAngle, endAngle);
                var rad = ang * Math.PI / 180.0;
                var tipX = cx + Math.Cos(rad) * (radius - 4);
                var tipY = cy + Math.Sin(rad) * (radius - 4);

                var needleColor = cfg.Numeric.PickColor(v);
                var needle = new Line
                {
                    X1 = cx, Y1 = cy, X2 = tipX, Y2 = tipY,
                    Stroke = new SolidColorBrush(needleColor),
                    StrokeThickness = 3,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round,
                };
                _canvas.Children.Add(needle);

                var hub = new Ellipse
                {
                    Width = 12, Height = 12,
                    Fill = new SolidColorBrush(needleColor),
                    Stroke = new SolidColorBrush(Color.FromRgb(0x1C, 0x23, 0x26)),
                    StrokeThickness = 2,
                };
                Canvas.SetLeft(hub, cx - 6);
                Canvas.SetTop(hub, cy - 6);
                _canvas.Children.Add(hub);
            }
        }

        // Modern progress-arc style: a thin gray background track plus a thicker
        // progress arc that fills from the start angle to the current value's angle.
        // The progress color picks from the threshold (Ok/Warn/Bad) so a single
        // glance still tells the operator how healthy the value is.
        private void DrawModernArc(double? value, GaugeConfig cfg, double sweepDegrees)
        {
            double cx = LogicalW / 2.0;
            double cy = sweepDegrees >= 270 ? 105 : 120;
            double radius = sweepDegrees >= 270 ? 78 : 92;
            double trackThickness = 14;
            double progressThickness = 14;

            double startAngle = sweepDegrees >= 270 ? 135 : 180;
            double endAngle = startAngle + sweepDegrees;

            double rmin = cfg.RangeMin;
            double rmax = cfg.RangeMax;

            // Background track — full sweep, dim gray.
            DrawArcSegment(cx, cy, radius, trackThickness,
                startAngle, endAngle,
                Color.FromRgb(0x33, 0x3B, 0x40), rounded: true);

            // Progress fill — startAngle to value angle.
            if (value.HasValue)
            {
                var v = Clamp(value.Value, rmin, rmax);
                var ang = ValueToAngle(v, rmin, rmax, startAngle, endAngle);
                if (Math.Abs(ang - startAngle) > 0.01)
                {
                    var progressColor = cfg.Numeric.PickColor(v);
                    DrawArcSegment(cx, cy, radius, progressThickness,
                        startAngle, ang, progressColor, rounded: true);
                }
            }

            // Tick labels at min/max — small + subtle.
            DrawScaleLabel(cx, cy, radius + progressThickness + 2, startAngle, FormatNumber(rmin));
            DrawScaleLabel(cx, cy, radius + progressThickness + 2, endAngle, FormatNumber(rmax));
        }

        private void DrawArcSegment(double cx, double cy, double r, double thickness, double a1, double a2, Color color)
            => DrawArcSegment(cx, cy, r, thickness, a1, a2, color, rounded: false);

        private void DrawArcSegment(double cx, double cy, double r, double thickness, double a1, double a2, Color color, bool rounded)
        {
            if (Math.Abs(a2 - a1) < 0.01) return;
            var rad1 = a1 * Math.PI / 180.0;
            var rad2 = a2 * Math.PI / 180.0;
            var p1 = new Point(cx + Math.Cos(rad1) * r, cy + Math.Sin(rad1) * r);
            var p2 = new Point(cx + Math.Cos(rad2) * r, cy + Math.Sin(rad2) * r);

            var fig = new PathFigure { StartPoint = p1, IsClosed = false };
            fig.Segments.Add(new ArcSegment
            {
                Point = p2,
                Size = new Size(r, r),
                IsLargeArc = Math.Abs(a2 - a1) > 180,
                SweepDirection = a2 > a1 ? SweepDirection.Clockwise : SweepDirection.Counterclockwise,
            });
            var pg = new PathGeometry();
            pg.Figures.Add(fig);

            var path = new Path
            {
                Data = pg,
                Stroke = new SolidColorBrush(color),
                StrokeThickness = thickness,
                StrokeStartLineCap = rounded ? PenLineCap.Round : PenLineCap.Flat,
                StrokeEndLineCap = rounded ? PenLineCap.Round : PenLineCap.Flat,
            };
            _canvas.Children.Add(path);
        }

        private void DrawScaleLabel(double cx, double cy, double r, double angleDeg, string text)
        {
            var rad = angleDeg * Math.PI / 180.0;
            var x = cx + Math.Cos(rad) * r;
            var y = cy + Math.Sin(rad) * r;
            var tb = new TextBlock
            {
                Text = text,
                Foreground = new SolidColorBrush(Color.FromRgb(0xA9, 0xB5, 0xBB)),
                FontSize = 11,
            };
            // Approximate centering — not measuring, fine for 1-3 char numbers.
            Canvas.SetLeft(_canvas, 0);
            tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(tb, x - tb.DesiredSize.Width / 2);
            Canvas.SetTop(tb, y - tb.DesiredSize.Height / 2);
            _canvas.Children.Add(tb);
        }

        private static double ValueToAngle(double value, double rmin, double rmax, double aStart, double aEnd)
        {
            if (rmax <= rmin) return aStart;
            var f = (value - rmin) / (rmax - rmin);
            f = Clamp(f, 0, 1);
            return aStart + (aEnd - aStart) * f;
        }

        // ───────── Bar drawing ─────────

        private void DrawBar(double? value, GaugeConfig cfg)
        {
            // Bar in lower half of canvas; value/unit labels sit at the top (y=8).
            double left = 20, top = 90, width = LogicalW - 40, height = 36;

            double rmin = cfg.RangeMin;
            double rmax = cfg.RangeMax;
            double tMin = cfg.Numeric.Min ?? rmin;
            double tMax = cfg.Numeric.Max ?? rmax;
            tMin = Clamp(tMin, rmin, rmax);
            tMax = Clamp(tMax, rmin, rmax);
            if (tMax < tMin) tMax = tMin;

            Color cOk = cfg.Numeric.ColorOk;
            Color cWarn = cfg.Numeric.ColorWarn;
            Color cBad = cfg.Numeric.ColorBad;
            Color band1 = cfg.Numeric.HighIsBad ? cOk : cBad;
            Color band2 = cWarn;
            Color band3 = cfg.Numeric.HighIsBad ? cBad : cOk;

            DrawBarBand(left, top, ValueToX(rmin, rmin, rmax, left, width), ValueToX(tMin, rmin, rmax, left, width), height, band1);
            DrawBarBand(left, top, ValueToX(tMin, rmin, rmax, left, width), ValueToX(tMax, rmin, rmax, left, width), height, band2);
            DrawBarBand(left, top, ValueToX(tMax, rmin, rmax, left, width), ValueToX(rmax, rmin, rmax, left, width), height, band3);

            DrawBarScaleLabel(left, top + height + 4, FormatNumber(rmin), TextAlignment.Left);
            DrawBarScaleLabel(left + width, top + height + 4, FormatNumber(rmax), TextAlignment.Right);

            // Outer outline
            var outline = new System.Windows.Shapes.Rectangle
            {
                Width = width,
                Height = height,
                Stroke = new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF)),
                StrokeThickness = 1,
                Fill = Brushes.Transparent,
            };
            Canvas.SetLeft(outline, left);
            Canvas.SetTop(outline, top);
            _canvas.Children.Add(outline);

            if (value.HasValue)
            {
                var v = Clamp(value.Value, rmin, rmax);
                var x = ValueToX(v, rmin, rmax, left, width);
                var marker = new System.Windows.Shapes.Rectangle
                {
                    Width = 4,
                    Height = height + 12,
                    Fill = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF)),
                };
                Canvas.SetLeft(marker, x - 2);
                Canvas.SetTop(marker, top - 6);
                _canvas.Children.Add(marker);
            }
        }

        private void DrawBarBand(double clipLeft, double top, double x1, double x2, double height, Color color)
        {
            if (x2 <= x1) return;
            var rect = new System.Windows.Shapes.Rectangle
            {
                Width = x2 - x1,
                Height = height,
                Fill = new SolidColorBrush(color),
            };
            Canvas.SetLeft(rect, x1);
            Canvas.SetTop(rect, top);
            _canvas.Children.Add(rect);
        }

        private void DrawBarScaleLabel(double anchorX, double y, string text, TextAlignment align)
        {
            var tb = new TextBlock
            {
                Text = text,
                Foreground = new SolidColorBrush(Color.FromRgb(0xA9, 0xB5, 0xBB)),
                FontSize = 11,
            };
            tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double left = anchorX;
            if (align == TextAlignment.Right) left = anchorX - tb.DesiredSize.Width;
            else if (align == TextAlignment.Center) left = anchorX - tb.DesiredSize.Width / 2;
            Canvas.SetLeft(tb, left);
            Canvas.SetTop(tb, y);
            _canvas.Children.Add(tb);
        }

        private static double ValueToX(double value, double rmin, double rmax, double left, double width)
        {
            if (rmax <= rmin) return left;
            var f = (value - rmin) / (rmax - rmin);
            f = Clamp(f, 0, 1);
            return left + width * f;
        }

        // ───────── Helpers ─────────

        private static double? ParseValue(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? (double?)v : null;
        }

        private static string FormatNumber(double v)
        {
            if (v == (long)v) return ((long)v).ToString(CultureInfo.InvariantCulture);
            return v.ToString("0.##", CultureInfo.InvariantCulture);
        }

        private static double Clamp(double v, double min, double max)
        {
            if (v < min) return min;
            if (v > max) return max;
            return v;
        }
    }
}
