using System;
using System.Collections.Generic;
using System.Windows.Forms;
using VideoOS.Platform;
using VideoOS.Platform.Admin;

namespace AutoExporter.Admin
{
    public class ExecutionsItemManager : ItemManager
    {
        private DashboardUserControl _userControl;
        private readonly Guid _kind;

        public ExecutionsItemManager(Guid kind) { _kind = kind; }

        public override void Init() { }
        public override void Close() { ReleaseUserControl(); }

        public override UserControl GenerateDetailUserControl()
        {
            _userControl = new DashboardUserControl();
            return _userControl;
        }

        public override void ReleaseUserControl()
        {
            try { _userControl?.Shutdown(); } catch { }
            _userControl = null;
        }

        public override void FillUserControl(Item item)
        {
            CurrentItem = item;
            _userControl?.FillContent(item);
        }

        public override void ClearUserControl()
        {
            CurrentItem = null;
            _userControl?.ClearContent();
        }

        public override bool ValidateAndSaveUserControl() { return true; }

        public override string GetItemName() => "Status and Executions";
        public override void SetItemName(string name) { if (CurrentItem != null) CurrentItem.Name = "Status and Executions"; }

        public override List<Item> GetItems()
            => Configuration.Instance.GetItemConfigurations(AutoExporterDefinition.PluginId, null, _kind);

        public override List<Item> GetItems(Item parentItem)
            => Configuration.Instance.GetItemConfigurations(AutoExporterDefinition.PluginId, parentItem, _kind);

        public override Item GetItem(FQID fqid)
            => Configuration.Instance.GetItemConfiguration(AutoExporterDefinition.PluginId, _kind, fqid.ObjectId);

        public override Item CreateItem(Item parentItem, FQID suggestedFQID)
        {
            var fqid = new FQID(
                suggestedFQID.ServerId,
                suggestedFQID.ParentId,
                AutoExporterDefinition.ExecutionsSingletonId,
                FolderType.No,
                _kind);

            CurrentItem = new Item(fqid, "Status and Executions");
            Configuration.Instance.SaveItemConfiguration(AutoExporterDefinition.PluginId, CurrentItem);
            return CurrentItem;
        }

        public override void DeleteItem(Item item)
        {
            if (item != null)
                Configuration.Instance.DeleteItemConfiguration(AutoExporterDefinition.PluginId, item);
        }

        public override OperationalState GetOperationalState(Item item) => OperationalState.Ok;
        public override string GetItemStatusDetails(Item item, string language) => "Auto export run history";
    }
}
