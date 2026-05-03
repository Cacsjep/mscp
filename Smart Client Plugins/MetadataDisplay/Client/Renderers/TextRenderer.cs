using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MetadataDisplay.Client.Renderers
{
    internal sealed class TextRenderer
    {
        private readonly TextBlock _text;

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
        }

        public UIElement Visual => _text;

        public double FontSize
        {
            get => _text.FontSize;
            set { if (value > 0) _text.FontSize = value; }
        }

        public void Update(string value)
        {
            _text.Text = string.IsNullOrEmpty(value) ? "—" : value;
        }

        public void Clear() => _text.Text = "—";
    }
}
