using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Forms;
using VideoOS.Platform;
using VideoOS.Platform.Admin;
using VideoOS.Platform.Messaging;

namespace RTMPStreamer.Admin
{
    public class RTMPStreamerItemManager : ItemManager
    {
        private StreamConfigUserControl _userControl;
        private readonly Guid _kind;

        public RTMPStreamerItemManager(Guid kind)
        {
            _kind = kind;
        }

        public override void Init()
        {
            // Pre-start MessageCommunication on a background thread so it doesn't
            // block the UI when the user first clicks a stream item.
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

        public override void Close()
        {
            ReleaseUserControl();
        }

        #region User Control

        public override UserControl GenerateDetailUserControl()
        {
            _userControl = new StreamConfigUserControl();
            _userControl.ConfigurationChangedByUser += new EventHandler(ConfigurationChangedByUserHandler);
            return _userControl;
        }

        public override void ReleaseUserControl()
        {
            if (_userControl != null)
            {
                _userControl.ConfigurationChangedByUser -= new EventHandler(ConfigurationChangedByUserHandler);
                _userControl.UnsubscribeLiveStatus();
            }
            _userControl = null;
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
                    System.Windows.Forms.MessageBox.Show(error, "Validation",
                        System.Windows.Forms.MessageBoxButtons.OK,
                        System.Windows.Forms.MessageBoxIcon.Warning);
                    return false;
                }

                _userControl.UpdateItem(CurrentItem);
                Configuration.Instance.SaveItemConfiguration(RTMPStreamerDefinition.PluginId, CurrentItem);
            }
            return true;
        }

        #endregion

        #region Status

        public override OperationalState GetOperationalState(Item item)
        {
            if (item == null)
                return OperationalState.Disabled;

            var enabled = !item.Properties.ContainsKey("Enabled") || item.Properties["Enabled"] != "No";
            if (!enabled)
                return OperationalState.Disabled;

            return OperationalState.Ok;
        }

        public override string GetItemStatusDetails(Item item, string language)
        {
            if (item == null)
                return "";

            var enabled = !item.Properties.ContainsKey("Enabled") || item.Properties["Enabled"] != "No";
            if (!enabled)
                return "Disabled";

            return "See Live Status panel";
        }

        #endregion

        #region Item Management

        public override string GetItemName()
        {
            if (_userControl != null)
                return _userControl.DisplayName;
            return "";
        }

        public override void SetItemName(string name)
        {
            if (CurrentItem != null)
                CurrentItem.Name = name;
        }

        public override List<Item> GetItems()
        {
            return Configuration.Instance.GetItemConfigurations(
                RTMPStreamerDefinition.PluginId, null, _kind);
        }

        public override List<Item> GetItems(Item parentItem)
        {
            return Configuration.Instance.GetItemConfigurations(
                RTMPStreamerDefinition.PluginId, parentItem, _kind);
        }

        public override Item GetItem(FQID fqid)
        {
            return Configuration.Instance.GetItemConfiguration(
                RTMPStreamerDefinition.PluginId, _kind, fqid.ObjectId);
        }

        public override Item CreateItem(Item parentItem, FQID suggestedFQID)
        {
            CurrentItem = new Item(suggestedFQID, "New RTMP Stream");
            CurrentItem.Properties["Enabled"] = "Yes";
            CurrentItem.Properties["RtmpUrl"] = "rtmp://";

            if (_userControl != null)
                _userControl.FillContent(CurrentItem);

            Configuration.Instance.SaveItemConfiguration(RTMPStreamerDefinition.PluginId, CurrentItem);
            return CurrentItem;
        }

        public override void DeleteItem(Item item)
        {
            if (item != null)
            {
                Configuration.Instance.DeleteItemConfiguration(RTMPStreamerDefinition.PluginId, item);
            }
        }

        #endregion
    }
}
