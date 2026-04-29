using FontAwesome5;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace ColoredTimeline.Background
{
    // Renders a FontAwesome Solid icon at a given color/size into a frozen BitmapSource
    // suitable for TimelineSequenceSource.MarkerIconSource. Cache by (icon, ARGB, size)
    // so panning the timeline doesn't re-render on every Reattach.
    internal static class MarkerIconRenderer
    {
        private static readonly object _lock = new object();
        private static readonly Dictionary<string, BitmapSource> _cache =
            new Dictionary<string, BitmapSource>();

        public static BitmapSource Render(EFontAwesomeIcon icon, Color color, int size = 16)
        {
            var key = ((int)icon).ToString() + "|" + color.ToString() + "|" + size;
            lock (_lock)
            {
                if (_cache.TryGetValue(key, out var cached)) return cached;
            }

            // Smart Client config-change callbacks can land on a non-UI thread; ImageAwesome
            // and RenderTargetBitmap require an STA dispatcher. Marshal to the application's
            // dispatcher when needed, falling back to a private STA-style render when there
            // is no application dispatcher (admin-client during plugin init can be one such case).
            var dispatcher = Application.Current?.Dispatcher;
            BitmapSource rtb;
            if (dispatcher != null && !dispatcher.CheckAccess())
                rtb = (BitmapSource)dispatcher.Invoke(new Func<BitmapSource>(() => RenderOnCurrentThread(icon, color, size)));
            else
                rtb = RenderOnCurrentThread(icon, color, size);

            lock (_lock) { _cache[key] = rtb; }
            return rtb;
        }

        private static BitmapSource RenderOnCurrentThread(EFontAwesomeIcon icon, Color color, int size)
        {
            var awesome = new ImageAwesome
            {
                Icon = icon,
                Foreground = new SolidColorBrush(color),
                Width = size,
                Height = size
            };
            awesome.Measure(new Size(size, size));
            awesome.Arrange(new Rect(0, 0, size, size));

            var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(awesome);
            rtb.Freeze();
            return rtb;
        }

        public static bool TryParseIcon(string name, out EFontAwesomeIcon icon)
        {
            icon = EFontAwesomeIcon.Solid_Bell;
            if (string.IsNullOrEmpty(name)) return false;
            return Enum.TryParse(name, ignoreCase: true, result: out icon);
        }

        public static Color ParseColor(string hex, Color fallback)
        {
            if (string.IsNullOrEmpty(hex)) return fallback;
            try
            {
                var c = (Color)ColorConverter.ConvertFromString(hex);
                return c;
            }
            catch { return fallback; }
        }
    }
}
