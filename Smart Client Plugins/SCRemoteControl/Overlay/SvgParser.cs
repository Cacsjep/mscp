using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Xml.Linq;

namespace SCRemoteControl.Overlay
{
    public class SvgParseException : Exception
    {
        public SvgParseException(string message) : base(message) { }
    }

    /// <summary>
    /// Parses a subset of SVG (rect, circle, ellipse, line, polyline, polygon, path,
    /// text, g) into ParsedOverlay descriptors. Coordinates stay in viewBox space;
    /// scaling to paint pixels happens at render time. Default viewBox is 0 0 1000 1000
    /// when the document does not declare one.
    /// </summary>
    public static class SvgParser
    {
        private static readonly Regex _wsSplit = new Regex(@"[\s,]+", RegexOptions.Compiled);
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static ParsedOverlay Parse(string svg)
        {
            if (string.IsNullOrWhiteSpace(svg))
                throw new SvgParseException("SVG body is empty");

            XDocument doc;
            try { doc = XDocument.Parse(svg); }
            catch (Exception ex) { throw new SvgParseException("Invalid XML: " + ex.Message); }

            var root = doc.Root;
            if (root == null || root.Name.LocalName != "svg")
                throw new SvgParseException("Root element must be <svg>");

            var overlay = new ParsedOverlay
            {
                ViewBox = ReadViewBox(root)
            };

            var rootStyle = new ShapeStyle();
            ReadStyle(root, rootStyle);

            foreach (var child in root.Elements())
                VisitElement(child, rootStyle, Matrix.Identity, overlay.Shapes);

            return overlay;
        }

        // --- viewBox ---

        private static Rect ReadViewBox(XElement svg)
        {
            var vb = (string)svg.Attribute("viewBox");
            if (string.IsNullOrWhiteSpace(vb))
                return new Rect(0, 0, 1000, 1000);

            var parts = _wsSplit.Split(vb.Trim());
            if (parts.Length != 4)
                throw new SvgParseException("viewBox must have 4 numbers: '" + vb + "'");
            try
            {
                return new Rect(
                    double.Parse(parts[0], Inv),
                    double.Parse(parts[1], Inv),
                    double.Parse(parts[2], Inv),
                    double.Parse(parts[3], Inv));
            }
            catch (Exception ex) { throw new SvgParseException("viewBox parse failed: " + ex.Message); }
        }

        // --- traversal ---

        private static void VisitElement(XElement elem, ShapeStyle parentStyle, Matrix parentTransform, List<ParsedShape> output)
        {
            var style = parentStyle.Clone();
            ReadStyle(elem, style);

            var transform = parentTransform;
            var localTransform = ReadTransform(elem);
            if (!localTransform.IsIdentity)
            {
                // Local transform applies BEFORE the parent transform when composing
                // matrices for points: p' = parent * local * p
                localTransform.Append(parentTransform);
                transform = localTransform;
            }

            switch (elem.Name.LocalName)
            {
                case "g":
                    foreach (var child in elem.Elements())
                        VisitElement(child, style, transform, output);
                    break;
                case "rect":
                    output.Add(new RectShape
                    {
                        Style = style,
                        Transform = transform,
                        X = D(elem, "x"),
                        Y = D(elem, "y"),
                        Width = D(elem, "width"),
                        Height = D(elem, "height"),
                        Rx = D(elem, "rx"),
                        Ry = D(elem, "ry"),
                    });
                    break;
                case "circle":
                    output.Add(new CircleShape
                    {
                        Style = style,
                        Transform = transform,
                        Cx = D(elem, "cx"),
                        Cy = D(elem, "cy"),
                        R = D(elem, "r"),
                    });
                    break;
                case "ellipse":
                    output.Add(new EllipseShape
                    {
                        Style = style,
                        Transform = transform,
                        Cx = D(elem, "cx"),
                        Cy = D(elem, "cy"),
                        Rx = D(elem, "rx"),
                        Ry = D(elem, "ry"),
                    });
                    break;
                case "line":
                    output.Add(new LineShape
                    {
                        Style = style,
                        Transform = transform,
                        X1 = D(elem, "x1"),
                        Y1 = D(elem, "y1"),
                        X2 = D(elem, "x2"),
                        Y2 = D(elem, "y2"),
                    });
                    break;
                case "polyline":
                    output.Add(new PolyShape
                    {
                        Style = style,
                        Transform = transform,
                        Closed = false,
                        Points = ParsePoints((string)elem.Attribute("points")),
                    });
                    break;
                case "polygon":
                    output.Add(new PolyShape
                    {
                        Style = style,
                        Transform = transform,
                        Closed = true,
                        Points = ParsePoints((string)elem.Attribute("points")),
                    });
                    break;
                case "path":
                    output.Add(new PathShape
                    {
                        Style = style,
                        Transform = transform,
                        D = (string)elem.Attribute("d"),
                    });
                    break;
                case "text":
                    output.Add(new TextShape
                    {
                        Style = style,
                        Transform = transform,
                        X = D(elem, "x"),
                        Y = D(elem, "y"),
                        Text = elem.Value ?? "",
                    });
                    break;
                // Silently skip unknowns (e.g. <title>, <desc>, <defs>, <metadata>).
            }
        }

        private static double D(XElement e, string name, double fallback = 0)
        {
            var s = (string)e.Attribute(name);
            if (string.IsNullOrEmpty(s)) return fallback;
            // Strip "px" / "%" suffixes; treat both as the raw number.
            s = Regex.Replace(s, @"(px|pt|%|em|ex|cm|mm|in)$", "", RegexOptions.IgnoreCase).Trim();
            return double.TryParse(s, NumberStyles.Float, Inv, out var v) ? v : fallback;
        }

        private static List<Point> ParsePoints(string s)
        {
            var list = new List<Point>();
            if (string.IsNullOrWhiteSpace(s)) return list;
            var parts = _wsSplit.Split(s.Trim());
            for (int i = 0; i + 1 < parts.Length; i += 2)
            {
                if (double.TryParse(parts[i], NumberStyles.Float, Inv, out var x) &&
                    double.TryParse(parts[i + 1], NumberStyles.Float, Inv, out var y))
                    list.Add(new Point(x, y));
            }
            return list;
        }

        // --- style ---

        private static void ReadStyle(XElement elem, ShapeStyle style)
        {
            // SVG presentation attributes first, then inline style overrides them.
            ReadStyleAttr(elem, "fill", style, v => style.Fill = ParseColor(v));
            ReadStyleAttr(elem, "stroke", style, v => style.Stroke = ParseColor(v));
            ReadStyleAttr(elem, "fill-opacity", style, v => style.FillOpacity = Clamp01(D(v)));
            ReadStyleAttr(elem, "stroke-opacity", style, v => style.StrokeOpacity = Clamp01(D(v)));
            ReadStyleAttr(elem, "opacity", style, v => style.Opacity = Clamp01(D(v)));
            ReadStyleAttr(elem, "stroke-width", style, v => style.StrokeWidth = Math.Max(0, D(v)));
            ReadStyleAttr(elem, "font-family", style, v => style.FontFamily = v);
            ReadStyleAttr(elem, "font-size", style, v => style.FontSize = Math.Max(1, D(v)));
            ReadStyleAttr(elem, "font-weight", style, v => style.FontWeight = ParseWeight(v));
            ReadStyleAttr(elem, "font-style", style, v => style.FontStyle = ParseFontStyle(v));

            // Inline style="" with higher specificity.
            var inlineStyle = (string)elem.Attribute("style");
            if (string.IsNullOrEmpty(inlineStyle)) return;

            foreach (var decl in inlineStyle.Split(';'))
            {
                var kv = decl.Split(new[] { ':' }, 2);
                if (kv.Length != 2) continue;
                var prop = kv[0].Trim().ToLowerInvariant();
                var val = kv[1].Trim();
                switch (prop)
                {
                    case "fill": style.Fill = ParseColor(val); break;
                    case "stroke": style.Stroke = ParseColor(val); break;
                    case "fill-opacity": style.FillOpacity = Clamp01(D(val)); break;
                    case "stroke-opacity": style.StrokeOpacity = Clamp01(D(val)); break;
                    case "opacity": style.Opacity = Clamp01(D(val)); break;
                    case "stroke-width": style.StrokeWidth = Math.Max(0, D(val)); break;
                    case "font-family": style.FontFamily = val; break;
                    case "font-size": style.FontSize = Math.Max(1, D(val)); break;
                    case "font-weight": style.FontWeight = ParseWeight(val); break;
                    case "font-style": style.FontStyle = ParseFontStyle(val); break;
                }
            }
        }

        private static void ReadStyleAttr(XElement elem, string name, ShapeStyle style, Action<string> apply)
        {
            var v = (string)elem.Attribute(name);
            if (!string.IsNullOrEmpty(v)) apply(v);
        }

        private static double D(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            s = Regex.Replace(s, @"(px|pt|%|em|ex|cm|mm|in)$", "", RegexOptions.IgnoreCase).Trim();
            return double.TryParse(s, NumberStyles.Float, Inv, out var v) ? v : 0;
        }

        private static double Clamp01(double v) => v < 0 ? 0 : (v > 1 ? 1 : v);

        private static FontWeight ParseWeight(string v)
        {
            v = (v ?? "").Trim().ToLowerInvariant();
            switch (v)
            {
                case "bold": return FontWeights.Bold;
                case "normal": return FontWeights.Normal;
                case "lighter": return FontWeights.Light;
                case "bolder": return FontWeights.ExtraBold;
                default:
                    if (int.TryParse(v, out var n)) return FontWeight.FromOpenTypeWeight(Math.Min(900, Math.Max(100, n)));
                    return FontWeights.Normal;
            }
        }

        private static FontStyle ParseFontStyle(string v)
        {
            v = (v ?? "").Trim().ToLowerInvariant();
            if (v == "italic" || v == "oblique") return FontStyles.Italic;
            return FontStyles.Normal;
        }

        // --- color ---

        private static readonly Regex _rgbRegex = new Regex(@"^rgba?\(\s*([^)]+)\)\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static Color? ParseColor(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            s = s.Trim();
            if (string.Equals(s, "none", StringComparison.OrdinalIgnoreCase)) return null;
            if (string.Equals(s, "transparent", StringComparison.OrdinalIgnoreCase)) return Colors.Transparent;

            var m = _rgbRegex.Match(s);
            if (m.Success)
            {
                var parts = m.Groups[1].Value.Split(',').Select(p => p.Trim()).ToArray();
                if (parts.Length >= 3 &&
                    double.TryParse(parts[0], NumberStyles.Float, Inv, out var r) &&
                    double.TryParse(parts[1], NumberStyles.Float, Inv, out var g) &&
                    double.TryParse(parts[2], NumberStyles.Float, Inv, out var b))
                {
                    byte a = 255;
                    if (parts.Length >= 4 && double.TryParse(parts[3], NumberStyles.Float, Inv, out var ad))
                        a = (byte)Math.Max(0, Math.Min(255, ad * 255));
                    return Color.FromArgb(a, ToByte(r), ToByte(g), ToByte(b));
                }
                return null;
            }

            // Expand 3-char hex (#f00) to 6-char (#ff0000); ColorConverter handles
            // #rrggbb and named colors natively.
            if (s.StartsWith("#") && s.Length == 4)
            {
                s = "#" + s[1] + s[1] + s[2] + s[2] + s[3] + s[3];
            }

            try { return (Color)ColorConverter.ConvertFromString(s); }
            catch { return null; }
        }

        private static byte ToByte(double v) => (byte)Math.Max(0, Math.Min(255, v));

        // --- transform ---

        private static readonly Regex _transformRegex = new Regex(
            @"(matrix|translate|scale|rotate)\s*\(\s*([^)]+)\)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static Matrix ReadTransform(XElement elem)
        {
            var t = (string)elem.Attribute("transform");
            if (string.IsNullOrWhiteSpace(t)) return Matrix.Identity;

            var result = Matrix.Identity;
            foreach (Match m in _transformRegex.Matches(t))
            {
                var op = m.Groups[1].Value.ToLowerInvariant();
                var args = _wsSplit.Split(m.Groups[2].Value.Trim())
                    .Select(p => double.TryParse(p, NumberStyles.Float, Inv, out var v) ? v : 0)
                    .ToArray();

                Matrix local = Matrix.Identity;
                switch (op)
                {
                    case "matrix" when args.Length >= 6:
                        local = new Matrix(args[0], args[1], args[2], args[3], args[4], args[5]);
                        break;
                    case "translate":
                        var tx = args.Length > 0 ? args[0] : 0;
                        var ty = args.Length > 1 ? args[1] : 0;
                        local.Translate(tx, ty);
                        break;
                    case "scale":
                        var sx = args.Length > 0 ? args[0] : 1;
                        var sy = args.Length > 1 ? args[1] : sx;
                        local.Scale(sx, sy);
                        break;
                    case "rotate":
                        var angle = args.Length > 0 ? args[0] : 0;
                        if (args.Length >= 3) local.RotateAt(angle, args[1], args[2]);
                        else local.Rotate(angle);
                        break;
                }
                // SVG transform list reads left-to-right: each subsequent op happens
                // in the coordinate system of the previous (right multiplication).
                local.Append(result);
                result = local;
            }
            return result;
        }
    }
}
