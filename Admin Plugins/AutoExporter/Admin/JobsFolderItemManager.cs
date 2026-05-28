using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Forms;
using VideoOS.Platform;
using VideoOS.Platform.Admin;
using VideoOS.Platform.Data;

namespace AutoExporter.Admin
{
    /// <summary>
    /// Parent (folder) ItemManager for the "Jobs" node. Holds the Rules-engine
    /// event registration so the Success/Failed events can target individual Jobs
    /// or the whole folder via the standard "ALL" targeting in the Rules UI.
    /// </summary>
    public class JobsFolderItemManager : ItemManager
    {
        private JobsFolderUserControl _userControl;
        private readonly Guid _kind;

        public JobsFolderItemManager(Guid kind) { _kind = kind; }

        public override void Init() { }
        public override void Close() { ReleaseUserControl(); }

        #region Event registration (on parent ItemManager)

        public override Collection<EventGroup> GetKnownEventGroups(CultureInfo culture)
        {
            return new Collection<EventGroup>
            {
                new EventGroup
                {
                    ID = AutoExporterDefinition.EventGroupId,
                    Name = "Auto Exporter"
                }
            };
        }

        public override Collection<EventType> GetKnownEventTypes(CultureInfo culture)
        {
            var sourceKinds = new List<Guid>
            {
                AutoExporterDefinition.JobKindId,
                AutoExporterDefinition.JobsFolderKindId
            };

            return new Collection<EventType>
            {
                new EventType
                {
                    ID = AutoExporterDefinition.EvtJobSucceededId,
                    Message = "Auto Export: Job Succeeded",
                    GroupID = AutoExporterDefinition.EventGroupId,
                    StateGroupID = AutoExporterDefinition.StateGroupId,
                    State = "Success",
                    DefaultSourceKind = AutoExporterDefinition.JobKindId,
                    SourceKinds = sourceKinds
                },
                new EventType
                {
                    ID = AutoExporterDefinition.EvtJobFailedId,
                    Message = "Auto Export: Job Failed",
                    GroupID = AutoExporterDefinition.EventGroupId,
                    StateGroupID = AutoExporterDefinition.StateGroupId,
                    State = "Failed",
                    DefaultSourceKind = AutoExporterDefinition.JobKindId,
                    SourceKinds = sourceKinds
                }
            };
        }

        public override Collection<StateGroup> GetKnownStateGroups(CultureInfo culture)
        {
            return new Collection<StateGroup>
            {
                new StateGroup
                {
                    ID = AutoExporterDefinition.StateGroupId,
                    Name = "Auto Export Status",
                    States = new[] { "Success", "Failed" }
                }
            };
        }

        #endregion

        #region User control

        public override UserControl GenerateDetailUserControl()
        {
            _userControl = new JobsFolderUserControl();
            return _userControl;
        }

        public override void ReleaseUserControl() { _userControl = null; }
        public override void FillUserControl(Item item) { CurrentItem = item; }
        public override void ClearUserControl() { CurrentItem = null; }
        public override bool ValidateAndSaveUserControl() { return true; }

        #endregion

        #region CRUD

        public override string GetItemName() => "Jobs";
        public override void SetItemName(string name) { if (CurrentItem != null) CurrentItem.Name = "Jobs"; }

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
                AutoExporterDefinition.JobsFolderSingletonId,
                FolderType.UserDefined,
                _kind);

            CurrentItem = new Item(fqid, "Jobs");
            Configuration.Instance.SaveItemConfiguration(AutoExporterDefinition.PluginId, CurrentItem);
            return CurrentItem;
        }

        public override void DeleteItem(Item item)
        {
            if (item == null) return;

            // Delete child jobs first.
            var children = Configuration.Instance.GetItemConfigurations(
                AutoExporterDefinition.PluginId, item, AutoExporterDefinition.JobKindId);
            if (children != null)
            {
                foreach (var c in children)
                    Configuration.Instance.DeleteItemConfiguration(AutoExporterDefinition.PluginId, c);
            }
            Configuration.Instance.DeleteItemConfiguration(AutoExporterDefinition.PluginId, item);
        }

        public override OperationalState GetOperationalState(Item item) => OperationalState.Ok;
        public override string GetItemStatusDetails(Item item, string language) => "Auto export jobs";

        #endregion
    }
}
