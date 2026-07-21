using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using CommunitySDK;
using VideoOS.Platform;
using VideoOS.Platform.Messaging;

namespace SmartBar.Client
{
    /// <summary>
    /// Background loader and cache for the command items that require server
    /// round-trips (cameras, outputs, events). On large federated sites
    /// enumerating these can take minutes, so it must never run on the UI
    /// thread and results are reused between palette opens.
    /// </summary>
    static class SmartBarItemCache
    {
        private static readonly PluginLog Log = SmartBarDefinition.Log;
        private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(60);

        private static readonly object Sync = new object();
        private static List<CommandItem> _items;
        private static Dictionary<Guid, Item> _camerasById;
        private static DateTime _loadedUtc;
        private static Task _refreshTask;

        /// <summary>
        /// Load progress ("Loading cameras... 1200"). Raised on a worker thread.
        /// </summary>
        public static event Action<string> Progress;

        public static bool IsLoaded
        {
            get { lock (Sync) return _items != null; }
        }

        public static List<CommandItem> GetItems()
        {
            lock (Sync) return _items ?? new List<CommandItem>();
        }

        public static Item FindCamera(Guid objectId)
        {
            lock (Sync)
            {
                if (_camerasById != null && _camerasById.TryGetValue(objectId, out var item))
                    return item;
                return null;
            }
        }

        public static void Invalidate()
        {
            lock (Sync)
            {
                _items = null;
                _camerasById = null;
                _loadedUtc = DateTime.MinValue;
            }
        }

        /// <summary>
        /// Starts a background refresh unless the cache is still fresh.
        /// The callback fires on a worker thread once a load has completed.
        /// </summary>
        public static void EnsureFresh(Action onCompleted = null)
        {
            Task task;
            lock (Sync)
            {
                if (_items != null && DateTime.UtcNow - _loadedUtc < RefreshInterval)
                    return;
                if (_refreshTask == null || _refreshTask.IsCompleted)
                    _refreshTask = Task.Run(() => Load());
                task = _refreshTask;
            }
            if (onCompleted != null)
                task.ContinueWith(_ => onCompleted());
        }

        private static void Load()
        {
            try
            {
                var sw = Stopwatch.StartNew();
                var items = new List<CommandItem>();
                var camerasById = new Dictionary<Guid, Item>();

                if (SmartBarConfig.IsEnabled(ItemCategory.Camera) || SmartBarConfig.IsEnabled(ItemCategory.Recent))
                    LoadCameras(items, camerasById);
                if (SmartBarConfig.IsEnabled(ItemCategory.Output))
                    LoadOutputs(items);
                if (SmartBarConfig.IsEnabled(ItemCategory.Event))
                    LoadEvents(items);

                lock (Sync)
                {
                    _items = items;
                    _camerasById = camerasById;
                    _loadedUtc = DateTime.UtcNow;
                }
                Log.Info($"Item cache refreshed: {items.Count} items in {sw.ElapsedMilliseconds} ms");
            }
            catch (Exception ex) { Log.Error("Item cache refresh failed", ex); }
        }

        private static void ReportProgress(string status)
        {
            var handler = Progress;
            if (handler == null) return;
            try { handler(status); } catch { }
        }

        private static void LoadCameras(List<CommandItem> items, Dictionary<Guid, Item> camerasById)
        {
            try
            {
                ReportProgress("Loading cameras...");
                var roots = Configuration.Instance.GetItemsByKind(Kind.Camera);
                if (roots == null) return;

                bool addItems = SmartBarConfig.IsEnabled(ItemCategory.Camera);
                int count = 0;
                foreach (var root in roots)
                    CollectCameras(root, null, addItems, items, camerasById, ref count);
            }
            catch (Exception ex) { Log.Error("LoadCameras failed", ex); }
        }

        private static void CollectCameras(Item item, string parentPath, bool addItems,
            List<CommandItem> items, Dictionary<Guid, Item> camerasById, ref int count)
        {
            if (item.FQID.FolderType == FolderType.No)
            {
                if (item.FQID.Kind != Kind.Camera) return;
                camerasById[item.FQID.ObjectId] = item;
                if (addItems)
                {
                    items.Add(new CommandItem
                    {
                        Name = item.Name,
                        Group = parentPath ?? "Cameras",
                        Category = ItemCategory.Camera,
                        PlatformItem = item
                    });
                }
                count++;
                if (count % 100 == 0)
                    ReportProgress($"Loading cameras... {count}");
                return;
            }

            var path = parentPath == null ? item.Name : parentPath + " \u203A " + item.Name;
            foreach (var child in item.GetChildren())
                CollectCameras(child, path, addItems, items, camerasById, ref count);
        }

        private static void LoadOutputs(List<CommandItem> items)
        {
            try
            {
                ReportProgress("Loading outputs...");
                var outputs = Configuration.Instance.GetItemsByKind(Kind.Output);
                foreach (var output in outputs)
                    CollectOutputItems(output, items);
            }
            catch (Exception ex) { Log.Error("LoadOutputs failed", ex); }
        }

        private static void CollectOutputItems(Item item, List<CommandItem> items)
        {
            if (item.FQID.FolderType == FolderType.No)
            {
                var fqid = item.FQID;
                items.Add(new CommandItem
                {
                    Name = "Output: " + item.Name + " Activate",
                    Group = "Outputs",
                    Category = ItemCategory.Output,
                    Execute = () =>
                    {
                        try
                        {
                            EnvironmentManager.Instance.SendMessage(
                                new Message(MessageId.Control.OutputActivate) { RelatedFQID = fqid }, fqid);
                            Log.Info($"Output activated: {item.Name}");
                        }
                        catch (Exception ex) { Log.Error($"OutputActivate failed: {item.Name}", ex); }
                    }
                });
                items.Add(new CommandItem
                {
                    Name = "Output: " + item.Name + " Deactivate",
                    Group = "Outputs",
                    Category = ItemCategory.Output,
                    Execute = () =>
                    {
                        try
                        {
                            EnvironmentManager.Instance.SendMessage(
                                new Message(MessageId.Control.OutputDeactivate) { RelatedFQID = fqid }, fqid);
                            Log.Info($"Output deactivated: {item.Name}");
                        }
                        catch (Exception ex) { Log.Error($"OutputDeactivate failed: {item.Name}", ex); }
                    }
                });
            }
            else
            {
                foreach (var child in item.GetChildren())
                    CollectOutputItems(child, items);
            }
        }

        private static void LoadEvents(List<CommandItem> items)
        {
            try
            {
                ReportProgress("Loading events...");
                var events = Configuration.Instance.GetItemsByKind(Kind.TriggerEvent);
                foreach (var ev in events)
                    CollectEventItems(ev, items);
            }
            catch (Exception ex) { Log.Error("LoadEvents failed", ex); }
        }

        private static void CollectEventItems(Item item, List<CommandItem> items)
        {
            if (item.FQID.FolderType == FolderType.No)
            {
                var fqid = item.FQID;
                items.Add(new CommandItem
                {
                    Name = "Event: " + item.Name,
                    Group = "Events",
                    Category = ItemCategory.Event,
                    Execute = () =>
                    {
                        try
                        {
                            EnvironmentManager.Instance.SendMessage(
                                new Message(MessageId.Control.TriggerCommand) { RelatedFQID = fqid }, fqid);
                            Log.Info($"Event triggered: {item.Name}");
                        }
                        catch (Exception ex) { Log.Error($"TriggerEvent failed: {item.Name}", ex); }
                    }
                });
            }
            else
            {
                foreach (var child in item.GetChildren())
                    CollectEventItems(child, items);
            }
        }
    }
}
