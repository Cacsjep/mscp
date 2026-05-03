using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MetadataDisplay.Client.Renderers
{
    internal sealed class TextRenderer
    {
        private readonly TextBlock _text;
        private readonly Border _border;

        public TextRenderer()
        {
            _text = new TextBlock
            {
                Text = "—",
                Foreground = new SolidColorBrush(Color.FromRgb(0xF5, 0xF7, 0xF8)),
                FontSize = 28,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            _border = new Border
            {
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12, 6, 12, 6),
                Background = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)),
                Child = _text,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
        }

        public UIElement Visual => _border;

        public void Update(string value)
        {
            _text.Text = string.IsNullOrEmpty(value) ? "—" : value;
        }

        public void Clear() => _text.Text = "—";
    }
}
