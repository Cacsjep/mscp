using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace RTMPDriver.Video
{
    public static class TestPatternGenerator
    {
        private static readonly int Width = 1280;
        private static readonly int Height = 720;
        private static readonly Color BgColor = Color.FromArgb(51, 51, 51);
        private static readonly Color CyanColor = Color.FromArgb(80, 200, 255);
        private static readonly Color GrayColor = Color.FromArgb(160, 160, 160);
        private static readonly Color DimGrayColor = Color.FromArgb(120, 120, 120);

        public static byte[] GenerateFrame(string rtmpUrl, string deviceName, string driverVersion)
        {
            // Replace localhost with 0.0.0.0 for display
            string displayUrl = rtmpUrl?.Replace("localhost", "0.0.0.0") ?? "";

            string versionText = $"{Constants.DriverDisplayName} v{driverVersion}";

            using (var bitmap = new Bitmap(Width, Height, PixelFormat.Format24bppRgb))
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(BgColor);
                graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
                graphics.SmoothingMode = SmoothingMode.AntiAlias;

                DrawOverlay(graphics, Width, Height, displayUrl, deviceName, versionText);

                using (var ms = new MemoryStream())
                {
                    var encoder = GetEncoder(ImageFormat.Jpeg);
                    var encoderParams = new EncoderParameters(1);
                    encoderParams.Param[0] = new EncoderParameter(Encoder.Quality, 85L);
                    bitmap.Save(ms, encoder, encoderParams);
                    return ms.ToArray();
                }
            }
        }

        public static byte[] GenerateErrorFrame(string deviceName, string driverVersion, string errorMessage)
        {
            string versionText = $"{Constants.DriverDisplayName} v{driverVersion}";

            using (var bitmap = new Bitmap(Width, Height, PixelFormat.Format24bppRgb))
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(BgColor);
                graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
                graphics.SmoothingMode = SmoothingMode.AntiAlias;

                DrawErrorOverlay(graphics, Width, Height, deviceName, versionText, errorMessage);

                using (var ms = new MemoryStream())
                {
                    var encoder = GetEncoder(ImageFormat.Jpeg);
                    var encoderParams = new EncoderParameters(1);
                    encoderParams.Param[0] = new EncoderParameter(Encoder.Quality, 85L);
                    bitmap.Save(ms, encoder, encoderParams);
                    return ms.ToArray();
                }
            }
        }

        public static byte[] GenerateBlackFrame()
        {
            using (var bitmap = new Bitmap(Width, Height, PixelFormat.Format24bppRgb))
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(BgColor);

                using (var ms = new MemoryStream())
                {
                    var encoder = GetEncoder(ImageFormat.Jpeg);
                    var encoderParams = new EncoderParameters(1);
                    encoderParams.Param[0] = new EncoderParameter(Encoder.Quality, 85L);
                    bitmap.Save(ms, encoder, encoderParams);
                    return ms.ToArray();
                }
            }
        }

        private static void DrawOverlay(Graphics g, int width, int height, string rtmpUrl, string deviceName, string versionText)
        {
            float margin = 40f;

            using (var deviceNameFont = new Font("Segoe UI", 18, FontStyle.Bold))
            using (var versionFont = new Font("Segoe UI", 13, FontStyle.Regular))
            using (var offlineFont = new Font("Segoe UI", 52, FontStyle.Bold))
            using (var subtitleFont = new Font("Segoe UI", 16, FontStyle.Regular))
            using (var urlFont = new Font("Segoe UI", 15, FontStyle.Regular))
            using (var whiteBrush = new SolidBrush(Color.White))
            using (var cyanBrush = new SolidBrush(CyanColor))
            using (var grayBrush = new SolidBrush(GrayColor))
            using (var dimBrush = new SolidBrush(DimGrayColor))
            {
                // Top-left: device name
                g.DrawString(deviceName, deviceNameFont, whiteBrush, margin, margin);

                // Top-right: version
                var sfRight = new StringFormat { Alignment = StringAlignment.Far };
                g.DrawString(versionText, versionFont, grayBrush, width - margin, margin + 4, sfRight);

                // Center: broadcast icon above "Stream Offline"
                var sfCenter = new StringFormat { Alignment = StringAlignment.Center };
                float centerY = height * 0.35f;

                // Alternate broadcast icon color each frame: white / cyan
                var pulseColor = (DateTime.UtcNow.Second % 2 == 0) ? Color.White : CyanColor;
                DrawBroadcastIcon(g, width / 2f, centerY - 20, 40, pulseColor);

                // "Stream Offline" in cyan
                g.DrawString("Stream Offline", offlineFont, cyanBrush, width / 2f, centerY + 24, sfCenter);

                // Below: "Ready for receiving..."
                float subtitleY = centerY + 24 + offlineFont.GetHeight(g) + 10;
                g.DrawString("Ready for receiving...", subtitleFont, whiteBrush, width / 2f, subtitleY, sfCenter);

                // Below: arrow icon + RTMP URL
                float urlY = subtitleY + subtitleFont.GetHeight(g) + 8;
                string urlLine = $"Push your RTMP stream to {rtmpUrl}";
                g.DrawString(urlLine, urlFont, grayBrush, width / 2f, urlY, sfCenter);
            }
        }

        private static readonly Color RedColor = Color.FromArgb(255, 80, 80);

        private static void DrawErrorOverlay(Graphics g, int width, int height, string deviceName, string versionText, string errorMessage)
        {
            float margin = 40f;

            using (var deviceNameFont = new Font("Segoe UI", 18, FontStyle.Bold))
            using (var versionFont = new Font("Segoe UI", 13, FontStyle.Regular))
            using (var errorTitleFont = new Font("Segoe UI", 46, FontStyle.Bold))
            using (var errorMsgFont = new Font("Segoe UI", 17, FontStyle.Regular))
            using (var whiteBrush = new SolidBrush(Color.White))
            using (var redBrush = new SolidBrush(RedColor))
            using (var grayBrush = new SolidBrush(GrayColor))
            {
                // Top-left: device name
                g.DrawString(deviceName, deviceNameFont, whiteBrush, margin, margin);

                // Top-right: version
                var sfRight = new StringFormat { Alignment = StringAlignment.Far };
                g.DrawString(versionText, versionFont, grayBrush, width - margin, margin + 4, sfRight);

                // Center: error title
                var sfCenter = new StringFormat { Alignment = StringAlignment.Center };
                float centerY = height * 0.35f;
                g.DrawString("Server Error", errorTitleFont, redBrush, width / 2f, centerY, sfCenter);

                // Below: error message (word-wrapped)
                float msgY = centerY + errorTitleFont.GetHeight(g) + 16;
                var msgRect = new RectangleF(margin * 2, msgY, width - margin * 4, height - msgY - margin);
                var sfWrap = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Near };
                g.DrawString(errorMessage, errorMsgFont, grayBrush, msgRect, sfWrap);
            }
        }

        /// <summary>
        /// Draws a broadcast/signal icon: center dot with radiating arcs.
        /// </summary>
        private static void DrawBroadcastIcon(Graphics g, float cx, float cy, float radius, Color color)
        {
            using (var pen = new Pen(color, 2.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            using (var brush = new SolidBrush(color))
            {
                // Center dot
                float dotR = 5f;
                g.FillEllipse(brush, cx - dotR, cy - dotR, dotR * 2, dotR * 2);

                // Three pairs of radiating arcs (left and right)
                float[] radii = { radius * 0.35f, radius * 0.6f, radius * 0.9f };
                foreach (float r in radii)
                {
                    // Right arc
                    g.DrawArc(pen, cx - r, cy - r, r * 2, r * 2, -40, 80);
                    // Left arc
                    g.DrawArc(pen, cx - r, cy - r, r * 2, r * 2, 140, 80);
                }
            }
        }

        private static ImageCodecInfo GetEncoder(ImageFormat format)
        {
            foreach (var codec in ImageCodecInfo.GetImageDecoders())
            {
                if (codec.FormatID == format.Guid)
                {
                    return codec;
                }
            }
            return null;
        }
    }
}
