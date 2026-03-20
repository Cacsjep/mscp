using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Forms;
using VideoOS.Platform;
using VideoOS.Platform.Admin;
using VideoOS.Platform.Data;

namespace Auditor.Admin
{
    public class AuditRuleItemManager : ItemManager
    {
        private AuditRuleUserControl _userControl;
        private readonly Guid _kind;

        public AuditRuleItemManager(Guid kind)
        {
            _kind = kind;
        }

        public override void Init() { }
        public override void Close() { ReleaseUserControl(); }

        #region Event Registration

        public override Collection<EventGroup> GetKnownEventGroups(CultureInfo culture)
        {
            return new Collection<EventGroup>
            {
                new EventGroup
                {
                    ID = AuditorDefinition.EventGroupId,
                    Name = "Auditor"
                }
            };
        }

        public override Collection<EventType> GetKnownEventTypes(CultureInfo culture)
        {
            var sourceKinds = new List<Guid> { AuditorDefinition.AuditRuleKindId };

            return new Collection<EventType>
            {
                new EventType
                {
                    ID = AuditorDefinition.EvtPlaybackId,
                    Message = "Audit: Playback Entry",
                    GroupID = AuditorDefinition.EventGroupId,
                    StateGroupID = AuditorDefinition.StateGroupId,
                    State = "Active",
                    DefaultSourceKind = AuditorDefinition.AuditRuleKindId,
                    SourceKinds = sourceKinds
                },
                new EventType
                {
                    ID = AuditorDefinition.EvtExportId,
                    Message = "Audit: Export Entry",
                    GroupID = AuditorDefinition.EventGroupId,
                    StateGroupID = AuditorDefinition.StateGroupId,
                    State = "Active",
                    DefaultSourceKind = AuditorDefinition.AuditRuleKindId,
                    SourceKinds = sourceKinds
                },
                new EventType
                {
                    ID = AuditorDefinition.EvtIndepPlaybackId,
                    Message = "Audit: Independent Playback",
                    GroupID = AuditorDefinition.EventGroupId,
                    StateGroupID = AuditorDefinition.StateGroupId,
                    State = "Active",
                    DefaultSourceKind = AuditorDefinition.AuditRuleKindId,
                    SourceKinds = sourceKinds
                },
            };
        }

        public override Collection<StateGroup> GetKnownStateGroups(CultureInfo culture)
        {
            return new Collection<StateGroup>
            {
                new StateGroup
                {
                    ID = AuditorDefinition.StateGroupId,
                    Name = "Audit Status",
                    States = new[] { "Idle", "Active" }
                }
            };
        }

        #endregion

        #region User Control

        public override UserControl GenerateDetailUserControl()
        {
            _userControl = new AuditRuleUserControl();
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
            if (_userControl != null)
                _userControl.FillContent(item);
        }

        public override void ClearUserControl()
        {
            CurrentItem = null;
            if (_userControl != null)
                _userControl.ClearContent();
        }

        public override bool ValidateAndSaveUserControl()
        {
            if (CurrentItem != null && _userControl != null)
            {
                var error = _userControl.ValidateInput();
                if (error != null)
                {
                    MessageBox.Show(error, "Validation",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return false;
                }

                _userControl.UpdateItem(CurrentItem);
                Configuration.Instance.SaveItemConfiguration(
                    AuditorDefinition.PluginId, CurrentItem);
            }
            return true;
        }

        #endregion

        #region Item Management

        public override string GetItemName()
        {
            if (_userControl != null)
                return _userControl.DisplayName;
            return CurrentItem?.Name ?? "Audit Rule";
        }

        public override void SetItemName(string name)
        {
            if (_userControl != null)
                _userControl.DisplayName = name;
            if (CurrentItem != null)
                CurrentItem.Name = name;
        }

        public override List<Item> GetItems()
        {
            return Configuration.Instance.GetItemConfigurations(
                AuditorDefinition.PluginId, null, _kind);
        }

        public override List<Item> GetItems(Item parentItem)
        {
            return Configuration.Instance.GetItemConfigurations(
                AuditorDefinition.PluginId, parentItem, _kind);
        }

        public override Item GetItem(FQID fqid)
        {
            return Configuration.Instance.GetItemConfiguration(
                AuditorDefinition.PluginId, _kind, fqid.ObjectId);
        }

        public override Item CreateItem(Item parentItem, FQID suggestedFQID)
        {
            CurrentItem = new Item(suggestedFQID, "New Audit Rule");
            CurrentItem.Properties["UserNames"] = "";
            CurrentItem.Properties["PromptPlayback"] = "Yes";
            CurrentItem.Properties["PromptExport"] = "Yes";
            CurrentItem.Properties["PromptIndependentPlayback"] = "Yes";
            CurrentItem.Properties["TriggerPlayback"] = "Yes";
            CurrentItem.Properties["TriggerExport"] = "Yes";
            CurrentItem.Properties["TriggerIndependentPlayback"] = "Yes";
            CurrentItem.Properties["Enabled"] = "Yes";
            CurrentItem.Properties["SpecifyCameras"] = "No";
            CurrentItem.Properties["CameraIds"] = "";
            CurrentItem.Properties["CameraNames"] = "";
            CurrentItem.Properties["PredefinedReasons"] = "";

            if (_userControl != null)
                _userControl.FillContent(CurrentItem);

            Configuration.Instance.SaveItemConfiguration(
                AuditorDefinition.PluginId, CurrentItem);
            return CurrentItem;
        }

        public override void DeleteItem(Item item)
        {
            if (item != null)
            {
                Configuration.Instance.DeleteItemConfiguration(
                    AuditorDefinition.PluginId, item);
            }
        }

        #endregion

        #region Status

        public override OperationalState GetOperationalState(Item item)
        {
            if (item == null)
                return OperationalState.Disabled;

            var enabled = !item.Properties.ContainsKey("Enabled") || item.Properties["Enabled"] != "No";
            return enabled ? OperationalState.Ok : OperationalState.Disabled;
        }

        public override string GetItemStatusDetails(Item item, string language)
        {
            if (item == null)
                return "";

            var enabled = !item.Properties.ContainsKey("Enabled") || item.Properties["Enabled"] != "No";
            if (!enabled)
                return "Disabled";

            var userNames = item.Properties.ContainsKey("UserNames") ? item.Properties["UserNames"] : "";
            return string.IsNullOrEmpty(userNames) ? "No users configured" : $"Monitoring: {userNames}";
        }

        #endregion
    }
}
