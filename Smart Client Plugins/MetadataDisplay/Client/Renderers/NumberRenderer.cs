using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using FontAwesome5;

namespace MetadataDisplay.Client.Renderers
{
    internal sealed class NumberRenderer
    {
        private readonly StackPanel _root;
        private readonly TextBlock _valueRow;
        private readonly Run _valueRun;
        private readonly Run _unitRun;
        private readonly StackPanel _chipsRow;
        private readonly Border _minChip;
        private readonly Border _maxChip;
        private readonly TextBlock _minChipText;
        private readonly TextBlock _maxChipText;
        private readonly ImageAwesome _minChipIcon;
        private readonly ImageAwesome _maxChipIcon;

        public NumberRenderer()
        {
            // Both value and unit live as Runs inside a single TextBlock so they
            // share one true text baseline. StackPanel + VerticalAlignment doesn't
            // do baseline alignment — the smaller font ends up on the bounding-box
            // bottom, which sits below the larger font's baseline.
            _valueRun = new Run
            {
                Text = "—",
                Foreground = new SolidColorBrush(WidgetTheme.ValueColor),
                FontSize = WidgetTheme.FontDisplay,
                FontWeight = FontWeights.SemiBold,
            };
            _unitRun = new Run
            {
                Text = "",
                Foreground = new SolidColorBrush(WidgetTheme.UnitColor),
                FontSize = WidgetTheme.FontUnitLarge,
            };
            _valueRow = new TextBlock
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
            };
            _valueRow.Inlines.Add(_valueRun);
            _valueRow.Inlines.Add(new Run(" ") { FontSize = WidgetTheme.FontUnitLarge }); // space at unit size
            _valueRow.Inlines.Add(_unitRun);

            // Down-arrow for min, up-arrow for max — matches the colored-pill style
            // the user asked for (icon + label, outlined with the threshold color).
            _minChip = BuildChip(EFontAwesomeIcon.Solid_ArrowDown, out _minChipText, out _minChipIcon);
            _maxChip = BuildChip(EFontAwesomeIcon.Solid_ArrowUp,   out _maxChipText, out _maxChipIcon);
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

        private static Border BuildChip(EFontAwesomeIcon icon, out TextBlock label, out ImageAwesome iconImg)
        {
            iconImg = new ImageAwesome
            {
                Icon = icon,
                Width = 10,
                Height = 10,
                Margin = new Thickness(0, 0, 5, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            label = new TextBlock
            {
                FontSize = WidgetTheme.FontMeta,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(iconImg);
            row.Children.Add(label);
            // Pill-style outlined chip — color (border + icon + text) is set per-chip
            // by ApplyChipColors so it picks up the threshold palette.
            return new Border
            {
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(1.2),
                CornerRadius = new CornerRadius(11),
                Padding = new Thickness(10, 3, 10, 3),
                Child = row,
            };
        }

        private void ApplyChipColors(NumericConfig cfg)
        {
            // With HighIsBad: min boundary = Ok side (green), max boundary = Bad side (red).
            // With LowIsBad : min boundary = Bad side (red),   max boundary = Ok side (green).
            Color minColor = cfg.HighIsBad ? cfg.ColorOk : cfg.ColorBad;
            Color maxColor = cfg.HighIsBad ? cfg.ColorBad : cfg.ColorOk;
            ApplyChipPalette(_minChip, _minChipText, _minChipIcon, minColor);
            ApplyChipPalette(_maxChip, _maxChipText, _maxChipIcon, maxColor);
        }

        private static void ApplyChipPalette(Border chip, TextBlock label, ImageAwesome icon, Color color)
        {
            var fg = new SolidColorBrush(color);
            chip.BorderBrush = fg;
            label.Foreground = fg;
            icon.Foreground = fg;
        }

        public UIElement Visual => _root;

        public string Density { get; set; } = "Comfortable";

        public void Update(string rawValue, NumericConfig cfg)
        {
            double scale = WidgetTheme.DensityScale(Density);
            _valueRun.FontSize = WidgetTheme.FontDisplay * scale;
            _unitRun.FontSize = WidgetTheme.FontUnitLarge * scale;

            _unitRun.Text = cfg.Unit ?? "";

            UpdateChips(cfg);

            if (rawValue == null || !double.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
            {
                _valueRun.Text = "—";
                _valueRun.Foreground = new SolidColorBrush(WidgetTheme.ValueColor);
                return;
            }

            _valueRun.Text = FormatNumber(v);
            var color = cfg.PickColor(v);
            _valueRun.Foreground = new SolidColorBrush(color);
        }

        private void UpdateChips(NumericConfig cfg)
        {
            bool enabled = cfg?.Enabled == true;
            bool hasMin = enabled && cfg.Min != null;
            bool hasMax = enabled && cfg.Max != null;
            _minChip.Visibility = hasMin ? Visibility.Visible : Visibility.Collapsed;
            _maxChip.Visibility = hasMax ? Visibility.Visible : Visibility.Collapsed;
            _chipsRow.Visibility = (hasMin || hasMax) ? Visibility.Visible : Visibility.Collapsed;

            // Icon already says "min/max" — just print the value.
            if (hasMin) _minChipText.Text = FormatNumber(cfg.Min.Value);
            if (hasMax) _maxChipText.Text = FormatNumber(cfg.Max.Value);
            if (enabled) ApplyChipColors(cfg);
        }

        public void Clear()
        {
            _valueRun.Text = "—";
            _valueRun.Foreground = new SolidColorBrush(WidgetTheme.ValueColor);
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
        public bool Enabled;       // master switch — when false, render neutral
        public double? Min;
        public double? Max;
        public bool HighIsBad;     // true → high values trip red
        public Color ColorOk;
        public Color ColorWarn;
        public Color ColorBad;
        public string Unit;

        // Neutral fill used everywhere a renderer wants "no threshold opinion" —
        // the standard widget value color so disabled-thresholds doesn't reintroduce
        // a hidden green/red default.
        public static readonly Color NeutralColor = WidgetTheme.ValueColor;

        public static NumericConfig FromManager(MetadataDisplayViewItemManager m)
        {
            return new NumericConfig
            {
                Enabled = string.Equals(m.ThresholdsEnabled, "true", System.StringComparison.OrdinalIgnoreCase),
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
            if (!Enabled) return NeutralColor;
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
