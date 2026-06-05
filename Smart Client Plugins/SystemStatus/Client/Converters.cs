using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace SystemStatus.Client
{
    /// <summary>Maps a bool to one of two brushes (e.g. online dot green/red).</summary>
    public sealed class BoolToBrushConverter : IValueConverter
    {
        public Brush TrueBrush { get; set; } = Brushes.LimeGreen;
        public Brush FalseBrush { get; set; } = Brushes.IndianRed;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => (value is bool b && b) ? TrueBrush : FalseBrush;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// Camera connectivity to a status-dot brush:
    /// "Online" -> green, "Offline" -> red, anything else (recorder unreachable) -> gray.
    /// </summary>
    public sealed class ConnectivityToBrushConverter : IValueConverter
    {
        private static readonly Brush Green = Freeze(Color.FromRgb(0x3F, 0xB9, 0x50));
        private static readonly Brush Red = Freeze(Color.FromRgb(0xE0, 0x45, 0x45));
        private static readonly Brush Gray = Freeze(Color.FromRgb(0x77, 0x77, 0x77));

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            switch (value as string)
            {
                case "Online": return Green;
                case "Offline": return Red;
                default: return Gray;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();

        private static Brush Freeze(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
    }

    public sealed class PercentToBrushConverter : IValueConverter
    {
        private static readonly Brush Green = new SolidColorBrush(Color.FromRgb(0x3F, 0xB9, 0x50));
        private static readonly Brush Orange = new SolidColorBrush(Color.FromRgb(0xE0, 0xA2, 0x3F));
        private static readonly Brush Red = new SolidColorBrush(Color.FromRgb(0xE0, 0x45, 0x45));

        static PercentToBrushConverter()
        {
            Green.Freeze(); Orange.Freeze(); Red.Freeze();
        }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            double pct = value is double d ? d : 0;
            if (pct >= 95) return Red;
            if (pct >= 90) return Orange;
            return Green;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// Styles the first/last-recording timestamp "chips" (Vuetify label style). Given the cell's
    /// display text, returns a brush per role (ConverterParameter): "fill" / "border" / "fg". For a
    /// real timestamp the chip is a tinted accent label; for a placeholder ("-", "…", "error",
    /// "(none)", empty) the fill/border are transparent and the text is dimmed, so empty cells read
    /// as plain text rather than empty chips.
    /// </summary>
    public sealed class TimestampChipConverter : IValueConverter
    {
        private static readonly Brush Fill = Freeze(Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF));   // neutral white @ ~10%
        private static readonly Brush Border = Freeze(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)); // neutral white @ ~20%
        private static readonly Brush Text = Freeze(Color.FromRgb(0xE6, 0xE6, 0xE6));          // ScText
        private static readonly Brush Dim = Freeze(Color.FromRgb(0x8C, 0x8C, 0x8C));           // ScSubtle
        private static readonly Brush Clear = Freeze(Colors.Transparent);

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var s = (value as string)?.Trim();
            bool real = !string.IsNullOrEmpty(s) && s != "-" && s != "…" && s != "error" && s != "(none)";
            switch (parameter as string)
            {
                case "fg":     return real ? Text : Dim;
                case "border": return real ? Border : Clear;
                default:       return real ? Fill : Clear;   // "fill"
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();

        private static Brush Freeze(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
    }

    /// <summary>Maps a bool to Visibility; <see cref="Invert"/> shows when the value is false.</summary>
    public sealed class BoolToVisibilityConverter : IValueConverter
    {
        public bool Invert { get; set; }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool b = value is bool v && v;
            if (Invert) b = !b;
            return b ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
