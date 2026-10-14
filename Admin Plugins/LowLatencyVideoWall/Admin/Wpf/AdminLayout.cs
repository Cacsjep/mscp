using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace LowLatencyVideoWall.Admin.Wpf
{
    /// <summary>
    /// The page furniture the four admin pages share: the white card every section sits in,
    /// its small grey section heading, a labelled field and the borderless text box that goes
    /// inside an <see cref="AdminTheme.IconInputBox"/>.
    ///
    /// These are the same shapes the Race Commander console builds inline. They live here
    /// rather than in AdminTheme so the theme file stays a straight copy of the shared one.
    /// </summary>
    internal static class AdminLayout
    {
        private static readonly Brush CardBorder = Freeze("#E3E6EA");

        private static Brush Freeze(string hex)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            brush.Freeze();
            return brush;
        }

        /// <summary>One white rounded card with the house border and margin.</summary>
        public static Border Card(FrameworkElement content, double padding)
        {
            return new Border
            {
                Background = Brushes.White,
                BorderBrush = CardBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Margin = new Thickness(10, 10, 10, 0),
                Padding = new Thickness(padding),
                Child = content
            };
        }

        public static TextBlock SectionHeader(ResourceDictionary res, string text)
            => new TextBlock { Text = text, Style = (Style)res["SectionHeaderStyle"] };

        public static TextBlock PageTitle(string text)
            => new TextBlock
            {
                Text = text,
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                Foreground = AdminTheme.Text,
                VerticalAlignment = VerticalAlignment.Center
            };

        /// <summary>A text box with no chrome of its own, for hosting inside an icon input box.</summary>
        public static TextBox BareTextBox(string initial = "")
            => new TextBox
            {
                Text = initial,
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                VerticalContentAlignment = VerticalAlignment.Center,
                Padding = new Thickness(0),
                FontSize = 13
            };

        /// <summary>A field with its caption above it.</summary>
        public static FrameworkElement Labeled(ResourceDictionary res, string caption, FrameworkElement field)
        {
            var stack = new StackPanel { Margin = new Thickness(0, 0, 14, 0) };
            stack.Children.Add(new TextBlock
            {
                Text = caption,
                Style = (Style)res["FieldLabelStyle"],
                Margin = new Thickness(1, 0, 0, 4)
            });
            stack.Children.Add(field);
            return stack;
        }

        /// <summary>The small state dot used in a header or a grid cell.</summary>
        public static Ellipse Dot(Brush fill, double size = 9)
            => new Ellipse
            {
                Width = size,
                Height = size,
                Fill = fill,
                VerticalAlignment = VerticalAlignment.Center
            };
    }
}
