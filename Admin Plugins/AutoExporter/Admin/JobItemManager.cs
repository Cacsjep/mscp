using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Forms;
using VideoOS.Platform;
using VideoOS.Platform.Admin;
using VideoOS.Platform.Data;
using VideoOS.Platform.Util;

namespace AutoExporter.Admin
{
    public class JobItemManager : ItemManager
    {
        private JobUserControl _userControl;
        private readonly Guid _kind;

        public JobItemManager(Guid kind) { _kind = kind; }

        public override void Init() { }
        public override void Close() { ReleaseUserControl(); }

        #region Rules-engine event registration (top-level ItemManager)

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
            var sourceKinds = new List<Guid> { AutoExporterDefinition.JobKindId };
            return new Collection<EventType>
            {
                new EventType
                {
                    ID = AutoExporterDefinition.EvtJobStartedId,
                    Message = "Auto Export: Job Started",
                    GroupID = AutoExporterDefinition.EventGroupId,
                    StateGroupID = AutoExporterDefinition.StateGroupId,
                    State = "Running",
                    DefaultSourceKind = AutoExporterDefinition.JobKindId,
                    SourceKinds = sourceKinds
                },
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
                    States = new[] { "Running", "Success", "Failed" }
                }
            };
        }

        #endregion

        public override UserControl GenerateDetailUserControl()
        {
            _userControl = new JobUserControl();
            _userControl.ConfigurationChangedByUser += ConfigurationChangedByUserHandler;
            return _userControl;
        }

        public override void ReleaseUserControl()
        {
            if (_userControl != null)
            {
                _userControl.ConfigurationChangedByUser -= ConfigurationChangedByUserHandler;
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

            try { SecurityAccess.CheckPermission(CurrentItem, "GENERIC_WRITE"); }
            catch (NotAuthorizedMIPException)
            {
                MessageBox.Show("You do not have permission to edit Auto Exporter jobs.",
                    "Not authorized", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            var error = _userControl.ValidateInput();
            if (error != null)
            {
                MessageBox.Show(error, "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            var dupName = FindJobWithSameStoragePath(_userControl.StoragePathValue, CurrentItem.FQID.ObjectId);
            if (dupName != null)
            {
                MessageBox.Show(
                    $"Another job ('{dupName}') already uses this storage path. Two jobs writing to the same folder " +
                    "would mix their exports and fight over the retention cleanup. Please pick a different folder.",
                    "Duplicate storage path", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            _userControl.UpdateItem(CurrentItem);
            Configuration.Instance.SaveItemConfiguration(AutoExporterDefinition.PluginId, CurrentItem);
            return true;
        }

        // Returns the name of another job already using the same storage folder, or
        // null if the path is free. Paths are normalized (full path, trailing
        // separators trimmed) and compared case-insensitively.
        private string FindJobWithSameStoragePath(string path, Guid currentJobId)
        {
            var norm = NormalizePath(path);
            if (string.IsNullOrEmpty(norm)) return null;

            List<Item> jobs;
            try { jobs = Configuration.Instance.GetItemConfigurations(AutoExporterDefinition.PluginId, null, AutoExporterDefinition.JobKindId); }
            catch { return null; }

            foreach (var job in jobs)
            {
                if (job?.FQID == null || job.FQID.ObjectId == currentJobId) continue;
                if (job.Properties == null || !job.Properties.TryGetValue("StoragePath", out var other)) continue;
                if (NormalizePath(other) == norm) return job.Name;
            }
            return null;
        }

        // Normalizes for duplicate comparison WITHOUT Path.GetFullPath: these are paths
        // interpreted on the Event Server, so resolving them against the Management
        // Client's working directory would be wrong. Just trim, drop trailing
        // separators, and lower-case.
        private static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return "";
            return path.Trim().TrimEnd('\\', '/').ToLowerInvariant();
        }

        public override string GetItemName() => _userControl?.DisplayName ?? CurrentItem?.Name ?? "Job";

        public override void SetItemName(string name)
        {
            if (_userControl != null) _userControl.DisplayName = name;
            if (CurrentItem != null) CurrentItem.Name = name;
        }

        public override List<Item> GetItems()
            => FilterReadable(Configuration.Instance.GetItemConfigurations(AutoExporterDefinition.PluginId, null, _kind));

        public override List<Item> GetItems(Item parentItem)
            => FilterReadable(Configuration.Instance.GetItemConfigurations(AutoExporterDefinition.PluginId, parentItem, _kind));

        public override Item GetItem(FQID fqid)
        {
            var item = Configuration.Instance.GetItemConfiguration(AutoExporterDefinition.PluginId, _kind, fqid.ObjectId);
            if (item == null) return null;
            try { SecurityAccess.CheckPermission(item, "GENERIC_READ"); return item; }
            catch (NotAuthorizedMIPException) { return null; }
        }

        // Drops jobs the current role has no Read permission for (mirrors PKI).
        private static List<Item> FilterReadable(List<Item> items)
        {
            if (items == null) return new List<Item>();
            var allowed = new List<Item>(items.Count);
            foreach (var item in items)
            {
                try { SecurityAccess.CheckPermission(item, "GENERIC_READ"); allowed.Add(item); }
                catch (NotAuthorizedMIPException) { }
            }
            return allowed;
        }

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

    }
}
