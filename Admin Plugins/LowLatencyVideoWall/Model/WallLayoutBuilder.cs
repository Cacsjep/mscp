using System;
using System.Collections.Generic;
using System.Linq;

namespace LowLatencyVideoWall.Model
{
    /// <summary>
    /// Builds a wall layout that nobody designed in the Management Client.
    ///
    /// A rule can push cameras straight onto a wall, and at that moment there is no stored
    /// view to show: the arrangement has to be invented from the number of cameras. The
    /// result is a perfectly ordinary <see cref="ViewProfile"/> that is never saved, so the
    /// renderer, the session builder and the resolution picker all treat it exactly like a
    /// stored one.
    /// </summary>
    internal static class WallLayoutBuilder
    {
        /// <summary>
        /// A camera as a rule action hands it over: the configuration item's object id, which
        /// is the same Guid a WallCamera carries, plus its name for the pane title.
        /// </summary>
        internal struct CameraRef
        {
            internal Guid Id;
            internal string Name;

            internal CameraRef(Guid id, string name)
            {
                Id = id;
                Name = name ?? "";
            }
        }

        /// <summary>
        /// The squarest grid that holds <paramref name="count"/> panes: 1 is 1x1, 4 is 2x2,
        /// 9 is 3x3, and the in between counts keep the columns and fill the last row short
        /// rather than stretching one pane across it. 6 is therefore 3x2 with no gap, and 5
        /// is 3x2 with one empty cell at the end.
        /// </summary>
        internal static void GridFor(int count, out int columns, out int rows)
        {
            if (count < 1) count = 1;
            columns = (int)Math.Ceiling(Math.Sqrt(count));
            rows = (int)Math.Ceiling(count / (double)columns);
        }

        /// <summary>
        /// A transient view holding exactly these cameras, laid out in the best fit grid, in
        /// the order the rule listed them.
        /// </summary>
        internal static ViewProfile Grid(IList<CameraRef> cameras, string name)
        {
            var profile = new ViewProfile { Id = Guid.NewGuid(), Name = name ?? "Rule" };
            if (cameras == null || cameras.Count == 0) return profile;

            GridFor(cameras.Count, out var columns, out var rows);

            for (var i = 0; i < cameras.Count; i++)
                profile.Panes.Add(Cell(i, columns, rows, cameras[i]));

            return profile;
        }

        /// <summary>
        /// Puts one camera in one numbered position, keeping everything else on the wall.
        ///
        /// Positions are numbered from one, left to right and then top to bottom, which is how
        /// an operator reads a wall and the only numbering a rule author can be expected to
        /// guess. Asking for a position the wall does not have grows the grid to the smallest
        /// one that does, carrying the existing cameras over in their current order, so
        /// "camera on position 7" works on a 2x2 wall instead of being silently dropped.
        /// </summary>
        internal static ViewProfile PlaceAt(ViewProfile current, int position, CameraRef camera)
        {
            if (position < 1) position = 1;

            var existing = current?.Panes
                               .Select(p => new CameraRef(p.CameraId, p.CameraName))
                               .ToList()
                           ?? new List<CameraRef>();

            // One past the end is an append, which is what a rule author means by "the next
            // free position" and costs nothing to support.
            var slots = Math.Max(existing.Count, position);
            while (existing.Count < slots) existing.Add(new CameraRef(Guid.Empty, ""));

            existing[position - 1] = camera;

            var profile = Grid(existing, current?.Name ?? "Rule");

            // A layout the rule did not design should not claim to be the stored view it grew
            // out of, or a later edit of that view would look like it had been applied here.
            profile.Id = Guid.NewGuid();
            return profile;
        }

        private static PaneDefinition Cell(int index, int columns, int rows, CameraRef camera)
        {
            var column = index % columns;
            var row = index / columns;

            return new PaneDefinition
            {
                CameraId = camera.Id,
                CameraName = camera.Name,
                X = column * (PaneDefinition.Scale / columns),
                Y = row * (PaneDefinition.Scale / rows),
                Width = PaneDefinition.Scale / columns,
                Height = PaneDefinition.Scale / rows
            };
        }
    }
}
