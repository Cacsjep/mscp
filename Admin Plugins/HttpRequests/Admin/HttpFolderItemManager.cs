using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Forms;
using VideoOS.Platform;
using VideoOS.Platform.Admin;

namespace HttpRequests.Admin
{
    public class HttpFolderItemManager : ItemManager
    {
        private HttpFolderUserControl _userControl;
        private readonly Guid _kind;

        public HttpFolderItemManager(Guid kind)
        {
            _kind = kind;
        }

        public override void Init() { }
        public override void Close() { ReleaseUserControl(); }

        #region Event Registration

        public override Collection<VideoOS.Platform.Data.EventGroup> GetKnownEventGroups(CultureInfo culture)
        {
            return new Collection<VideoOS.Platform.Data.EventGroup>
            {
                new VideoOS.Platform.Data.EventGroup
                {
                    ID = HttpRequestsDefinition.EventGroupId,
                    Name = "HTTP Requests"
                }
            };
        }

        public override Collection<VideoOS.Platform.Data.EventType> GetKnownEventTypes(CultureInfo culture)
        {
            var sourceKinds = new List<Guid>
            {
                HttpRequestsDefinition.RequestKindId,
                HttpRequestsDefinition.FolderKindId
            };

            return new Collection<VideoOS.Platform.Data.EventType>
            {
                new VideoOS.Platform.Data.EventType
                {
                    ID = HttpRequestsDefinition.EvtRequestExecutedId,
                    Message = "HTTP Request Executed",
                    GroupID = HttpRequestsDefinition.EventGroupId,
                    StateGroupID = HttpRequestsDefinition.StateGroupId,
                    State = "Success",
                    DefaultSourceKind = HttpRequestsDefinition.FolderKindId,
                    SourceKinds = sourceKinds
                },
                new VideoOS.Platform.Data.EventType
                {
                    ID = HttpRequestsDefinition.EvtRequestFailedId,
                    Message = "HTTP Request Failed",
                    GroupID = HttpRequestsDefinition.EventGroupId,
                    StateGroupID = HttpRequestsDefinition.StateGroupId,
                    State = "Failed",
                    DefaultSourceKind = HttpRequestsDefinition.FolderKindId,
                    SourceKinds = sourceKinds
                }
            };
        }

        public override Collection<VideoOS.Platform.Data.StateGroup> GetKnownStateGroups(CultureInfo culture)
        {
            return new Collection<VideoOS.Platform.Data.StateGroup>
            {
                new VideoOS.Platform.Data.StateGroup
                {
                    ID = HttpRequestsDefinition.StateGroupId,
                    Name = "HTTP Request Status",
                    States = new[] { "Success", "Failed" }
                }
            };
        }

        #endregion

        #region User Control

        public override UserControl GenerateDetailUserControl()
        {
            _userControl = new HttpFolderUserControl();
            _userControl.ConfigurationChangedByUser += ConfigurationChangedByUserHandler;
            return _userControl;
        }

        public override void ReleaseUserControl()
        {
            if (_userControl != null)
                _userControl.ConfigurationChangedByUser -= ConfigurationChangedByUserHandler;
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
                _userControl.UpdateItem(CurrentItem);
                Configuration.Instance.SaveItemConfiguration(HttpRequestsDefinition.PluginId, CurrentItem);
            }
            return true;
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
                HttpRequestsDefinition.PluginId, null, _kind);
        }

        public override List<Item> GetItems(Item parentItem)
        {
            return Configuration.Instance.GetItemConfigurations(
                HttpRequestsDefinition.PluginId, parentItem, _kind);
        }

        public override Item GetItem(FQID fqid)
        {
            return Configuration.Instance.GetItemConfiguration(
                HttpRequestsDefinition.PluginId, _kind, fqid.ObjectId);
        }

        public override Item CreateItem(Item parentItem, FQID suggestedFQID)
        {
            CurrentItem = new Item(suggestedFQID, "New Request Folder");
            if (_userControl != null)
                _userControl.FillContent(CurrentItem);
            Configuration.Instance.SaveItemConfiguration(HttpRequestsDefinition.PluginId, CurrentItem);
            return CurrentItem;
        }

        public override void DeleteItem(Item item)
        {
            if (item == null) return;

            // Delete all child requests first to avoid ghost items
            var children = Configuration.Instance.GetItemConfigurations(
                HttpRequestsDefinition.PluginId, item, HttpRequestsDefinition.RequestKindId);
            if (children != null)
            {
                foreach (var child in children)
                    Configuration.Instance.DeleteItemConfiguration(HttpRequestsDefinition.PluginId, child);
            }

            Configuration.Instance.DeleteItemConfiguration(HttpRequestsDefinition.PluginId, item);
        }

        #endregion

        #region Status

        public override OperationalState GetOperationalState(Item item)
        {
            return OperationalState.Ok;
        }

        public override string GetItemStatusDetails(Item item, string language)
        {
            return "Request folder";
        }

        #endregion
    }
}
