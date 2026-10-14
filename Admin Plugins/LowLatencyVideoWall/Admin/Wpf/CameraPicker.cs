using System;
using System.Collections.Generic;
using System.Linq;
using LowLatencyVideoWall.Vapix;
using VideoOS.Platform;
using VideoOS.Platform.UI;

namespace LowLatencyVideoWall.Admin.Wpf
{
    /// <summary>
    /// Picks the camera for a pane with Milestone's own <see cref="ItemPickerWpfWindow"/>
    /// rather than a hand built dialog: a pane needs nothing more than the camera's id and
    /// name, and the built-in picker already searches the whole site and looks like the rest
    /// of the Management Client.
    ///
    /// The camera id a pane stores is the camera item's object id, which is the same Guid
    /// CameraDiscovery puts on a WallCamera, so a pane picked here resolves at render time.
    ///
    /// Only Axis cameras are offered. The wall drives cameras over VAPIX and RTSP directly, so
    /// anything else would be pickable, savable, and then a black pane with an explanation at
    /// render time. Better to not offer it at all.
    /// </summary>
    internal static class CameraPicker
    {
        /// <summary>
        /// Opens the picker over <paramref name="cameras"/>, the list the caller prefetched.
        /// An empty list is read back here; in the Management Client that is a hit on the
        /// configuration already in memory, not a server roundtrip.
        /// </summary>
        internal static bool TryPick(IEnumerable<Item> cameras, out Guid cameraId, out string cameraName)
        {
            cameraId = Guid.Empty;
            cameraName = "";

            var items = cameras?.Where(c => c != null).ToList();
            if (items == null || items.Count == 0) items = ReadCameras();

            if (items.Count == 0)
            {
                AdminUi.ShowError("Low Latency Video Wall",
                    "No Axis camera was found in this configuration. The wall streams over " +
                    "VAPIX and RTSP, so only Axis hardware can be placed on it.", null);
                return false;
            }

            var picker = new ItemPickerWpfWindow
            {
                Items = items,
                KindsFilter = new List<Guid> { Kind.Camera },
                SelectionMode = SelectionModeOptions.AutoCloseOnSelect,
                Title = "Select camera"
            };

            if (AdminUi.ShowModal(picker) != true) return false;

            var selected = picker.SelectedItems?.FirstOrDefault();
            if (selected?.FQID == null) return false;

            cameraId = selected.FQID.ObjectId;
            cameraName = selected.Name ?? "";
            return cameraId != Guid.Empty;
        }

        /// <summary>
        /// Every Axis camera on the site, flat, for the caller to prefetch off the UI thread.
        ///
        /// Flat rather than the recording server hierarchy the picker would normally show,
        /// because the hierarchy cannot be filtered: GetItemsByKind hands back the servers, and
        /// hiding a non Axis camera means hiding one of their children. Discovery already
        /// decides what is Axis, so the two are intersected here and the picker is handed the
        /// result. A site's camera count is in the hundreds, which is a list a search box
        /// handles perfectly well.
        /// </summary>
        internal static List<Item> ReadCameras()
        {
            try
            {
                var axis = new HashSet<Guid>(CameraDiscovery.Discover()
                    .Where(c => c.IsAxis)
                    .Select(c => c.CameraId));

                if (axis.Count == 0) return new List<Item>();

                var found = new List<Item>();
                var seen = new HashSet<Guid>();
                foreach (var root in Configuration.Instance.GetItemsByKind(Kind.Camera) ?? new List<Item>())
                    Collect(root, axis, seen, found);

                return found.OrderBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            }
            catch (Exception ex)
            {
                LowLatencyVideoWallDefinition.Log.Error(
                    $"Reading the camera list for the picker failed: {AdminUi.Flatten(ex)}");
                return new List<Item>();
            }
        }

        /// <summary>
        /// GetItemsByKind returns the top level, which on most sites is the recording servers
        /// rather than the cameras, so the tree has to be walked. Depth is one or two in
        /// practice; the seen set guards against a configuration that loops.
        /// </summary>
        private static void Collect(Item item, HashSet<Guid> wanted, HashSet<Guid> seen, List<Item> found)
        {
            if (item?.FQID == null || !seen.Add(item.FQID.ObjectId)) return;

            if (item.FQID.Kind == Kind.Camera && item.FQID.FolderType == FolderType.No
                && wanted.Contains(item.FQID.ObjectId))
            {
                found.Add(item);
            }

            List<Item> children;
            try { children = item.GetChildren(); }
            catch { return; }

            if (children == null) return;
            foreach (var child in children) Collect(child, wanted, seen, found);
        }
    }
}
