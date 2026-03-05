using System.Drawing;
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
    }
}
