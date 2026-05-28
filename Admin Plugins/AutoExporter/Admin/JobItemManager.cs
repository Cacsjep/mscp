using System;
using System.Collections.Generic;
using System.Windows.Forms;
using VideoOS.Platform;
using VideoOS.Platform.Admin;

namespace AutoExporter.Admin
{
    public class JobItemManager : ItemManager
    {
        private JobUserControl _userControl;
        private readonly Guid _kind;

        public JobItemManager(Guid kind) { _kind = kind; }

        public override void Init() { }
        public override void Close() { ReleaseUserControl(); }

        public override UserControl GenerateDetailUserControl()
        {
            _userControl = new JobUserControl();
            _userControl.ConfigurationChangedByUser += ConfigurationChangedByUserHandler;
            _userControl.DuplicateRequested += OnDuplicateRequested;
            return _userControl;
        }

        public override void ReleaseUserControl()
        {
            if (_userControl != null)
            {
                _userControl.ConfigurationChangedByUser -= ConfigurationChangedByUserHandler;
                _userControl.DuplicateRequested -= OnDuplicateRequested;
            }
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

        public override bool ValidateAndSaveUserControl()
        {
            if (CurrentItem == null || _userControl == null) return true;

            var error = _userControl.ValidateInput();
            if (error != null)
            {
                MessageBox.Show(error, "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            _userControl.UpdateItem(CurrentItem);
            Configuration.Instance.SaveItemConfiguration(AutoExporterDefinition.PluginId, CurrentItem);
            return true;
        }

        public override string GetItemName() => _userControl?.DisplayName ?? CurrentItem?.Name ?? "Job";

        public override void SetItemName(string name)
        {
            if (_userControl != null) _userControl.DisplayName = name;
            if (CurrentItem != null) CurrentItem.Name = name;
        }

        public override List<Item> GetItems()
            => Configuration.Instance.GetItemConfigurations(AutoExporterDefinition.PluginId, null, _kind);

        public override List<Item> GetItems(Item parentItem)
            => Configuration.Instance.GetItemConfigurations(AutoExporterDefinition.PluginId, parentItem, _kind);

        public override Item GetItem(FQID fqid)
            => Configuration.Instance.GetItemConfiguration(AutoExporterDefinition.PluginId, _kind, fqid.ObjectId);

        public override Item CreateItem(Item parentItem, FQID suggestedFQID)
        {
            CurrentItem = new Item(suggestedFQID, "New Job");
            CurrentItem.Properties["Format"]        = "XProtect";
            CurrentItem.Properties["Encrypt"]       = "No";
            CurrentItem.Properties["Password"]      = "";
            CurrentItem.Properties["RangeValue"]    = "1";
            CurrentItem.Properties["RangeUnit"]     = "Days";
            CurrentItem.Properties["IncludePlayer"] = "Yes";
            CurrentItem.Properties["IncludeAudio"]  = "Yes";
            CurrentItem.Properties["Enabled"]       = "Yes";
            CurrentItem.Properties["Targets_Count"] = "0";
            CurrentItem.Properties["StoragePath"]   = "";
            CurrentItem.Properties["MaxGB"]         = "0";
            CurrentItem.Properties["MaxAgeDays"]    = "0";

            _userControl?.FillContent(CurrentItem);
            Configuration.Instance.SaveItemConfiguration(AutoExporterDefinition.PluginId, CurrentItem);
            return CurrentItem;
        }

        public override void DeleteItem(Item item)
        {
            if (item != null)
                Configuration.Instance.DeleteItemConfiguration(AutoExporterDefinition.PluginId, item);
        }

        public override OperationalState GetOperationalState(Item item)
        {
            if (item == null) return OperationalState.Disabled;
            var enabled = !item.Properties.ContainsKey("Enabled") || item.Properties["Enabled"] != "No";
            return enabled ? OperationalState.Ok : OperationalState.Disabled;
        }

        public override string GetItemStatusDetails(Item item, string language)
        {
            if (item == null) return "";
            var enabled = !item.Properties.ContainsKey("Enabled") || item.Properties["Enabled"] != "No";
            if (!enabled) return "Disabled";

            var format = item.Properties.ContainsKey("Format") ? item.Properties["Format"] : "XProtect";
            var val    = item.Properties.ContainsKey("RangeValue") ? item.Properties["RangeValue"] : "?";
            var unit   = item.Properties.ContainsKey("RangeUnit") ? item.Properties["RangeUnit"] : "?";
            return $"{format} • Last {val} {unit.ToLowerInvariant()}";
        }

        private void OnDuplicateRequested(object sender, EventArgs e)
        {
            if (CurrentItem == null) return;

            var src = CurrentItem;
            var newFqid = new FQID(
                src.FQID.ServerId,
                src.FQID.ParentId,
                Guid.NewGuid(),
                FolderType.No,
                _kind);

            var newItem = new Item(newFqid, "Copy of " + src.Name);
            foreach (var kvp in src.Properties)
                newItem.Properties[kvp.Key] = kvp.Value;

            Configuration.Instance.SaveItemConfiguration(AutoExporterDefinition.PluginId, newItem);
            MessageBox.Show(
                $"Created \"{newItem.Name}\".\n\nCollapse/expand \"Jobs\" to see it.",
                "Duplicated", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
