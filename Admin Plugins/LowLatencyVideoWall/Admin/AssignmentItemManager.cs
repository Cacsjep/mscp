using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using LowLatencyVideoWall.Config;
using LowLatencyVideoWall.Model;
using VideoOS.Platform;
using VideoOS.Platform.Admin;

namespace LowLatencyVideoWall.Admin
{
    /// <summary>
    /// The container node for wall assignments. One configuration item is one WallAssignment,
    /// and its name is the user name: the rule action builds its target dropdown from these
    /// items, so a user with no item here cannot be targeted by a rule at all.
    ///
    /// That is also why an assignment with no view profile is a valid, saveable row.
    /// </summary>
    public class AssignmentItemManager : ItemManager
    {
        private readonly Guid _kind;
        private AssignmentUserControl _userControl;
        private HostedNodeUserControl _hosted;

        public AssignmentItemManager(Guid kind)
        {
            _kind = kind;
        }

        public override void Init() { }

        public override void Close() => ReleaseUserControl();

        // ---- user control ----------------------------------------------------------------

        public override UserControl GenerateDetailUserControl()
        {
            ReleaseUserControl();

            try
            {
                // The editor is WPF, so what the client gets is the ElementHost wrapper; the
                // page itself is kept separately because that is what FillUserControl talks to.
                _userControl = new AssignmentUserControl();
                _userControl.ConfigurationChangedByUser += ConfigurationChangedByUserHandler;
                _hosted = new HostedNodeUserControl(_userControl);
                return _hosted;
            }
            catch (Exception ex)
            {
                LowLatencyVideoWallDefinition.Log.Error($"Building the assignment editor failed: {ex}");
                return new HostedNodeUserControl(
                    AdminUi.ErrorPage("The assignment editor could not be built.", ex));
            }
        }

        public override void ReleaseUserControl()
        {
            if (_userControl != null)
            {
                _userControl.ConfigurationChangedByUser -= ConfigurationChangedByUserHandler;
                _userControl = null;
            }

            if (_hosted != null)
            {
                try { _hosted.Dispose(); } catch { }
                _hosted = null;
            }
        }

        public override void FillUserControl(Item item)
        {
            CurrentItem = item;
            try
            {
                _userControl?.FillContent(item);
            }
            catch (Exception ex)
            {
                AdminUi.ShowError("Low Latency Video Wall",
                    "This assignment could not be opened.", ex);
            }
        }

        public override void ClearUserControl()
        {
            CurrentItem = null;
            try { _userControl?.ClearContent(); } catch { }
        }

        public override bool ValidateAndSaveUserControl()
        {
            if (CurrentItem == null || _userControl == null) return true;

            try
            {
                var error = _userControl.ValidateInput();
                if (error != null)
                {
                    MessageBox.Show(error, "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return false;
                }

                var assignment = _userControl.BuildAssignment(CurrentItem.FQID?.ObjectId ?? Guid.Empty);
                WallConfigStore.SaveAssignment(assignment);

                _userControl.UpdateItem(CurrentItem);

                LowLatencyVideoWallDefinition.Log.Info(
                    assignment.ViewProfileId == Guid.Empty
                        ? $"Saved wall assignment for '{assignment.UserName}' with no default view"
                        : $"Saved wall assignment for '{assignment.UserName}' pointing at view {assignment.ViewProfileId}");

                return true;
            }
            catch (Exception ex)
            {
                AdminUi.ShowError("Low Latency Video Wall",
                    "This assignment could not be saved.", ex);
                return false;
            }
        }

        // ---- item name -------------------------------------------------------------------

        public override string GetItemName()
        {
            var fromControl = _userControl?.DisplayName;
            if (!string.IsNullOrWhiteSpace(fromControl)) return fromControl;
            return CurrentItem?.Name ?? "Wall assignment";
        }

        public override void SetItemName(string name)
        {
            // Renaming the item in the tree is renaming the user, so the form has to follow.
            _userControl?.SetDisplayName(name);
            if (CurrentItem != null) CurrentItem.Name = name;
        }

        // ---- items -----------------------------------------------------------------------

        public override List<Item> GetItems()
        {
            try
            {
                return Configuration.Instance.GetItemConfigurations(
                           LowLatencyVideoWallDefinition.PluginId, null, _kind) ?? new List<Item>();
            }
            catch (Exception ex)
            {
                LowLatencyVideoWallDefinition.Log.Error($"Listing wall assignments failed: {ex.Message}");
                return new List<Item>();
            }
        }

        public override List<Item> GetItems(Item parentItem)
        {
            try
            {
                return Configuration.Instance.GetItemConfigurations(
                           LowLatencyVideoWallDefinition.PluginId, parentItem, _kind) ?? new List<Item>();
            }
            catch (Exception ex)
            {
                LowLatencyVideoWallDefinition.Log.Error($"Listing wall assignments failed: {ex.Message}");
                return new List<Item>();
            }
        }

        public override Item GetItem(FQID fqid)
        {
            try
            {
                return Configuration.Instance.GetItemConfiguration(
                    LowLatencyVideoWallDefinition.PluginId, _kind, fqid.ObjectId);
            }
            catch (Exception ex)
            {
                LowLatencyVideoWallDefinition.Log.Error($"Reading a wall assignment failed: {ex.Message}");
                return null;
            }
        }

        public override Item CreateItem(Item parentItem, FQID suggestedFQID)
        {
            try
            {
                // No view yet on purpose: the admin picks one, or leaves it empty so the user
                // is only a rule target.
                var assignment = new WallAssignment
                {
                    Id = suggestedFQID.ObjectId,
                    UserName = UniqueName("New user"),
                    ViewProfileId = Guid.Empty
                };

                WallConfigStore.SaveAssignment(assignment);

                CurrentItem = GetItem(suggestedFQID) ?? new Item(suggestedFQID, assignment.UserName);
                _userControl?.FillContent(CurrentItem);

                LowLatencyVideoWallDefinition.Log.Info(
                    $"Created wall assignment '{assignment.UserName}' ({assignment.Id})");
                return CurrentItem;
            }
            catch (Exception ex)
            {
                AdminUi.ShowError("Low Latency Video Wall",
                    "A new assignment could not be created.", ex);
                return null;
            }
        }

        public override void DeleteItem(Item item)
        {
            if (item?.FQID == null) return;

            try
            {
                WallConfigStore.DeleteAssignment(item.FQID.ObjectId);
                LowLatencyVideoWallDefinition.Log.Info(
                    $"Deleted wall assignment '{item.Name}' ({item.FQID.ObjectId}). " +
                    "That user can no longer be targeted by the wall rule action.");
            }
            catch (Exception ex)
            {
                AdminUi.ShowError("Low Latency Video Wall",
                    "This assignment could not be deleted.", ex);
            }
        }

        public override string GetItemStatusDetails(Item item, string language)
        {
            if (item?.FQID == null) return "";

            try
            {
                var assignment = WallConfigStore.LoadAssignments()
                    .FirstOrDefault(a => a.Id == item.FQID.ObjectId);

                if (assignment == null) return "Not configured";
                if (assignment.ViewProfileId == Guid.Empty) return "Rule target only, no default view";

                var profile = WallConfigStore.LoadViewProfile(assignment.ViewProfileId);
                return profile == null
                    ? "Points at a view that no longer exists"
                    : $"Starts on '{profile.Name}'";
            }
            catch
            {
                return "";
            }
        }

        private static string UniqueName(string baseName)
        {
            try
            {
                var taken = new HashSet<string>(
                    WallConfigStore.LoadAssignments().Select(a => (a.UserName ?? "").Trim()),
                    StringComparer.CurrentCultureIgnoreCase);

                if (!taken.Contains(baseName)) return baseName;

                for (int i = 2; i < 1000; i++)
                {
                    var candidate = $"{baseName} {i}";
                    if (!taken.Contains(candidate)) return candidate;
                }
            }
            catch (Exception ex)
            {
                LowLatencyVideoWallDefinition.Log.Error($"Picking an assignment name failed: {ex.Message}");
            }

            return baseName;
        }
    }
}
