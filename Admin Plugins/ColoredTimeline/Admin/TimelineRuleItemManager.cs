using CommunitySDK;
using System;
using System.Collections.Generic;
using System.Windows.Forms;
using VideoOS.Platform;
using VideoOS.Platform.Admin;

namespace ColoredTimeline.Admin
{
    public class TimelineRuleItemManager : ItemManager
    {
        private TimelineRuleUserControl _userControl;
        private readonly Guid _kind;
        private readonly PluginLog _log = new PluginLog("ColoredTimeline - ItemManager");

        public TimelineRuleItemManager(Guid kind)
        {
            _kind = kind;
        }

        public override void Init() { }

        public override void Close() { ReleaseUserControl(); }

        public override UserControl GenerateDetailUserControl()
        {
            _userControl = new TimelineRuleUserControl();
            _userControl.ConfigurationChangedByUser += ConfigurationChangedByUserHandler;
            return _userControl;
        }

        public override void ReleaseUserControl()
        {
            if (_userControl != null)
            {
                _userControl.ConfigurationChangedByUser -= ConfigurationChangedByUserHandler;
                _userControl = null;
            }
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
            Configuration.Instance.SaveItemConfiguration(ColoredTimelineDefinition.PluginId, CurrentItem);
            return true;
        }

        public override string GetItemName() => _userControl?.DisplayName ?? CurrentItem?.Name ?? "Timeline Rule";

        public override void SetItemName(string name)
        {
            if (_userControl != null) _userControl.DisplayName = name;
            if (CurrentItem != null) CurrentItem.Name = name;
        }

        public override List<Item> GetItems()
            => Configuration.Instance.GetItemConfigurations(ColoredTimelineDefinition.PluginId, null, _kind);

        public override List<Item> GetItems(Item parentItem)
            => Configuration.Instance.GetItemConfigurations(ColoredTimelineDefinition.PluginId, parentItem, _kind);

        public override Item GetItem(FQID fqid)
            => Configuration.Instance.GetItemConfiguration(ColoredTimelineDefinition.PluginId, _kind, fqid.ObjectId);

        public override Item CreateItem(Item parentItem, FQID suggestedFQID)
        {
            CurrentItem = new Item(suggestedFQID, "New Timeline Rule");
            CurrentItem.Properties["Enabled"] = "Yes";
            CurrentItem.Properties["RibbonColor"] = "#1E88E5";
            CurrentItem.Properties["StartEvent"] = "";
            CurrentItem.Properties["StopEvent"] = "";
            CurrentItem.Properties["CameraIds"] = "";
            CurrentItem.Properties["CameraNames"] = "";

            _userControl?.FillContent(CurrentItem);
            Configuration.Instance.SaveItemConfiguration(ColoredTimelineDefinition.PluginId, CurrentItem);
            return CurrentItem;
        }

        public override void DeleteItem(Item item)
        {
            if (item != null)
                Configuration.Instance.DeleteItemConfiguration(ColoredTimelineDefinition.PluginId, item);
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
            var camIds = item.Properties.ContainsKey("CameraIds") ? item.Properties["CameraIds"] : "";
            var count = string.IsNullOrEmpty(camIds) ? 0 : camIds.Split(';').Length;
            return count == 0 ? "No cameras" : $"{count} camera(s)";
        }
    }
}
