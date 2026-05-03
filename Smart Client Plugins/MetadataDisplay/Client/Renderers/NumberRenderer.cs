using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MetadataDisplay.Client.Renderers
{
    internal sealed class NumberRenderer
    {
        private readonly Border _border;
        private readonly TextBlock _valueText;
        private readonly TextBlock _unitText;
        private readonly StackPanel _row;

        public NumberRenderer()
        {
            _valueText = new TextBlock
            {
                Text = "—",
                Foreground = new SolidColorBrush(Color.FromRgb(0xF5, 0xF7, 0xF8)),
                FontSize = 64,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
            };
            _unitText = new TextBlock
            {
                Text = "",
                Foreground = new SolidColorBrush(Color.FromRgb(0xCF, 0xD7, 0xDA)),
                FontSize = 22,
                Margin = new Thickness(6, 0, 0, 6),
                VerticalAlignment = VerticalAlignment.Bottom,
            };
            _row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            _row.Children.Add(_valueText);
            _row.Children.Add(_unitText);

            _border = new Border
            {
                CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(2),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF)),
                Padding = new Thickness(16, 8, 16, 8),
                Background = new SolidColorBrush(Color.FromArgb(0x33, 0x00, 0x00, 0x00)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Child = _row,
            };
        }

        public UIElement Visual => _border;

        public void Update(string rawValue, NumericConfig cfg)
        {
            _unitText.Text = cfg.Unit ?? "";
            if (rawValue == null || !double.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
            {
                _valueText.Text = "—";
                _border.BorderBrush = new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF));
                return;
            }

            _valueText.Text = FormatNumber(v);
            var color = cfg.PickColor(v);
            _border.BorderBrush = new SolidColorBrush(color);
        }

        public void Clear()
        {
            _valueText.Text = "—";
            _border.BorderBrush = new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF));
        }

        private static string FormatNumber(double v)
        {
            if (v == (long)v) return ((long)v).ToString(CultureInfo.InvariantCulture);
            return v.ToString("0.##", CultureInfo.InvariantCulture);
        }
    }

    internal sealed class NumericConfig
    {
        public double? Min;
        public double? Max;
        public bool HighIsBad;     // true → high values trip red
        public Color ColorOk;
        public Color ColorWarn;
        public Color ColorBad;
        public string Unit;

        public static NumericConfig FromManager(MetadataDisplayViewItemManager m)
        {
            return new NumericConfig
            {
                Min = ParseNullable(m.NumMin),
                Max = ParseNullable(m.NumMax),
                HighIsBad = !string.Equals(m.NumDirection, "LowIsBad", System.StringComparison.OrdinalIgnoreCase),
                ColorOk = ColorUtil.Parse(m.ColorOk, Color.FromRgb(0x3C, 0xB3, 0x71)),
                ColorWarn = ColorUtil.Parse(m.ColorWarn, Color.FromRgb(0xE6, 0x95, 0x00)),
                ColorBad = ColorUtil.Parse(m.ColorBad, Color.FromRgb(0xD8, 0x39, 0x2C)),
                Unit = m.Unit ?? "",
            };
        }

        public Color PickColor(double v)
        {
            // Conventions:
            //   HighIsBad: <Min → Ok (green), <Max → Warn (orange), else Bad (red)
            //   LowIsBad : >Max → Ok (green), >Min → Warn (orange), else Bad (red)
            if (HighIsBad)
            {
                if (Min.HasValue && v < Min.Value) return ColorOk;
                if (Max.HasValue && v < Max.Value) return ColorWarn;
                if (Max.HasValue) return ColorBad;
                if (Min.HasValue) return ColorWarn;
                return ColorOk;
            }
            else
            {
                if (Max.HasValue && v > Max.Value) return ColorOk;
                if (Min.HasValue && v > Min.Value) return ColorWarn;
                if (Min.HasValue) return ColorBad;
                if (Max.HasValue) return ColorWarn;
                return ColorOk;
            }
        }

        private static double? ParseNullable(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? (double?)v : null;
        }
    }
}
