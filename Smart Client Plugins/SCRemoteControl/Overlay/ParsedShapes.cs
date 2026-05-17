using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;

namespace SCRemoteControl.Overlay
{
    /// <summary>
    /// Result of parsing an SVG document. Shapes are stored in viewBox-relative
    /// coordinates; the final paint-pixel transform is applied at render time so
    /// the overlay rescales with the viewport.
    /// </summary>
    public class ParsedOverlay
    {
        public Rect ViewBox { get; set; } = new Rect(0, 0, 1000, 1000);
        public List<ParsedShape> Shapes { get; set; } = new List<ParsedShape>();
    }

    public class ShapeStyle
    {
        public Color? Fill;
        public Color? Stroke;
        public double FillOpacity = 1.0;
        public double StrokeOpacity = 1.0;
        public double Opacity = 1.0;
        public double StrokeWidth = 0;
        public string FontFamily;
        public double FontSize = 16;
        public FontWeight FontWeight = FontWeights.Normal;
        public FontStyle FontStyle = FontStyles.Normal;

        public ShapeStyle Clone()
        {
            return new ShapeStyle
            {
                Fill = Fill,
                Stroke = Stroke,
                FillOpacity = FillOpacity,
                StrokeOpacity = StrokeOpacity,
                Opacity = Opacity,
                StrokeWidth = StrokeWidth,
                FontFamily = FontFamily,
                FontSize = FontSize,
                FontWeight = FontWeight,
                FontStyle = FontStyle,
            };
        }
    }

    public abstract class ParsedShape
    {
        public ShapeStyle Style { get; set; } = new ShapeStyle();
        public Matrix Transform { get; set; } = Matrix.Identity;

        /// <summary>
        /// Build a WPF Shape positioned in the target paint area. Caller provides
        /// the viewBox-to-paint matrix; this method composes it with the shape's
        /// own transform.
        /// </summary>
        public Shape Render(Matrix viewBoxToPaint)
        {
            var geometry = BuildGeometry();
            if (geometry == null) return null;

            var composed = Transform;
            composed.Append(viewBoxToPaint);
            geometry.Transform = new MatrixTransform(composed);

            var path = new Path { Data = geometry };
            ApplyStyle(path);
            return path;
        }

        protected abstract Geometry BuildGeometry();

        protected void ApplyStyle(Shape shape)
        {
            if (Style.Fill.HasValue)
            {
                var brush = new SolidColorBrush(Style.Fill.Value)
                {
                    Opacity = Style.FillOpacity * Style.Opacity
                };
                shape.Fill = brush;
            }
            if (Style.Stroke.HasValue && Style.StrokeWidth > 0)
            {
                var brush = new SolidColorBrush(Style.Stroke.Value)
                {
                    Opacity = Style.StrokeOpacity * Style.Opacity
                };
                shape.Stroke = brush;
                shape.StrokeThickness = Style.StrokeWidth;
            }
        }
    }

    public class RectShape : ParsedShape
    {
        public double X, Y, Width, Height, Rx, Ry;
        protected override Geometry BuildGeometry()
        {
            if (Width <= 0 || Height <= 0) return null;
            return new RectangleGeometry(new Rect(X, Y, Width, Height), Rx, Ry);
        }
    }

    public class CircleShape : ParsedShape
    {
        public double Cx, Cy, R;
        protected override Geometry BuildGeometry()
        {
            if (R <= 0) return null;
            return new EllipseGeometry(new Point(Cx, Cy), R, R);
        }
    }

    public class EllipseShape : ParsedShape
    {
        public double Cx, Cy, Rx, Ry;
        protected override Geometry BuildGeometry()
        {
            if (Rx <= 0 || Ry <= 0) return null;
            return new EllipseGeometry(new Point(Cx, Cy), Rx, Ry);
        }
    }

    public class LineShape : ParsedShape
    {
        public double X1, Y1, X2, Y2;
        protected override Geometry BuildGeometry()
        {
            return new LineGeometry(new Point(X1, Y1), new Point(X2, Y2));
        }
    }

    public class PolyShape : ParsedShape
    {
        public List<Point> Points { get; set; } = new List<Point>();
        public bool Closed { get; set; }
        protected override Geometry BuildGeometry()
        {
            if (Points.Count < 2) return null;
            var figure = new PathFigure { StartPoint = Points[0], IsClosed = Closed };
            for (int i = 1; i < Points.Count; i++)
                figure.Segments.Add(new LineSegment(Points[i], true));
            return new PathGeometry(new[] { figure });
        }
    }

    public class PathShape : ParsedShape
    {
        public string D { get; set; }
        protected override Geometry BuildGeometry()
        {
            if (string.IsNullOrWhiteSpace(D)) return null;
            try
            {
                return Geometry.Parse(D);
            }
            catch
            {
                return null;
            }
        }
    }

    public class TextShape : ParsedShape
    {
        public string Text;
        public double X, Y;

        protected override Geometry BuildGeometry()
        {
            if (string.IsNullOrEmpty(Text)) return null;
            var fontFamily = new FontFamily(string.IsNullOrEmpty(Style.FontFamily)
                ? "Segoe UI" : Style.FontFamily);
            var typeface = new Typeface(fontFamily, Style.FontStyle, Style.FontWeight, FontStretches.Normal);
#pragma warning disable CS0618
            var formatted = new FormattedText(
                Text,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                typeface,
                Style.FontSize,
                Brushes.Black);
#pragma warning restore CS0618
            return formatted.BuildGeometry(new Point(X, Y - Style.FontSize));
        }
    }
}
