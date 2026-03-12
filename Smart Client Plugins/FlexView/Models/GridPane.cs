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

        public string Label => CameraName ?? $"Slot {Id}";

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
