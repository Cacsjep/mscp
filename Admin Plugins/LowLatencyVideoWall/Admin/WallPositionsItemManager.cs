using System;
using System.Collections.Generic;
using System.Globalization;
using VideoOS.Platform;
using VideoOS.Platform.Admin;

namespace LowLatencyVideoWall.Admin
{
    /// <summary>
    /// The numbered pane positions a rule can target, as configuration items.
    ///
    /// This exists purely to get a number into a rule. An ActionDefinition has two inputs and
    /// both are item pickers: there is no text box and no spinner anywhere in the rule wizard,
    /// so "show this camera at position 7" can only be expressed by making position 7 an item
    /// that can be picked. They are generated rather than stored, because a position is not a
    /// thing an administrator should have to create, maintain or be able to delete.
    ///
    /// The object id is derived from the number, so a rule saved today still means position 7
    /// after a reinstall, a restore or a move to another server.
    /// </summary>
    internal sealed class WallPositionsItemManager : ItemManager
    {
        /// <summary>
        /// The most panes the wall supports, which is what bounds the list. Offering more
        /// positions than the renderer will ever lay out would only invite rules that cannot
        /// be satisfied.
        /// </summary>
        internal const int MaxPositions = 48;

        private readonly Guid _kind;

        internal WallPositionsItemManager(Guid kind)
        {
            _kind = kind;
        }

        /// <summary>
        /// Position number to item id. The number goes in the first field of the Guid, so the
        /// ids are stable, readable in a log, and trivially reversible.
        /// </summary>
        internal static Guid IdFor(int position)
            => new Guid(position, 0, 0, new byte[8]);

        /// <summary>The position an item id means, or zero if it is not one of ours.</summary>
        internal static int PositionOf(Guid id)
        {
            var bytes = id.ToByteArray();
            for (var i = 4; i < bytes.Length; i++)
                if (bytes[i] != 0) return 0;

            var position = BitConverter.ToInt32(bytes, 0);
            return position >= 1 && position <= MaxPositions ? position : 0;
        }

        public override void Init() { }

        public override void Close() { }

        public override List<Item> GetItems()
        {
            var items = new List<Item>(MaxPositions);

            for (var position = 1; position <= MaxPositions; position++)
            {
                var fqid = new FQID
                {
                    ServerId = EnvironmentManager.Instance.MasterSite.ServerId,
                    ParentId = Guid.Empty,
                    ObjectId = IdFor(position),
                    Kind = _kind,
                    FolderType = FolderType.No
                };

                items.Add(new Item(fqid,
                    "Position " + position.ToString(CultureInfo.CurrentCulture)));
            }

            return items;
        }

        public override List<Item> GetItems(Item parentItem) => GetItems();

        public override Item GetItem(FQID fqid)
        {
            var position = fqid == null ? 0 : PositionOf(fqid.ObjectId);
            if (position == 0) return null;

            return new Item(fqid, "Position " + position.ToString(CultureInfo.CurrentCulture));
        }

        public override string GetItemStatusDetails(Item item, string language)
        {
            var position = item?.FQID == null ? 0 : PositionOf(item.FQID.ObjectId);
            if (position == 0) return "";

            WallLayoutHint(position, out var columns, out var rows, out var column, out var row);
            return $"Position {position} is column {column} of {columns}, row {row} of {rows} " +
                   "on a wall of that many panes";
        }

        /// <summary>
        /// Where a position lands on the smallest grid that contains it, which is what the
        /// client does with it. Only here so the Management Client can explain the number
        /// instead of leaving the administrator to count panes.
        /// </summary>
        private static void WallLayoutHint(int position, out int columns, out int rows,
            out int column, out int row)
        {
            Model.WallLayoutBuilder.GridFor(position, out columns, out rows);
            column = (position - 1) % columns + 1;
            row = (position - 1) / columns + 1;
        }
    }
}
