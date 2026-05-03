using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MetadataDisplay.Client.Renderers
{
    internal sealed class NumberRenderer
    {
        private readonly StackPanel _root;
        private readonly StackPanel _valueRow;
        private readonly TextBlock _valueText;
        private readonly TextBlock _unitText;
        private readonly StackPanel _chipsRow;
        private readonly Border _minChip;
        private readonly Border _maxChip;
        private readonly TextBlock _minChipText;
        private readonly TextBlock _maxChipText;

        public NumberRenderer()
        {
            _valueText = new TextBlock
            {
                Text = "—",
                Foreground = new SolidColorBrush(WidgetTheme.ValueColor),
                FontSize = WidgetTheme.FontDisplay,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
            };
            _unitText = new TextBlock
            {
                Text = "",
                Foreground = new SolidColorBrush(WidgetTheme.UnitColor),
                FontSize = WidgetTheme.FontUnitLarge,
                Margin = new Thickness(6, 0, 0, 6),
                VerticalAlignment = VerticalAlignment.Bottom,
            };
            _valueRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            _valueRow.Children.Add(_valueText);
            _valueRow.Children.Add(_unitText);

            _minChip = BuildChip(out _minChipText);
            _maxChip = BuildChip(out _maxChipText);
            _minChip.Margin = new Thickness(0, 0, 6, 0);
            _maxChip.Margin = new Thickness(6, 0, 0, 0);

            _chipsRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 8, 0, 0),
                Visibility = Visibility.Collapsed,
            };
            _chipsRow.Children.Add(_minChip);
            _chipsRow.Children.Add(_maxChip);

            _root = new StackPanel
            {
                Orientation = Orientation.Vertical,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            _root.Children.Add(_valueRow);
            _root.Children.Add(_chipsRow);
        }

        private static Border BuildChip(out TextBlock label)
        {
            label = new TextBlock
            {
                Foreground = new SolidColorBrush(WidgetTheme.SubtleColor),
                FontSize = WidgetTheme.FontMeta,
            };
            return new Border
            {
                Background = new SolidColorBrush(WidgetTheme.ChipBackground),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(8, 2, 8, 2),
                Child = label,
            };
        }

        public UIElement Visual => _root;

        public string Density { get; set; } = "Comfortable";

        public void Update(string rawValue, NumericConfig cfg)
        {
            double scale = WidgetTheme.DensityScale(Density);
            _valueText.FontSize = WidgetTheme.FontDisplay * scale;
            _unitText.FontSize = WidgetTheme.FontUnitLarge * scale;

            _unitText.Text = cfg.Unit ?? "";

            UpdateChips(cfg);

            if (rawValue == null || !double.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
            {
                _valueText.Text = "—";
                _valueText.Foreground = new SolidColorBrush(WidgetTheme.ValueColor);
                return;
            }

            _valueText.Text = FormatNumber(v);
            var color = cfg.PickColor(v);
            _valueText.Foreground = new SolidColorBrush(color);
        }

        private void UpdateChips(NumericConfig cfg)
        {
            bool hasMin = cfg?.Min != null;
            bool hasMax = cfg?.Max != null;
            _minChip.Visibility = hasMin ? Visibility.Visible : Visibility.Collapsed;
            _maxChip.Visibility = hasMax ? Visibility.Visible : Visibility.Collapsed;
            _chipsRow.Visibility = (hasMin || hasMax) ? Visibility.Visible : Visibility.Collapsed;

            if (hasMin) _minChipText.Text = "min " + FormatNumber(cfg.Min.Value);
            if (hasMax) _maxChipText.Text = "max " + FormatNumber(cfg.Max.Value);
        }

        public void Clear()
        {
            _valueText.Text = "—";
            _valueText.Foreground = new SolidColorBrush(WidgetTheme.ValueColor);
            _chipsRow.Visibility = Visibility.Collapsed;
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
