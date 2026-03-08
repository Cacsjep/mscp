using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using RTSPDriver.Rtsp;

namespace RTSPDriver.Video
{
    /// <summary>
    /// Generates rich JPEG status frames showing connection state, errors,
    /// and diagnostics. This is the key differentiator from the Universal Driver.
    /// </summary>
    public static class StatusFrameGenerator
    {
        private static readonly int Width = 1280;
        private static readonly int Height = 720;
        private static readonly Color BgColor = Color.FromArgb(51, 51, 51);
        private static readonly Color CyanColor = Color.FromArgb(80, 200, 255);
        private static readonly Color GreenColor = Color.FromArgb(80, 220, 120);
        private static readonly Color RedColor = Color.FromArgb(255, 80, 80);
        private static readonly Color OrangeColor = Color.FromArgb(255, 180, 60);
        private static readonly Color GrayColor = Color.FromArgb(160, 160, 160);
        private static readonly Color DimGrayColor = Color.FromArgb(100, 100, 100);

        /// <summary>
        /// Generate a frame showing "Connecting..." with attempt info.
        /// </summary>
        public static byte[] GenerateConnectingFrame(string deviceName, string displayUrl, string transport, int attempt)
        {
            return RenderFrame((g, w, h) =>
            {
                DrawHeader(g, w, deviceName);
                DrawCenterTitle(g, w, h, "Connecting...", CyanColor);

                float detailY = h * 0.52f;
                DrawDetailLine(g, w, ref detailY, $"URL: {displayUrl}");
                DrawDetailLine(g, w, ref detailY, $"Transport: {transport.ToUpper()} (RTP over {transport.ToUpper()})");
                DrawDetailLine(g, w, ref detailY, $"Attempt: {attempt}");

                DrawPulsingDots(g, w, h * 0.38f);
            });
        }

        /// <summary>
        /// Generate a frame showing "Awaiting Keyframe" after RTSP connect but before first IDR.
        /// </summary>
        public static byte[] GenerateAwaitingKeyFrameFrame(string deviceName, string displayUrl, string transport)
        {
            return RenderFrame((g, w, h) =>
            {
                DrawHeader(g, w, deviceName);
                DrawCenterTitle(g, w, h, "Awaiting Keyframe...", CyanColor);

                float detailY = h * 0.52f;
                DrawDetailLine(g, w, ref detailY, $"URL: {displayUrl}");
                DrawDetailLine(g, w, ref detailY, $"Transport: {transport.ToUpper()}");
                DrawDetailLine(g, w, ref detailY, "Connected, waiting for first keyframe (IDR)");

                DrawPulsingDots(g, w, h * 0.38f);
            });
        }

        /// <summary>
        /// Generate a frame showing the error message prominently after a connection failure.
        /// </summary>
        public static byte[] GenerateReconnectingFrame(string deviceName, string displayUrl, string transport, string lastError, int attempt, int reconnectSec)
        {
            return RenderFrame((g, w, h) =>
            {
                DrawHeader(g, w, deviceName);

                // Error message is the main title
                string title = !string.IsNullOrEmpty(lastError) ? lastError : "Connection Lost";
                DrawWrappedTitle(g, w, h, title, OrangeColor, 0.26f);

                float detailY = h * 0.55f;
                DrawDetailLine(g, w, ref detailY, $"URL: {displayUrl}");
                DrawDetailLine(g, w, ref detailY, $"Transport: {transport.ToUpper()}");
                DrawDetailLine(g, w, ref detailY, $"Reconnecting in {reconnectSec}s... (attempt {attempt})", OrangeColor);
            });
        }

        /// <summary>
        /// Generate a frame for authentication failure.
        /// </summary>
        public static byte[] GenerateAuthFailedFrame(string deviceName, string displayUrl, string error)
        {
            return RenderFrame((g, w, h) =>
            {
                DrawHeader(g, w, deviceName);
                DrawLockIcon(g, w, h * 0.30f);
                DrawCenterTitle(g, w, h, "Authentication Failed", RedColor, 0.40f);

                float detailY = h * 0.55f;
                DrawDetailLine(g, w, ref detailY, $"URL: {displayUrl}");
                DrawDetailLine(g, w, ref detailY, "Check username and password in Management Client");
                if (!string.IsNullOrEmpty(error))
                    DrawDetailLine(g, w, ref detailY, error, DimGrayColor);
            });
        }

        /// <summary>
        /// Generate a frame for connection refused.
        /// </summary>
        public static byte[] GenerateConnectionErrorFrame(string deviceName, string displayUrl, string transport, string error)
        {
            return RenderFrame((g, w, h) =>
            {
                DrawHeader(g, w, deviceName);

                string title = !string.IsNullOrEmpty(error) ? error : "Connection Failed";
                DrawWrappedTitle(g, w, h, title, RedColor, 0.26f);

                float detailY = h * 0.55f;
                DrawDetailLine(g, w, ref detailY, $"URL: {displayUrl}");
                DrawDetailLine(g, w, ref detailY, $"Transport: {transport.ToUpper()}");
            });
        }

        /// <summary>
        /// Generate a frame for unsupported codec.
        /// </summary>
        public static byte[] GenerateUnsupportedCodecFrame(string deviceName, string displayUrl, string error)
        {
            return RenderFrame((g, w, h) =>
            {
                DrawHeader(g, w, deviceName);
                DrawWarningTriangle(g, w, h * 0.30f, OrangeColor);
                DrawCenterTitle(g, w, h, "Unsupported Codec", OrangeColor, 0.40f);

                float detailY = h * 0.55f;
                DrawDetailLine(g, w, ref detailY, $"URL: {displayUrl}");
                if (!string.IsNullOrEmpty(error))
                    DrawDetailLine(g, w, ref detailY, error, GrayColor);
                DrawDetailLine(g, w, ref detailY, "Only H.264 and H.265/HEVC are supported");
            });
        }

        /// <summary>
        /// Generate a frame for no video track found.
        /// </summary>
        public static byte[] GenerateNoVideoTrackFrame(string deviceName, string displayUrl, string error)
        {
            return RenderFrame((g, w, h) =>
            {
                DrawHeader(g, w, deviceName);
                DrawWarningTriangle(g, w, h * 0.30f, OrangeColor);
                DrawCenterTitle(g, w, h, "No Video Stream", OrangeColor, 0.40f);

                float detailY = h * 0.55f;
                DrawDetailLine(g, w, ref detailY, $"URL: {displayUrl}");
                DrawDetailLine(g, w, ref detailY, "RTSP source has no video track");
                if (!string.IsNullOrEmpty(error))
                    DrawDetailLine(g, w, ref detailY, error, DimGrayColor);
            });
        }

        /// <summary>
        /// Generate a frame showing the channel is disabled.
        /// </summary>
        public static byte[] GenerateDisabledFrame(string deviceName)
        {
            return RenderFrame((g, w, h) =>
            {
                DrawHeader(g, w, deviceName);

                using (var font = new Font("Segoe UI", 36, FontStyle.Regular))
                using (var brush = new SolidBrush(DimGrayColor))
                {
                    var sf = new StringFormat { Alignment = StringAlignment.Center };
                    g.DrawString("Channel Disabled", font, brush, w / 2f, h * 0.40f, sf);
                }

                float detailY = h * 0.58f;
                DrawDetailLine(g, w, ref detailY, "Enable in Management Client", DimGrayColor);
            });
        }

        /// <summary>
        /// Generate a frame showing the channel has no RTSP path configured yet,
        /// with helpful examples on how to configure it.
        /// </summary>
        public static byte[] GenerateNotConfiguredFrame(string deviceName)
        {
            return RenderFrame((g, w, h) =>
            {
                DrawHeader(g, w, deviceName);
                DrawCenterTitle(g, w, h, "Not Configured", DimGrayColor, 0.22f);

                float detailY = h * 0.36f;
                DrawDetailLine(g, w, ref detailY, "Set the RTSP Path in Management Client to start streaming");
                detailY += 10;
                DrawDetailLine(g, w, ref detailY, "Example RTSP URLs and their Stream Path setting:", Color.White);
                detailY += 6;
                DrawDetailLine(g, w, ref detailY, "Axis:  rtsp://ip/axis-media/media.amp", CyanColor);
                DrawDetailLine(g, w, ref detailY, "Path:  /axis-media/media.amp", GrayColor);
                detailY += 6;
                DrawDetailLine(g, w, ref detailY, "Hikvision:  rtsp://ip/Streaming/Channels/101", CyanColor);
                DrawDetailLine(g, w, ref detailY, "Path:  /Streaming/Channels/101", GrayColor);
                detailY += 6;
                DrawDetailLine(g, w, ref detailY, "Dahua:  rtsp://ip/cam/realmonitor?channel=1&subtype=0", CyanColor);
                DrawDetailLine(g, w, ref detailY, "Path:  /cam/realmonitor?channel=1&subtype=0", GrayColor);
                detailY += 6;
                DrawDetailLine(g, w, ref detailY, "ONVIF:  rtsp://ip/onvif-media/media.amp", CyanColor);
                DrawDetailLine(g, w, ref detailY, "Path:  /onvif-media/media.amp", GrayColor);
                detailY += 14;
                DrawDetailLine(g, w, ref detailY, "The IP address and credentials are taken from the hardware settings.", DimGrayColor);
            });
        }

        /// <summary>
        /// Generate a solid dark frame (no overlay).
        /// </summary>
        public static byte[] GenerateBlackFrame()
        {
            return RenderFrame((g, w, h) => { });
        }

        /// <summary>
        /// Generate an idle/offline frame showing "Waiting for stream".
        /// </summary>
        public static byte[] GenerateOfflineFrame(string deviceName, string displayUrl, string transport)
        {
            return RenderFrame((g, w, h) =>
            {
                DrawHeader(g, w, deviceName);
                DrawBroadcastIcon(g, w / 2f, h * 0.32f, 40);
                DrawCenterTitle(g, w, h, "Stream Offline", CyanColor, 0.40f);

                float detailY = h * 0.55f;
                DrawDetailLine(g, w, ref detailY, $"URL: {displayUrl}");
                DrawDetailLine(g, w, ref detailY, $"Transport: {transport.ToUpper()}");
            });
        }

        // --- Rendering helpers ---

        private delegate void DrawAction(Graphics g, int width, int height);

        private static byte[] RenderFrame(DrawAction drawAction)
        {
            using (var bitmap = new Bitmap(Width, Height, PixelFormat.Format24bppRgb))
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(BgColor);
                graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
                graphics.SmoothingMode = SmoothingMode.AntiAlias;

                drawAction(graphics, Width, Height);

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

        private static void DrawHeader(Graphics g, int width, string deviceName)
        {
            float margin = 40f;
            string versionText = $"{Constants.DriverDisplayName} v{Constants.DriverVersion}";

            using (var nameFont = new Font("Segoe UI", 18, FontStyle.Bold))
            using (var versionFont = new Font("Segoe UI", 13, FontStyle.Regular))
            using (var whiteBrush = new SolidBrush(Color.White))
            using (var grayBrush = new SolidBrush(GrayColor))
            {
                g.DrawString(deviceName, nameFont, whiteBrush, margin, margin);
                var sfRight = new StringFormat { Alignment = StringAlignment.Far };
                g.DrawString(versionText, versionFont, grayBrush, width - margin, margin + 4, sfRight);
            }
        }

        private static void DrawWrappedTitle(Graphics g, int width, int height, string text, Color color, float yRatio)
        {
            // Use a slightly smaller font that can wrap for long error messages
            float margin = 60f;
            using (var font = new Font("Segoe UI", 28, FontStyle.Bold))
            using (var brush = new SolidBrush(color))
            {
                var rect = new RectangleF(margin, height * yRatio, width - margin * 2, height * 0.28f);
                var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Near };
                g.DrawString(text, font, brush, rect, sf);
            }
        }

        private static void DrawCenterTitle(Graphics g, int width, int height, string text, Color color, float yRatio = 0.40f)
        {
            using (var font = new Font("Segoe UI", 46, FontStyle.Bold))
            using (var brush = new SolidBrush(color))
            {
                var sf = new StringFormat { Alignment = StringAlignment.Center };
                g.DrawString(text, font, brush, width / 2f, height * yRatio, sf);
            }
        }

        private static void DrawDetailLine(Graphics g, int width, ref float y, string text, Color? color = null)
        {
            using (var font = new Font("Segoe UI", 15, FontStyle.Regular))
            using (var brush = new SolidBrush(color ?? GrayColor))
            {
                var sf = new StringFormat { Alignment = StringAlignment.Center };
                g.DrawString(text, font, brush, width / 2f, y, sf);
                y += font.GetHeight(g) + 6;
            }
        }

        private static void DrawBroadcastIcon(Graphics g, float cx, float cy, float radius)
        {
            var pulseColor = (DateTime.UtcNow.Second % 2 == 0) ? Color.White : CyanColor;
            using (var pen = new Pen(pulseColor, 2.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            using (var brush = new SolidBrush(pulseColor))
            {
                float dotR = 5f;
                g.FillEllipse(brush, cx - dotR, cy - dotR, dotR * 2, dotR * 2);

                float[] radii = { radius * 0.35f, radius * 0.6f, radius * 0.9f };
                foreach (float r in radii)
                {
                    g.DrawArc(pen, cx - r, cy - r, r * 2, r * 2, -40, 80);
                    g.DrawArc(pen, cx - r, cy - r, r * 2, r * 2, 140, 80);
                }
            }
        }

        private static void DrawPulsingDots(Graphics g, int width, float y)
        {
            int dotCount = 3;
            int activeDot = (int)(DateTime.UtcNow.Millisecond / 333) % dotCount;
            float cx = width / 2f;
            float spacing = 20f;
            float startX = cx - (dotCount - 1) * spacing / 2f;

            for (int i = 0; i < dotCount; i++)
            {
                float r = (i == activeDot) ? 6f : 4f;
                var color = (i == activeDot) ? CyanColor : DimGrayColor;
                using (var brush = new SolidBrush(color))
                {
                    g.FillEllipse(brush, startX + i * spacing - r, y - r, r * 2, r * 2);
                }
            }
        }

        private static void DrawLockIcon(Graphics g, int width, float y)
        {
            float cx = width / 2f;
            using (var pen = new Pen(RedColor, 3f))
            using (var brush = new SolidBrush(RedColor))
            {
                // Lock body
                float bodyW = 28f, bodyH = 22f;
                g.FillRectangle(brush, cx - bodyW / 2, y, bodyW, bodyH);

                // Lock shackle (arc)
                float shackleW = 20f, shackleH = 16f;
                g.DrawArc(pen, cx - shackleW / 2, y - shackleH, shackleW, shackleH * 2, 180, 180);
            }
        }

        private static void DrawXIcon(Graphics g, int width, float y, Color color)
        {
            float cx = width / 2f;
            float size = 18f;
            using (var pen = new Pen(color, 4f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                g.DrawLine(pen, cx - size, y - size, cx + size, y + size);
                g.DrawLine(pen, cx + size, y - size, cx - size, y + size);
            }
        }

        private static void DrawWarningTriangle(Graphics g, int width, float y, Color color)
        {
            float cx = width / 2f;
            float size = 24f;
            var points = new PointF[]
            {
                new PointF(cx, y - size),
                new PointF(cx - size, y + size * 0.7f),
                new PointF(cx + size, y + size * 0.7f),
            };
            using (var pen = new Pen(color, 3f) { LineJoin = LineJoin.Round })
            {
                g.DrawPolygon(pen, points);
            }
            // Exclamation mark
            using (var brush = new SolidBrush(color))
            using (var font = new Font("Segoe UI", 18, FontStyle.Bold))
            {
                var sf = new StringFormat { Alignment = StringAlignment.Center };
                g.DrawString("!", font, brush, cx, y - size * 0.4f, sf);
            }
        }

        private static ImageCodecInfo GetEncoder(ImageFormat format)
        {
            foreach (var codec in ImageCodecInfo.GetImageDecoders())
            {
                if (codec.FormatID == format.Guid)
                    return codec;
            }
            return null;
        }
    }
}
