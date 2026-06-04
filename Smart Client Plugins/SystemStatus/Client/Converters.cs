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

    /// <summary>Storage usage % to a fill brush: green &lt; 90, orange &lt; 95, red &gt;= 95.</summary>
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
