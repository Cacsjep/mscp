using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ColoredTimeline.Background
{
    // Hover-preview popup for a marker. Smart Client invokes
    // TimelineSequenceSource.GetPreviewWpfUserControl(object dataId) when the user hovers
    // a marker; we look up the dataId in the source's dictionary and pass the resulting
    // info to this control. No XAML - kept code-only so the csproj stays clean.
    internal class MarkerPreviewControl : UserControl
    {
        public MarkerPreviewControl(MarkerInfo info)
        {
            if (info == null) info = new MarkerInfo();

            var border = new Border
            {
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(10),
                Background = new SolidColorBrush(Color.FromArgb(0xF0, 0x2B, 0x2B, 0x2B)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0xFF, 0x55, 0x55, 0x55)),
                BorderThickness = new Thickness(1)
            };
            var stack = new StackPanel { Orientation = Orientation.Vertical };

            stack.Children.Add(MakeTitle(info.RuleName ?? "(rule)", info.AccentColor));
            stack.Children.Add(MakeKindBadge(info.Kind));
            stack.Children.Add(MakeRow("Event:", string.IsNullOrEmpty(info.EventDisplayName) ? info.EventName : info.EventDisplayName));
            if (!string.IsNullOrEmpty(info.CameraName))
                stack.Children.Add(MakeRow("Camera:", info.CameraName));
            stack.Children.Add(MakeRow("Time:", info.Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.CurrentCulture)));

            border.Child = stack;
            Content = border;
            MinWidth = 260;
            Foreground = Brushes.White;
        }

        private static TextBlock MakeTitle(string text, Color accent)
        {
            return new TextBlock
            {
                Text = text,
                FontWeight = FontWeights.Bold,
                FontSize = 14,
                Foreground = new SolidColorBrush(accent),
                Margin = new Thickness(0, 0, 0, 4)
            };
        }

        private static UIElement MakeKindBadge(MarkerKind kind)
        {
            var border = new Border
            {
                CornerRadius = new CornerRadius(2),
                Padding = new Thickness(6, 1, 6, 1),
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 0, 6),
                Background = new SolidColorBrush(kind == MarkerKind.Start
                    ? Color.FromRgb(0x2E, 0x7D, 0x32)   // green-ish
                    : Color.FromRgb(0xC6, 0x28, 0x28))  // red-ish
            };
            border.Child = new TextBlock
            {
                Text = kind == MarkerKind.Start ? "START" : "STOP",
                FontWeight = FontWeights.SemiBold,
                FontSize = 10,
                Foreground = Brushes.White
            };
            return border;
        }

        private static UIElement MakeRow(string label, string value)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 1, 0, 1) };
            sp.Children.Add(new TextBlock
            {
                Text = label,
                Width = 60,
                Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0xAA, 0xAA, 0xAA))
            });
            sp.Children.Add(new TextBlock
            {
                Text = value ?? "",
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 320
            });
            return sp;
        }
    }

    public enum MarkerKind { Start, Stop }

    public class MarkerInfo
    {
        public string RuleName;
        public MarkerKind Kind;
        public string EventName;          // raw EventLog message
        public string EventDisplayName;   // friendly form
        public string CameraName;
        public DateTime Timestamp;        // UTC
        public Color AccentColor = Color.FromRgb(0x1E, 0x88, 0xE5);
    }
}
