using System;
using System.Collections.Generic;
using System.Windows.Forms;
using VideoOS.Platform;
using VideoOS.Platform.Admin;

namespace LowLatencyVideoWall.Admin
{
    /// <summary>
    /// The camera overview node. It owns no configuration items of its own: the table is a
    /// live read of the Milestone configuration plus what every camera reports over VAPIX,
    /// so every CRUD override here is a no-op and the page arrives through
    /// GenerateOverviewUserControl, which is what the Management Client calls for a node
    /// declared ItemsAllowed.None.
    /// </summary>
    public class OverviewItemManager : ItemManager
    {
        private HostedNodeUserControl _hosted;

        public override void Init() { }

        public override void Close() { ReleaseUserControl(); }

        public override ItemNodeUserControl GenerateOverviewUserControl()
        {
            try
            {
                _hosted = new HostedNodeUserControl(new OverviewUserControl());
                return _hosted;
            }
            catch (Exception ex)
            {
                LowLatencyVideoWallDefinition.Log.Error($"Building the camera overview failed: {ex}");
                return new HostedNodeUserControl(AdminUi.ErrorPage(
                    "The camera overview could not be built.", ex));
            }
        }

        public override void ReleaseUserControl()
        {
            _hosted = null;
        }

        // ---- no items live under this node ----------------------------------------------

        public override List<Item> GetItems() => new List<Item>();
        public override List<Item> GetItems(Item parentItem) => new List<Item>();
        public override Item GetItem(FQID fqid) => null;
        public override Item CreateItem(Item parentItem, FQID suggestedFQID) => null;
        public override void DeleteItem(Item item) { }

        public override UserControl GenerateDetailUserControl() => null;
        public override void FillUserControl(Item item) { }
        public override void ClearUserControl() { }
        public override bool ValidateAndSaveUserControl() => true;
    }
}
