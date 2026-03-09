using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Forms;
using VideoOS.Platform;
using VideoOS.Platform.Admin;

namespace HttpRequests.Admin
{
    public class HttpRequestItemManager : ItemManager
    {
        private HttpRequestUserControl _userControl;
        private readonly Guid _kind;

        public HttpRequestItemManager(Guid kind)
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
                    DefaultSourceKind = HttpRequestsDefinition.RequestKindId,
                    SourceKinds = sourceKinds
                },
                new VideoOS.Platform.Data.EventType
                {
                    ID = HttpRequestsDefinition.EvtRequestFailedId,
                    Message = "HTTP Request Failed",
                    GroupID = HttpRequestsDefinition.EventGroupId,
                    StateGroupID = HttpRequestsDefinition.StateGroupId,
                    State = "Failed",
                    DefaultSourceKind = HttpRequestsDefinition.RequestKindId,
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
            _userControl = new HttpRequestUserControl();
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

            Configuration.Instance.SaveItemConfiguration(HttpRequestsDefinition.PluginId, newItem);

            MessageBox.Show(
                $"Created \"{newItem.Name}\".\n\nRefresh the tree (collapse/expand the folder) to see it.",
                "Duplicated",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
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
            CurrentItem = new Item(suggestedFQID, "New HTTP Request");
            CurrentItem.Properties["Enabled"] = "Yes";
            CurrentItem.Properties["HttpMethod"] = "POST";
            CurrentItem.Properties["Url"] = "https://";
            CurrentItem.Properties["PayloadType"] = "json";
            CurrentItem.Properties["UserPayload"] = "";
            CurrentItem.Properties["Headers"] = "";
            CurrentItem.Properties["SkipCertValidation"] = "No";
            CurrentItem.Properties["IncludeEventData"] = "Yes";
            CurrentItem.Properties["TimeoutMs"] = "10000";
            CurrentItem.Properties["AuthType"] = "None";
            CurrentItem.Properties["AuthValue"] = "";

            if (_userControl != null)
                _userControl.FillContent(CurrentItem);

            Configuration.Instance.SaveItemConfiguration(HttpRequestsDefinition.PluginId, CurrentItem);
            return CurrentItem;
        }

        public override void DeleteItem(Item item)
        {
            if (item != null)
                Configuration.Instance.DeleteItemConfiguration(HttpRequestsDefinition.PluginId, item);
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

            var method = item.Properties.ContainsKey("HttpMethod") ? item.Properties["HttpMethod"] : "?";
            var url = item.Properties.ContainsKey("Url") ? item.Properties["Url"] : "";
            return $"{method} {url}";
        }

        #endregion
    }
}
