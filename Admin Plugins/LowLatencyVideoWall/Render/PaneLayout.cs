using System;
using System.Collections.Generic;
using LowLatencyVideoWall.Model;

namespace LowLatencyVideoWall.Render
{
    /// <summary>
    /// A destination rectangle on the wall in real backbuffer pixels.
    ///
    /// Kept separate from System.Windows.Rect and from RawRectangle on purpose: WPF measures
    /// in device independent units and a rounding error there is a visible seam, so the
    /// renderer only ever deals in integers it computed itself.
    /// </summary>
    public readonly struct PixelRect : IEquatable<PixelRect>
    {
        public PixelRect(int left, int top, int width, int height)
        {
            Left = left;
            Top = top;
            Width = width;
            Height = height;
        }

        public int Left { get; }
        public int Top { get; }
        public int Width { get; }
        public int Height { get; }

        public int Right => Left + Width;
        public int Bottom => Top + Height;

        public bool IsEmpty => Width <= 0 || Height <= 0;

        public static PixelRect Empty => default(PixelRect);

        public bool Equals(PixelRect other)
            => Left == other.Left && Top == other.Top && Width == other.Width && Height == other.Height;

        public override bool Equals(object obj) => obj is PixelRect other && Equals(other);

        public override int GetHashCode()
            => (((Left * 397) ^ Top) * 397 ^ Width) * 397 ^ Height;

        public override string ToString() => $"{Width}x{Height}+{Left}+{Top}";
    }

    /// <summary>
    /// Turns the 0 to 1000 normalised geometry a <see cref="PaneDefinition"/> carries into
    /// pixel rectangles for a given wall size.
    ///
    /// The whole job is the rounding. Panes are stored as origin plus size, but converting
    /// origin and size independently is what produces one pixel seams and one pixel overlaps:
    /// two panes that share a normalised edge end up disagreeing about where that edge lands.
    /// So edges are converted, not sizes. Both the right edge of one pane and the left edge of
    /// its neighbour go through the same <see cref="Edge"/> call on the same normalised value,
    /// which can only ever give the same pixel column.
    /// </summary>
    public static class PaneLayout
    {
        /// <summary>
        /// Pixel rectangles for the panes, in the same order. A pane with no geometry, or one
        /// that falls outside the wall, comes back as <see cref="PixelRect.Empty"/> and the
        /// renderer simply does not blit it.
        ///
        /// maximisedIndex, when it is a valid index, gives that one pane the whole wall and
        /// every other pane an empty rectangle.
        /// </summary>
        public static PixelRect[] Compute(IReadOnlyList<PaneDefinition> panes,
            int wallWidth, int wallHeight, int maximisedIndex = -1)
        {
            var count = panes?.Count ?? 0;
            var result = new PixelRect[count];
            if (count == 0 || wallWidth <= 0 || wallHeight <= 0) return result;

            if (maximisedIndex >= 0 && maximisedIndex < count)
            {
                result[maximisedIndex] = new PixelRect(0, 0, wallWidth, wallHeight);
                return result;
            }

            for (var i = 0; i < count; i++)
                result[i] = ToPixels(panes[i], wallWidth, wallHeight);

            return result;
        }

        /// <summary>Same thing for panes the renderer already owns.</summary>
        public static PixelRect[] Compute(IReadOnlyList<WallPane> panes,
            int wallWidth, int wallHeight, int maximisedIndex = -1)
        {
            var count = panes?.Count ?? 0;
            var result = new PixelRect[count];
            if (count == 0 || wallWidth <= 0 || wallHeight <= 0) return result;

            if (maximisedIndex >= 0 && maximisedIndex < count)
            {
                result[maximisedIndex] = new PixelRect(0, 0, wallWidth, wallHeight);
                return result;
            }

            for (var i = 0; i < count; i++)
                result[i] = ToPixels(panes[i].Geometry, wallWidth, wallHeight);

            return result;
        }

        /// <summary>One normalised pane rectangle in pixels.</summary>
        public static PixelRect ToPixels(PaneDefinition pane, int wallWidth, int wallHeight)
        {
            if (pane == null || wallWidth <= 0 || wallHeight <= 0) return PixelRect.Empty;
            if (pane.Width <= 0 || pane.Height <= 0) return PixelRect.Empty;

            var left = Edge(pane.X, wallWidth);
            var right = Edge(pane.X + pane.Width, wallWidth);
            var top = Edge(pane.Y, wallHeight);
            var bottom = Edge(pane.Y + pane.Height, wallHeight);

            // A pane narrower than a pixel still has to be visible, otherwise a wall with a
            // badly authored view silently loses a camera. Give it one pixel, borrowed from
            // the left if it is already hard against the right edge.
            if (right <= left)
            {
                if (left >= wallWidth) left = wallWidth - 1;
                right = left + 1;
            }
            if (bottom <= top)
            {
                if (top >= wallHeight) top = wallHeight - 1;
                bottom = top + 1;
            }

            return new PixelRect(left, top, right - left, bottom - top);
        }

        /// <summary>
        /// A normalised coordinate to a pixel column or row. Rounded away from zero so the
        /// result only depends on the coordinate, never on which pane asked: that is what
        /// makes shared edges agree.
        /// </summary>
        private static int Edge(int normalised, int size)
        {
            if (normalised <= 0) return 0;
            if (normalised >= PaneDefinition.Scale) return size;

            var pixels = (int)Math.Round(normalised * (double)size / PaneDefinition.Scale,
                MidpointRounding.AwayFromZero);
            return pixels < 0 ? 0 : pixels > size ? size : pixels;
        }

        /// <summary>
        /// Gives every pane that carries no geometry a cell in an even grid, so a view profile
        /// authored by dropping cameras in without arranging them still renders.
        ///
        /// Normalised, so it survives being saved and re-opened on a differently sized wall.
        /// Panes that already have geometry are left exactly as they are.
        /// </summary>
        public static void AssignAutoGrid(IList<PaneDefinition> panes)
        {
            if (panes == null || panes.Count == 0) return;

            var columns = (int)Math.Ceiling(Math.Sqrt(panes.Count));
            if (columns < 1) columns = 1;
            var rows = (int)Math.Ceiling(panes.Count / (double)columns);

            for (var i = 0; i < panes.Count; i++)
            {
                var pane = panes[i];
                if (pane == null || (pane.Width > 0 && pane.Height > 0)) continue;

                var column = i % columns;
                var row = i / columns;

                // Edges again rather than origin plus size, for the same reason: adjacent
                // cells have to land on the same normalised boundary.
                var x0 = column * PaneDefinition.Scale / columns;
                var x1 = (column + 1) * PaneDefinition.Scale / columns;
                var y0 = row * PaneDefinition.Scale / rows;
                var y1 = (row + 1) * PaneDefinition.Scale / rows;

                pane.X = x0;
                pane.Y = y0;
                pane.Width = x1 - x0;
                pane.Height = y1 - y0;
            }
        }
    }
}
