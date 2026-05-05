using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Forms;
using VideoOS.Platform;
using VideoOS.Platform.Admin;
using VideoOS.Platform.Messaging;

namespace BarcodeReader.Admin
{
    public class BarcodeChannelItemManager : ItemManager
    {
        private ChannelConfigUserControl _userControl;
        private readonly Guid _kind;

        public BarcodeChannelItemManager(Guid kind) { _kind = kind; }

        public override void Init()
        {
            // Pre-warm MessageCommunication so the first stream item click doesn't block the UI.
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    var serverId = EnvironmentManager.Instance.MasterSite.ServerId;
                    MessageCommunicationManager.Start(serverId);
                }
                catch { }
            });
        }

        public override void Close() => ReleaseUserControl();

        public override UserControl GenerateDetailUserControl()
        {
            // Management Client occasionally re-creates the detail panel (e.g. after a
            // ConfigurationChangedIndication tree refresh) without first calling
            // ReleaseUserControl. The previous control would then leak its MessageBroker
            // filter registrations for StatusUpdate/StatusResponse, and the new control's
            // fresh registration would trip "Same messageId being registered multiple times".
            ReleaseUserControl();

            _userControl = new ChannelConfigUserControl();
            _userControl.ConfigurationChangedByUser += ConfigurationChangedByUserHandler;
            return _userControl;
        }

        public override void ReleaseUserControl()
        {
            if (_userControl != null)
            {
                _userControl.ConfigurationChangedByUser -= ConfigurationChangedByUserHandler;
                try { _userControl.UnsubscribeLiveStatus(); } catch { }
                try { _userControl.Dispose(); } catch { }
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
                Configuration.Instance.SaveItemConfiguration(BarcodeReaderDefinition.PluginId, CurrentItem);
            }
            return true;
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
            return enabled ? "See Live Status panel" : "Disabled";
        }

        public override string GetItemName() => _userControl?.DisplayName ?? "";
        public override void SetItemName(string name) { if (CurrentItem != null) CurrentItem.Name = name; }

        public override List<Item> GetItems()
            => Configuration.Instance.GetItemConfigurations(BarcodeReaderDefinition.PluginId, null, _kind);

        public override List<Item> GetItems(Item parentItem)
            => Configuration.Instance.GetItemConfigurations(BarcodeReaderDefinition.PluginId, parentItem, _kind);

        public override Item GetItem(FQID fqid)
            => Configuration.Instance.GetItemConfiguration(BarcodeReaderDefinition.PluginId, _kind, fqid.ObjectId);

        public override Item CreateItem(Item parentItem, FQID suggestedFQID)
        {
            CurrentItem = new Item(suggestedFQID, "New Barcode Channel");
            CurrentItem.Properties["Enabled"] = "Yes";
            CurrentItem.Properties["Formats"] = Background.ChannelConfig.DefaultFormats;
            CurrentItem.Properties["TryHarder"] = "Yes";
            CurrentItem.Properties["AutoRotate"] = "Yes";
            CurrentItem.Properties["TryInverted"] = "No";
            CurrentItem.Properties["TargetFps"] = Background.ChannelConfig.DefaultTargetFps.ToString();
            CurrentItem.Properties["DownscaleWidth"] = Background.ChannelConfig.DefaultDownscaleWidth.ToString();
            CurrentItem.Properties["DebounceMs"] = Background.ChannelConfig.DefaultDebounceMs.ToString();
            CurrentItem.Properties["CreateBookmarks"] = "Yes";

            _userControl?.FillContent(CurrentItem);

            Configuration.Instance.SaveItemConfiguration(BarcodeReaderDefinition.PluginId, CurrentItem);
            return CurrentItem;
        }

        public override void DeleteItem(Item item)
        {
            if (item != null)
                Configuration.Instance.DeleteItemConfiguration(BarcodeReaderDefinition.PluginId, item);
        }
    }
}
