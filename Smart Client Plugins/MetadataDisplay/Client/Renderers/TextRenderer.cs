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
                Foreground = new SolidColorBrush(WidgetTheme.ValueColor),
                FontSize = WidgetTheme.FontText,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
        }

        public UIElement Visual => _text;

        // User-configured size — taken as-is, density doesn't override.
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
