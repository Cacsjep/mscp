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
    /// The container node for wall views. One configuration item is one ViewProfile, which is
    /// what makes a view selectable in the rule action: the Management Client builds the
    /// action's dropdown from the plugin's items of this kind, and the item name is the only
    /// thing the rule author sees.
    ///
    /// Everything is persisted through WallConfigStore so the payload format stays in one
    /// place; this class only ties the Management Client's item lifecycle to it.
    /// </summary>
    public class ViewsItemManager : ItemManager
    {
        private readonly Guid _kind;
        private ViewsUserControl _userControl;
        private HostedNodeUserControl _hosted;

        public ViewsItemManager(Guid kind)
        {
            _kind = kind;
        }

        public override void Init() { }

        public override void Close() => ReleaseUserControl();

        // ---- user control ----------------------------------------------------------------

        public override UserControl GenerateDetailUserControl()
        {
            // The client sometimes rebuilds the detail pane without releasing the old control
            // first, so release defensively rather than leaking event handlers.
            ReleaseUserControl();

            try
            {
                // The editor is WPF, so what the client gets is the ElementHost wrapper; the
                // page itself is kept separately because that is what FillUserControl talks to.
                _userControl = new ViewsUserControl();
                _userControl.ConfigurationChangedByUser += ConfigurationChangedByUserHandler;
                _hosted = new HostedNodeUserControl(_userControl);
                return _hosted;
            }
            catch (Exception ex)
            {
                LowLatencyVideoWallDefinition.Log.Error($"Building the wall view editor failed: {ex}");
                return new HostedNodeUserControl(
                    AdminUi.ErrorPage("The wall view editor could not be built.", ex));
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
                    "This wall view could not be opened.", ex);
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

                var profile = _userControl.BuildProfile(CurrentItem.FQID?.ObjectId ?? Guid.Empty);
                WallConfigStore.SaveViewProfile(profile);

                _userControl.UpdateItem(CurrentItem);

                LowLatencyVideoWallDefinition.Log.Info(
                    $"Saved wall view '{profile.Name}' ({profile.Id}) with {profile.Panes.Count} pane(s)");
                return true;
            }
            catch (Exception ex)
            {
                AdminUi.ShowError("Low Latency Video Wall",
                    "This wall view could not be saved.", ex);
                return false;
            }
        }

        // ---- item name -------------------------------------------------------------------

        public override string GetItemName()
        {
            var fromControl = _userControl?.DisplayName;
            if (!string.IsNullOrWhiteSpace(fromControl)) return fromControl;
            return CurrentItem?.Name ?? "Wall view";
        }

        public override void SetItemName(string name)
        {
            // A rename in the tree has to reach the form, because the form is what gets
            // written back on save.
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
                LowLatencyVideoWallDefinition.Log.Error($"Listing wall views failed: {ex.Message}");
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
                LowLatencyVideoWallDefinition.Log.Error($"Listing wall views failed: {ex.Message}");
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
                LowLatencyVideoWallDefinition.Log.Error($"Reading a wall view failed: {ex.Message}");
                return null;
            }
        }

        public override Item CreateItem(Item parentItem, FQID suggestedFQID)
        {
            try
            {
                // The profile id is the item's object id, which is what keeps WallConfigStore
                // and the Management Client tree talking about the same thing.
                var profile = new ViewProfile
                {
                    Id = suggestedFQID.ObjectId,
                    Name = UniqueName("New wall view")
                };

                WallConfigStore.SaveViewProfile(profile);

                CurrentItem = GetItem(suggestedFQID) ?? new Item(suggestedFQID, profile.Name);
                _userControl?.FillContent(CurrentItem);

                LowLatencyVideoWallDefinition.Log.Info($"Created wall view '{profile.Name}' ({profile.Id})");
                return CurrentItem;
            }
            catch (Exception ex)
            {
                AdminUi.ShowError("Low Latency Video Wall",
                    "A new wall view could not be created.", ex);
                return null;
            }
        }

        public override void DeleteItem(Item item)
        {
            if (item?.FQID == null) return;

            try
            {
                var id = item.FQID.ObjectId;

                // An assignment pointing at a deleted view would silently resolve to nothing,
                // so say which users are about to lose their default view.
                var orphaned = WallConfigStore.LoadAssignments()
                    .Where(a => a.ViewProfileId == id)
                    .Select(a => a.UserName)
                    .ToList();

                WallConfigStore.DeleteViewProfile(id);
                LowLatencyVideoWallDefinition.Log.Info($"Deleted wall view '{item.Name}' ({id})");

                if (orphaned.Count > 0)
                {
                    MessageBox.Show(
                        "This wall view was the default for: " + string.Join(", ", orphaned) +
                        "\r\n\r\nThose assignments now have no view. Open Wall assignment to repoint them.",
                        "Low Latency Video Wall", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                AdminUi.ShowError("Low Latency Video Wall",
                    "This wall view could not be deleted.", ex);
            }
        }

        public override string GetItemStatusDetails(Item item, string language)
        {
            if (item?.FQID == null) return "";

            try
            {
                var profile = WallConfigStore.LoadViewProfile(item.FQID.ObjectId);
                if (profile == null) return "Not configured";

                var empty = profile.Panes.Count(p => p.CameraId == Guid.Empty);
                if (profile.Panes.Count == 0) return "No panes";
                return empty == 0
                    ? $"{profile.Panes.Count} pane(s)"
                    : $"{profile.Panes.Count} pane(s), {empty} without a camera";
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
                    WallConfigStore.LoadViewProfiles().Select(p => (p.Name ?? "").Trim()),
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
                LowLatencyVideoWallDefinition.Log.Error($"Picking a wall view name failed: {ex.Message}");
            }

            return baseName;
        }
    }
}
