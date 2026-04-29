using CommunitySDK;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using VideoOS.Platform;
using VideoOS.Platform.ConfigurationItems;

namespace ColoredTimeline.Admin
{
    // Discovers camera-applicable EventType names once at plugin load and caches
    // them. UC consumers read Items directly; if a load is still in flight they
    // subscribe to Loaded for an apply-on-completion callback.
    internal static class EventTypeCache
    {
        public struct Entry
        {
            public string Group;
            public string Name;
            public Entry(string g, string n) { Group = g; Name = n; }
        }

        private static readonly object _lock = new object();
        private static readonly PluginLog _log = new PluginLog("ColoredTimeline - EventTypeCache");
        private static volatile bool _loaded;
        private static volatile bool _loading;
        private static List<Entry> _items = new List<Entry>();

        public static event EventHandler Loaded;

        public static bool IsLoaded => _loaded;

        public static IList<Entry> Items
        {
            get { lock (_lock) return _items.ToList(); }
        }

        public static void StartLoad()
        {
            lock (_lock)
            {
                if (_loaded || _loading) return;
                _loading = true;
            }

            ThreadPool.QueueUserWorkItem(_ =>
            {
                var found = new List<Entry>();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    var ms = new ManagementServer(EnvironmentManager.Instance.MasterSite);
                    var groupFolder = ms.EventTypeGroupFolder;
                    if (groupFolder?.EventTypeGroups != null)
                    {
                        foreach (var grp in groupFolder.EventTypeGroups)
                        {
                            string grpName = null;
                            try { grpName = grp?.DisplayName ?? grp?.Name; } catch { }
                            if (string.IsNullOrEmpty(grpName)) continue;
                            // Only "Device - Predefined" and "Device - Configurable".
                            if (!grpName.StartsWith("Device -", StringComparison.OrdinalIgnoreCase)) continue;

                            EventTypeFolder folder = null;
                            try { folder = grp.EventTypeFolder; } catch { }
                            if (folder?.EventTypes == null) continue;

                            foreach (var et in folder.EventTypes)
                            {
                                if (et == null) continue;
                                var name = !string.IsNullOrEmpty(et.Name) ? et.Name : et.GeneratorName;
                                if (string.IsNullOrEmpty(name)) continue;
                                var key = grpName + "|" + name;
                                if (!seen.Add(key)) continue;
                                found.Add(new Entry(grpName, name));
                            }
                        }
                    }

                    found.Sort((a, b) =>
                    {
                        var c = string.Compare(a.Group, b.Group, StringComparison.OrdinalIgnoreCase);
                        return c != 0 ? c : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                    });

                    lock (_lock)
                    {
                        _items = found;
                        _loaded = true;
                        _loading = false;
                    }
                    _log.Info($"Discovered {found.Count} camera event type(s).");
                }
                catch (Exception ex)
                {
                    lock (_lock) { _loading = false; }
                    _log.Error($"EventTypeCache load failed: {ex.Message}");
                }

                try { Loaded?.Invoke(null, EventArgs.Empty); }
                catch (Exception ex) { _log.Error($"Loaded handler threw: {ex.Message}"); }
            });
        }
    }
}
