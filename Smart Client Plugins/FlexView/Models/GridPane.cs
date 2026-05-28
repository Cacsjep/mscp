using System;

namespace FlexView.Models
{
    public class GridPane
    {
        public int Id { get; set; }
        public int Col { get; set; }
        public int Row { get; set; }
        public int ColSpan { get; set; } = 1;
        public int RowSpan { get; set; } = 1;

        // Edit mode: original slot index in the view (-1 = new pane)
        public int OriginalSlotIndex { get; set; } = -1;

        // Camera info (populated when editing existing views)
        public Guid? CameraId { get; set; }
        public string CameraName { get; set; }

        // Non-camera view-item plugin name (populated when editing existing
        // views). e.g. "Metadata Display" for slots whose ViewItemId resolves
        // to a registered ViewItemPlugin. Empty for built-in view items
        // (Camera, Hotspot, etc.) since those are not in the plugin registry.
        public string PluginName { get; set; }

        public string Label => CameraName ?? PluginName ?? $"Slot {Id}";

        public bool Contains(int col, int row)
        {
            return col >= Col && col < Col + ColSpan &&
                   row >= Row && row < Row + RowSpan;
        }

        public bool Overlaps(GridPane other)
        {
            return Col < other.Col + other.ColSpan &&
                   Col + ColSpan > other.Col &&
                   Row < other.Row + other.RowSpan &&
                   Row + RowSpan > other.Row;
        }
    }
}
