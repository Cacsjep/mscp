using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading;
using System.Windows.Forms;
using VideoOS.Platform;
using VideoOS.Platform.Admin;
using VideoOS.Platform.Data;
using VideoOS.Platform.Messaging;

namespace CertWatchdog.Admin
{
    public class CertWatchdogItemManager : ItemManager
    {
        private CertWatchdogAdminUserControl _userControl;
        private readonly Guid _kind;

        public CertWatchdogItemManager(Guid kind)
        {
            _kind = kind;
        }

        public override void Init()
        {
            // Pre-start MessageCommunication on a background thread
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

        #region Event Registration

        public override Collection<VideoOS.Platform.Data.EventGroup> GetKnownEventGroups(CultureInfo culture)
        {
            return new Collection<VideoOS.Platform.Data.EventGroup>
            {
                new VideoOS.Platform.Data.EventGroup
                {
                    ID = CertWatchdogDefinition.EventGroupId,
                    Name = "Certificate Watchdog"
                },
                new VideoOS.Platform.Data.EventGroup
                {
                    ID = CertWatchdogDefinition.DeviceEventGroupId,
                    Name = "Device Certificate Watchdog"
                }
            };
        }

        public override Collection<VideoOS.Platform.Data.EventType> GetKnownEventTypes(CultureInfo culture)
        {
            var sourceKinds = new List<Guid> { CertWatchdogDefinition.CertWatchdogKindId };
            var deviceSourceKinds = new List<Guid> { Kind.Camera };

            return new Collection<VideoOS.Platform.Data.EventType>
            {
                // Server certificate events
                new VideoOS.Platform.Data.EventType
                {
                    ID = CertWatchdogDefinition.EventType60DaysId,
                    Message = "Cert Expire (60 Days)",
                    GroupID = CertWatchdogDefinition.EventGroupId,
                    StateGroupID = CertWatchdogDefinition.StateGroupId,
                    State = "Expiring",
                    DefaultSourceKind = CertWatchdogDefinition.CertWatchdogKindId,
                    SourceKinds = sourceKinds
                },
                new VideoOS.Platform.Data.EventType
                {
                    ID = CertWatchdogDefinition.EventType30DaysId,
                    Message = "Cert Expire (30 Days)",
                    GroupID = CertWatchdogDefinition.EventGroupId,
                    StateGroupID = CertWatchdogDefinition.StateGroupId,
                    State = "Critical",
                    DefaultSourceKind = CertWatchdogDefinition.CertWatchdogKindId,
                    SourceKinds = sourceKinds
                },
                new VideoOS.Platform.Data.EventType
                {
                    ID = CertWatchdogDefinition.EventType15DaysId,
                    Message = "Cert Expire (15 Days)",
                    GroupID = CertWatchdogDefinition.EventGroupId,
                    StateGroupID = CertWatchdogDefinition.StateGroupId,
                    State = "Critical",
                    DefaultSourceKind = CertWatchdogDefinition.CertWatchdogKindId,
                    SourceKinds = sourceKinds
                },
                // Device certificate events
                new VideoOS.Platform.Data.EventType
                {
                    ID = CertWatchdogDefinition.DeviceEventType60DaysId,
                    Message = "Device Cert Expire (60 Days)",
                    GroupID = CertWatchdogDefinition.DeviceEventGroupId,
                    StateGroupID = CertWatchdogDefinition.StateGroupId,
                    State = "Expiring",
                    DefaultSourceKind = Kind.Camera,
                    SourceKinds = deviceSourceKinds
                },
                new VideoOS.Platform.Data.EventType
                {
                    ID = CertWatchdogDefinition.DeviceEventType30DaysId,
                    Message = "Device Cert Expire (30 Days)",
                    GroupID = CertWatchdogDefinition.DeviceEventGroupId,
                    StateGroupID = CertWatchdogDefinition.StateGroupId,
                    State = "Critical",
                    DefaultSourceKind = Kind.Camera,
                    SourceKinds = deviceSourceKinds
                },
                new VideoOS.Platform.Data.EventType
                {
                    ID = CertWatchdogDefinition.DeviceEventType15DaysId,
                    Message = "Device Cert Expire (15 Days)",
                    GroupID = CertWatchdogDefinition.DeviceEventGroupId,
                    StateGroupID = CertWatchdogDefinition.StateGroupId,
                    State = "Critical",
                    DefaultSourceKind = Kind.Camera,
                    SourceKinds = deviceSourceKinds
                }
            };
        }

        public override Collection<VideoOS.Platform.Data.StateGroup> GetKnownStateGroups(CultureInfo culture)
        {
            return new Collection<VideoOS.Platform.Data.StateGroup>
            {
                new VideoOS.Platform.Data.StateGroup
                {
                    ID = CertWatchdogDefinition.StateGroupId,
                    Name = "Certificate Status",
                    States = new[] { "OK", "Expiring", "Critical" }
                }
            };
        }

        #endregion

        #region User Control

        public override UserControl GenerateDetailUserControl()
        {
            _userControl = new CertWatchdogAdminUserControl();
            return _userControl;
        }

        public override void ReleaseUserControl()
        {
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
            if (CurrentItem != null)
            {
                Configuration.Instance.SaveItemConfiguration(
                    CertWatchdogDefinition.PluginId, CurrentItem);
            }
            return true;
        }

        #endregion

        #region Item Management

        public override string GetItemName()
        {
            return CurrentItem?.Name ?? "Certificate Watchdog";
        }

        public override void SetItemName(string name)
        {
            if (CurrentItem != null)
                CurrentItem.Name = name;
        }

        public override List<Item> GetItems()
        {
            return Configuration.Instance.GetItemConfigurations(
                CertWatchdogDefinition.PluginId, null, _kind);
        }

        public override List<Item> GetItems(Item parentItem)
        {
            return Configuration.Instance.GetItemConfigurations(
                CertWatchdogDefinition.PluginId, parentItem, _kind);
        }

        public override Item GetItem(FQID fqid)
        {
            return Configuration.Instance.GetItemConfiguration(
                CertWatchdogDefinition.PluginId, _kind, fqid.ObjectId);
        }

        public override Item CreateItem(Item parentItem, FQID suggestedFQID)
        {
            CurrentItem = new Item(suggestedFQID, "Certificate Watchdog");
            CurrentItem.Properties["CheckIntervalHours"] = "6";
            Configuration.Instance.SaveItemConfiguration(
                CertWatchdogDefinition.PluginId, CurrentItem);
            return CurrentItem;
        }

        public override void DeleteItem(Item item)
        {
            if (item != null)
            {
                Configuration.Instance.DeleteItemConfiguration(
                    CertWatchdogDefinition.PluginId, item);
            }
        }

        #endregion

        #region Status

        public override OperationalState GetOperationalState(Item item)
        {
            return OperationalState.Ok;
        }

        public override string GetItemStatusDetails(Item item, string language)
        {
            return "Certificate monitoring active";
        }

        #endregion
    }
}
