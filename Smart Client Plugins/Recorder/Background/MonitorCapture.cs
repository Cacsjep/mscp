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
            var minX = screens.Min(s => s.Bounds.X);
            var minY = screens.Min(s => s.Bounds.Y);
            var maxX = screens.Max(s => s.Bounds.Right);
            var maxY = screens.Max(s => s.Bounds.Bottom);

            var totalWidth = maxX - minX;
            var totalHeight = maxY - minY;

            var stitched = new Bitmap(totalWidth, totalHeight);
            using (var g = Graphics.FromImage(stitched))
            {
                g.Clear(Color.Black);
                foreach (var screen in screens)
                {
                    using (var capture = CaptureScreen(screen))
                    {
                        g.DrawImage(capture, screen.Bounds.X - minX, screen.Bounds.Y - minY);
                    }
                }
            }
            return stitched;
        }
    }
}
