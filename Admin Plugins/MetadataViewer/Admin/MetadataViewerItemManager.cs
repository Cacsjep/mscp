using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Forms;
using VideoOS.Platform;
using VideoOS.Platform.Admin;
using VideoOS.Platform.Messaging;

namespace MetadataViewer.Admin
{
    public class MetadataViewerItemManager : ItemManager
    {
        private MetadataViewerUserControl _userControl;
        private readonly Guid _kind;

        public MetadataViewerItemManager(Guid kind) { _kind = kind; }

        public override void Init()
        {
            // MetadataSupplier relies on the platform MessageCommunication session
            // being active. In Smart Client it's auto-started; in Admin Client we
            // bootstrap it explicitly or the live content callback never fires.
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
        public override void Close() { ReleaseUserControl(); }

        public override UserControl GenerateDetailUserControl()
        {
            // Defensive: if the platform didn't call ReleaseUserControl before asking
            // for a new one, tear down the previous instance so the MetadataLiveSource
            // inside it doesn't keep the orphaned control alive via its event delegate.
            ReleaseUserControl();

            _userControl = new MetadataViewerUserControl();
            _userControl.ConfigurationChangedByUser += ConfigurationChangedByUserHandler;
            return _userControl;
        }

        public override void ReleaseUserControl()
        {
            if (_userControl != null)
            {
                _userControl.ConfigurationChangedByUser -= ConfigurationChangedByUserHandler;
                // Let the platform dispose — we just stop the live stream so events
                // don't keep firing into a stale handle after we drop our reference.
                _userControl.StopStreamForRelease();
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
            if (CurrentItem != null && _userControl != null)
            {
                var error = _userControl.ValidateInput();
                if (error != null)
                {
                    MessageBox.Show(error, "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return false;
                }
                _userControl.UpdateItem(CurrentItem);
                Configuration.Instance.SaveItemConfiguration(MetadataViewerDefinition.PluginId, CurrentItem);
            }
            return true;
        }

        public override string GetItemName() => _userControl?.DisplayName ?? CurrentItem?.Name ?? "Metadata Viewer";

        public override void SetItemName(string name)
        {
            if (CurrentItem != null) CurrentItem.Name = name;
        }

        public override List<Item> GetItems()
            => Configuration.Instance.GetItemConfigurations(MetadataViewerDefinition.PluginId, null, _kind);

        public override List<Item> GetItems(Item parentItem)
            => Configuration.Instance.GetItemConfigurations(MetadataViewerDefinition.PluginId, parentItem, _kind);

        public override Item GetItem(FQID fqid)
            => Configuration.Instance.GetItemConfiguration(MetadataViewerDefinition.PluginId, _kind, fqid.ObjectId);

        public override Item CreateItem(Item parentItem, FQID suggestedFQID)
        {
            CurrentItem = new Item(suggestedFQID, "Metadata Viewer");
            Configuration.Instance.SaveItemConfiguration(MetadataViewerDefinition.PluginId, CurrentItem);
            return CurrentItem;
        }

        public override void DeleteItem(Item item)
        {
            if (item != null)
                Configuration.Instance.DeleteItemConfiguration(MetadataViewerDefinition.PluginId, item);
        }

        public override OperationalState GetOperationalState(Item item) => OperationalState.Ok;
    }
}
