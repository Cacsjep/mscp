using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Recorder.Background
{
    public static class MonitorCapture
    {
        public static Bitmap CaptureScreen(Screen screen)
        {
            var bounds = screen.Bounds;
            var bmp = new Bitmap(bounds.Width, bounds.Height);
            using (var g = Graphics.FromImage(bmp))
            {
                g.CopyFromScreen(bounds.X, bounds.Y, 0, 0, bounds.Size);
            }
            return bmp;
        }

        /// <summary>
        /// Captures all given screens and stitches them into one image
        /// based on their actual desktop positions.
        /// </summary>
        public static Bitmap CaptureAndStitch(Screen[] screens)
        {
            var sorted = screens.OrderBy(s => s.Bounds.X).ThenBy(s => s.Bounds.Y).ToArray();

            var totalWidth = sorted.Sum(s => s.Bounds.Width);
            var totalHeight = sorted.Max(s => s.Bounds.Height);

            var stitched = new Bitmap(totalWidth, totalHeight);
            using (var g = Graphics.FromImage(stitched))
            {
                g.Clear(Color.Black);
                var offsetX = 0;
                foreach (var screen in sorted)
                {
                    using (var capture = CaptureScreen(screen))
                    {
                        g.DrawImage(capture, offsetX, 0);
                    }
                    offsetX += screen.Bounds.Width;
                }
            }
            return stitched;
        }
    }
}
