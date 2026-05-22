using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MetadataDisplay.Client.Renderers
{
    // Renders a base64-encoded image carried inside a metadata value.
    // Empty / null value -> "No Image" placeholder.
    // Decode failure -> error label in the Bad color so the operator can see
    // the payload isn't a usable image without having to dig in the log.
    internal sealed class Base64ImageRenderer
    {
        // 320 matches the renderViewbox host width; capping prevents a huge
        // intrinsic image from blowing past the Viewbox layout pass.
        private const double MaxDimension = 320;

        private readonly Grid _root;
        private readonly Image _image;
        private readonly TextBlock _message;

        public Base64ImageRenderer()
        {
            _image = new Image
            {
                Stretch = Stretch.Uniform,
                MaxWidth = MaxDimension,
                MaxHeight = MaxDimension,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = Visibility.Collapsed,
            };
            _message = new TextBlock
            {
                Text = "No Image",
                Foreground = new SolidColorBrush(WidgetTheme.SubtleColor),
                FontSize = WidgetTheme.FontText,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            _root = new Grid();
            _root.Children.Add(_image);
            _root.Children.Add(_message);
        }

        public UIElement Visual => _root;

        public void Update(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                ShowMessage("No Image", WidgetTheme.SubtleColor);
                return;
            }

            try
            {
                string payload = StripDataUri(value);
                byte[] bytes = Convert.FromBase64String(payload);
                var bmp = new BitmapImage();
                using (var ms = new MemoryStream(bytes))
                {
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.StreamSource = ms;
                    bmp.EndInit();
                }
                bmp.Freeze();
                _image.Source = bmp;
                _image.Visibility = Visibility.Visible;
                _message.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                ShowMessage("Decode error: " + ex.Message, Color.FromRgb(0xD8, 0x39, 0x2C));
            }
        }

        public void Clear()
        {
            _image.Source = null;
            ShowMessage("No Image", WidgetTheme.SubtleColor);
        }

        private void ShowMessage(string text, Color color)
        {
            _image.Source = null;
            _image.Visibility = Visibility.Collapsed;
            _message.Text = text;
            _message.Foreground = new SolidColorBrush(color);
            _message.Visibility = Visibility.Visible;
        }

        // Accepts both raw base64 and the "data:image/png;base64,..." URI form.
        private static string StripDataUri(string s)
        {
            if (s.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                int comma = s.IndexOf(',');
                if (comma > 0) return s.Substring(comma + 1).Trim();
            }
            return s.Trim();
        }
    }
}
