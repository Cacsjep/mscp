using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FontAwesome5;
using VideoOS.Platform.UI.Controls;

namespace CommunitySDK
{
    public static class PluginIcon
    {
        public static readonly System.Windows.Media.Color DefaultColor = System.Windows.Media.Color.FromRgb(33, 150, 243);

        public static Image Render(EFontAwesomeIcon icon, int size = 24)
        {
            return Render(icon, DefaultColor, size);
        }

        public static Image Render(EFontAwesomeIcon icon, System.Windows.Media.Color color, int size = 24)
        {
            var rtb = RenderBitmap(icon, color, size);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(rtb));
            // MemoryStream intentionally not disposed - Bitmap requires the stream to remain open
            var ms = new MemoryStream();
            encoder.Save(ms);
            ms.Position = 0;
            return new Bitmap(ms);
        }

        public static VideoOSIconSourceBase RenderIconSource(EFontAwesomeIcon icon, int size = 24)
        {
            return RenderIconSource(icon, DefaultColor, size);
        }

        public static VideoOSIconSourceBase RenderIconSource(EFontAwesomeIcon icon, System.Windows.Media.Color color, int size = 24)
        {
            var rtb = RenderBitmap(icon, color, size);
            return new VideoOSIconBitmapSource { BitmapSource = rtb };
        }

        private static RenderTargetBitmap RenderBitmap(EFontAwesomeIcon icon, System.Windows.Media.Color color, int size)
        {
            var awesome = new ImageAwesome
            {
                Icon = icon,
                Foreground = new SolidColorBrush(color),
                Width = size,
                Height = size
            };
            awesome.Measure(new System.Windows.Size(size, size));
            awesome.Arrange(new Rect(0, 0, size, size));

            var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(awesome);
            return rtb;
        }
    }
}
