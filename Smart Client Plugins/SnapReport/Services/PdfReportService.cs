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

        /// <summary>
        /// Generates the report. If <paramref name="maxBytesPerPart"/> is null, writes a single PDF
        /// to <paramref name="outputPath"/>. Otherwise splits into numbered parts named
        /// "<basename>.001.pdf", "<basename>.002.pdf", ... beside <paramref name="outputPath"/>.
        /// Returns the list of files actually written.
        /// </summary>
        public static List<string> GenerateReport(string outputPath, List<CameraReportEntry> entries, long? maxBytesPerPart = null)
        {
            EnsureFontResolver();

            var written = new List<string>();

            if (!maxBytesPerPart.HasValue)
            {
                var doc = NewDocument();
                foreach (var entry in entries)
                    AddEntryPage(doc, entry);
                doc.Save(outputPath);
                written.Add(outputPath);
                return written;
            }

            // Split mode - size by estimation: each page contributes roughly (image bytes + per-page overhead).
            // The image dominates; per-page overhead covers font references, drawing ops, page object.
            const long PerPageOverhead = 2048;
            const long DocOverhead = 8192;

            string dir = Path.GetDirectoryName(outputPath) ?? "";
            string baseName = Path.GetFileNameWithoutExtension(outputPath);
            string ext = Path.GetExtension(outputPath);
            if (string.IsNullOrEmpty(ext)) ext = ".pdf";

            int partIndex = 1;
            var current = NewDocument();
            int pagesInCurrent = 0;
            long estimated = DocOverhead;

            foreach (var entry in entries)
            {
                long pageSize = PerPageOverhead + (entry.ImageData != null ? entry.ImageData.Length : 0);

                // If adding this page would overflow and the current part already has at least one
                // page, finalize the part first so this entry starts the next part.
                if (pagesInCurrent > 0 && estimated + pageSize > maxBytesPerPart.Value)
                {
                    string partPath = Path.Combine(dir, string.Format("{0}.{1:D3}{2}", baseName, partIndex, ext));
                    current.Save(partPath);
                    written.Add(partPath);
                    partIndex++;
                    current = NewDocument();
                    pagesInCurrent = 0;
                    estimated = DocOverhead;
                }

                AddEntryPage(current, entry);
                pagesInCurrent++;
                estimated += pageSize;
            }

            // Flush remaining
            if (pagesInCurrent > 0 || written.Count == 0)
            {
                string partPath = Path.Combine(dir, string.Format("{0}.{1:D3}{2}", baseName, partIndex, ext));
                current.Save(partPath);
                written.Add(partPath);
            }

            return written;
        }

        private static PdfDocument NewDocument()
        {
            var document = new PdfDocument();
            document.Info.Title = "SnapReport - Camera Snapshot Report";
            document.Info.Author = "SnapReport Plugin";
            return document;
        }

        private static void AddEntryPage(PdfDocument document, CameraReportEntry entry)
        {
            var page = document.AddPage();
            page.Size = PdfSharp.PageSize.A4;
            var gfx = XGraphics.FromPdfPage(page);

            gfx.DrawString(entry.CameraName, new XFont("Arial", 16, XFontStyleEx.Bold),
                           XBrushes.Black, new XPoint(40, 40));
            gfx.DrawString(entry.Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),
                           new XFont("Arial", 10), XBrushes.Gray, new XPoint(40, 60));

            if (entry.ImageData != null)
            {
                using (var stream = new MemoryStream(entry.ImageData))
                {
                    var image = XImage.FromStream(stream);
                    double maxWidth = page.Width.Point - 80;
                    double maxHeight = page.Height.Point - 120;
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
    }
}
