using System;
using System.Collections.Generic;
using System.IO;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;

namespace SnapReport.Services
{
    internal class CameraReportEntry
    {
        public string CameraName { get; set; }
        public byte[] ImageData { get; set; }
        public DateTime Timestamp { get; set; }
        public string ErrorMessage { get; set; }
    }

    /// <summary>
    /// PdfSharp 6.x requires a font resolver. This one loads system fonts from the Windows fonts folder.
    /// </summary>
    internal class SystemFontResolver : IFontResolver
    {
        public FontResolverInfo ResolveTypeface(string familyName, bool isBold, bool isItalic)
        {
            string faceName = familyName.ToLowerInvariant();
            if (isBold && isItalic) faceName += "#bi";
            else if (isBold) faceName += "#b";
            else if (isItalic) faceName += "#i";
            return new FontResolverInfo(faceName);
        }

        public byte[] GetFont(string faceName)
        {
            var fontsDir = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);

            // Map face name to file
            string fileName;
            switch (faceName)
            {
                case "arial#b":   fileName = "arialbd.ttf"; break;
                case "arial#i":   fileName = "ariali.ttf"; break;
                case "arial#bi":  fileName = "arialbi.ttf"; break;
                case "arial":     fileName = "arial.ttf"; break;
                default:          fileName = "arial.ttf"; break;
            }

            var path = Path.Combine(fontsDir, fileName);
            if (File.Exists(path))
                return File.ReadAllBytes(path);

            // Fallback: try segoeui (always present on modern Windows)
            var fallback = Path.Combine(fontsDir, "segoeui.ttf");
            if (File.Exists(fallback))
                return File.ReadAllBytes(fallback);

            return null;
        }
    }

    internal static class PdfReportService
    {
        private static bool _fontResolverSet;

        private static void EnsureFontResolver()
        {
            if (_fontResolverSet) return;
            GlobalFontSettings.FontResolver = new SystemFontResolver();
            _fontResolverSet = true;
        }

        public static void GenerateReport(string outputPath, List<CameraReportEntry> entries)
        {
            EnsureFontResolver();

            var document = new PdfDocument();
            document.Info.Title = "SnapReport - Camera Snapshot Report";
            document.Info.Author = "SnapReport Plugin";

            foreach (var entry in entries)
            {
                var page = document.AddPage();
                page.Size = PdfSharp.PageSize.A4;
                var gfx = XGraphics.FromPdfPage(page);

                // Header: camera name + timestamp
                gfx.DrawString(entry.CameraName, new XFont("Arial", 16, XFontStyleEx.Bold),
                               XBrushes.Black, new XPoint(40, 40));
                gfx.DrawString(entry.Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),
                               new XFont("Arial", 10), XBrushes.Gray, new XPoint(40, 60));

                if (entry.ImageData != null)
                {
                    using (var stream = new MemoryStream(entry.ImageData))
                    {
                        var image = XImage.FromStream(stream);
                        double maxWidth = page.Width.Point - XUnit.FromPoint(80).Point;
                        double maxHeight = page.Height.Point - XUnit.FromPoint(120).Point;
                        double scale = Math.Min(maxWidth / image.PixelWidth, maxHeight / image.PixelHeight);
                        gfx.DrawImage(image, 40, 80, image.PixelWidth * scale, image.PixelHeight * scale);
                    }
                }
                else
                {
                    gfx.DrawString(entry.ErrorMessage ?? "Snapshot unavailable",
                                   new XFont("Arial", 14), XBrushes.Red,
                                   new XRect(0, 200, page.Width.Point, 100),
                                   XStringFormats.Center);
                }
            }

            document.Save(outputPath);
        }
    }
}
