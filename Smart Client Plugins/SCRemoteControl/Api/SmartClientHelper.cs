using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows;
using VideoOS.Platform;
using VideoOS.Platform.Client;
using VideoOS.Platform.ConfigurationItems;
using VideoOS.Platform.Messaging;

namespace SCRemoteControl.Api
{
    /// <summary>
    /// Shared helper for discovering Smart Client items and dispatching commands.
    /// All SendMessage calls MUST go through the WPF dispatcher (UI thread).
    /// </summary>
    static class SmartClientHelper
    {
        private const string ViewGroupName = "SC Remote Control";

        private static readonly (int rows, int cols)[] GridLayouts =
        {
            (1, 1), (1, 2), (1, 3), (2, 2), (2, 3),
            (2, 4), (3, 3), (3, 4), (4, 4), (4, 5)
        };

        // --- Dispatch to UI thread ---

        public static void RunOnUiThread(Action action)
        {
            var app = Application.Current;
            if (app != null)
                app.Dispatcher.BeginInvoke(action);
        }

        // --- Item Discovery ---

        public static List<Dictionary<string, object>> GetViews()
        {
            var results = new List<Dictionary<string, object>>();
            try
            {
                var groups = ClientControl.Instance.GetViewGroupItems();
                if (groups == null) return results;
                foreach (var group in groups)
                    CollectViews(group, "", results);
            }
            catch (Exception ex)
            {
                SCRemoteControlDefinition.Log.Error("GetViews failed", ex);
            }
            return results;
        }

        private static void CollectViews(Item item, string parentPath, List<Dictionary<string, object>> results)
        {
            var path = string.IsNullOrEmpty(parentPath) ? item.Name : parentPath + " / " + item.Name;
            var children = item.GetChildren();

            if (children == null || children.Count == 0)
            {
                // Leaf node = view
                results.Add(new Dictionary<string, object>
                {
                    ["id"] = item.FQID.ObjectId.ToString(),
                    ["name"] = item.Name,
                    ["path"] = path
                });
            }
            else
            {
                foreach (var child in children)
                    CollectViews(child, path, results);
            }
        }

        public static List<Dictionary<string, object>> GetCameras()
        {
            var results = new List<Dictionary<string, object>>();
            try
            {
                var items = Configuration.Instance.GetItemsByKind(Kind.Camera);
                foreach (var item in items)
                {
                    results.Add(new Dictionary<string, object>
                    {
                        ["id"] = item.FQID.ObjectId.ToString(),
                        ["name"] = item.Name,
                        ["path"] = item.Name
                    });
                }
            }
            catch (Exception ex)
            {
                SCRemoteControlDefinition.Log.Error("GetCameras failed", ex);
            }
            return results;
        }

        public static List<Dictionary<string, object>> GetWorkspaces()
        {
            var results = new List<Dictionary<string, object>>();
            try
            {
                var workspaces = ClientControl.Instance.GetWorkSpaceItems();
                if (workspaces != null)
                {
                    foreach (var ws in workspaces)
                    {
                        results.Add(new Dictionary<string, object>
                        {
                            ["id"] = ws.FQID.ObjectId.ToString(),
                            ["name"] = ws.Name
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                SCRemoteControlDefinition.Log.Error("GetWorkspaces failed", ex);
            }
            return results;
        }

        public static List<Dictionary<string, object>> GetWindows()
        {
            var results = new List<Dictionary<string, object>>();
            try
            {
                var windows = Configuration.Instance.GetItemsByKind(Kind.Window);
                int idx = 0;
                foreach (var w in windows)
                {
                    results.Add(new Dictionary<string, object>
                    {
                        ["id"] = w.FQID.ObjectId.ToString(),
                        ["name"] = w.Name,
                        ["index"] = idx++
                    });
                }
            }
            catch (Exception ex)
            {
                SCRemoteControlDefinition.Log.Error("GetWindows failed", ex);
            }
            return results;
        }

        // --- FQID Resolution ---

        public static FQID FindViewFqid(string objectId)
        {
            if (!Guid.TryParse(objectId, out var guid)) return null;
            var groups = ClientControl.Instance.GetViewGroupItems();
            if (groups == null) return null;
            foreach (var group in groups)
            {
                var found = FindFqidInTree(group, guid);
                if (found != null) return found;
            }
            return null;
        }

        private static FQID FindFqidInTree(Item item, Guid objectId)
        {
            if (item.FQID.ObjectId == objectId) return item.FQID;
            var children = item.GetChildren();
            if (children == null) return null;
            foreach (var child in children)
            {
                var found = FindFqidInTree(child, objectId);
                if (found != null) return found;
            }
            return null;
        }

        public static FQID FindItemFqid(string objectId, Guid kind)
        {
            if (!Guid.TryParse(objectId, out var guid)) return null;
            var items = Configuration.Instance.GetItemsByKind(kind);
            var item = items.FirstOrDefault(i => i.FQID.ObjectId == guid);
            return item?.FQID;
        }

        public static FQID FindWorkspaceFqid(string objectId)
        {
            if (!Guid.TryParse(objectId, out var guid)) return null;
            var workspaces = ClientControl.Instance.GetWorkSpaceItems();
            if (workspaces == null) return null;
            var ws = workspaces.FirstOrDefault(w => w.FQID.ObjectId == guid);
            return ws?.FQID;
        }

        public static FQID GetWindowFqid(int windowIndex)
        {
            var windows = Configuration.Instance.GetItemsByKind(Kind.Window);
            if (windowIndex >= 0 && windowIndex < windows.Count)
                return windows[windowIndex].FQID;
            return windows.Count > 0 ? windows[0].FQID : null;
        }

        // --- View Management (standalone, independent of SmartBar) ---

        public static void EnsureRemoteControlViews()
        {
            try
            {
                var groups = ClientControl.Instance.GetViewGroupItems();
                if (groups == null || groups.Count == 0) return;

                var topGroup = groups[0] as ConfigItem;
                if (topGroup == null) return;

                var rcGroup = topGroup.GetChildren()
                    .FirstOrDefault(c => c.Name == ViewGroupName) as ConfigItem;

                if (rcGroup == null)
                    rcGroup = topGroup.AddChild(ViewGroupName, Kind.View, FolderType.UserDefined);

                if (rcGroup == null) return;

                var existing = rcGroup.GetChildren().Select(c => c.Name).ToHashSet();
                bool changed = false;

                foreach (var (rows, cols) in GridLayouts)
                {
                    int total = rows * cols;
                    string name = $"{rows}x{cols}";
                    if (existing.Contains(name)) continue;

                    var rects = new Rectangle[total];
                    int cellW = 1000 / cols;
                    int cellH = 1000 / rows;
                    for (int i = 0; i < total; i++)
                    {
                        int col = i % cols;
                        int row = i / cols;
                        rects[i] = new Rectangle(col * cellW, row * cellH, cellW, cellH);
                    }

                    var view = rcGroup.AddChild(name, Kind.View, FolderType.No) as ViewAndLayoutItem;
                    if (view == null) continue;

                    view.Layout = rects;
                    for (int i = 0; i < total; i++)
                    {
                        view.InsertBuiltinViewItem(i, ViewAndLayoutItem.CameraBuiltinId,
                            new Dictionary<string, string> { { "CameraId", Guid.Empty.ToString() } });
                    }
                    view.Save();
                    changed = true;
                }

                if (changed)
                    topGroup.PropertiesModified();
            }
            catch (Exception ex)
            {
                SCRemoteControlDefinition.Log.Error("EnsureRemoteControlViews failed", ex);
            }
        }

        public static FQID FindGridViewFqid(int cameraCount)
        {
            var layout = GridLayouts.FirstOrDefault(g => g.rows * g.cols >= cameraCount);
            if (layout.rows == 0) layout = GridLayouts[GridLayouts.Length - 1];

            string viewName = $"{layout.rows}x{layout.cols}";

            var groups = ClientControl.Instance.GetViewGroupItems();
            if (groups == null || groups.Count == 0) return null;

            var topGroup = groups[0];
            var rcGroup = topGroup.GetChildren()?.FirstOrDefault(c => c.Name == ViewGroupName);
            if (rcGroup == null) return null;

            var viewItem = rcGroup.GetChildren()?.FirstOrDefault(c => c.Name == viewName);
            return viewItem?.FQID;
        }

        // --- Commands ---

        public static void SwitchView(FQID viewFqid, FQID windowFqid)
        {
            RunOnUiThread(() =>
            {
                EnvironmentManager.Instance.SendMessage(
                    new Message(MessageId.SmartClient.MultiWindowCommand,
                        new MultiWindowCommandData
                        {
                            MultiWindowCommand = MultiWindowCommand.SetViewInWindow,
                            View = viewFqid,
                            Window = windowFqid
                        }), windowFqid);
            });
        }

        public static void SetCameraInSlot(int index, FQID cameraFqid, FQID windowFqid)
        {
            RunOnUiThread(() =>
            {
                EnvironmentManager.Instance.SendMessage(
                    new Message(MessageId.SmartClient.SetCameraInViewCommand,
                        new SetCameraInViewCommandData
                        {
                            Index = index,
                            CameraFQID = cameraFqid
                        }), windowFqid);
            });
        }

        public static void ShowCameras(List<FQID> cameraFqids, FQID windowFqid)
        {
            EnsureRemoteControlViews();

            var gridViewFqid = FindGridViewFqid(cameraFqids.Count);
            if (gridViewFqid == null) return;

            RunOnUiThread(() =>
            {
                // Switch to grid view first
                EnvironmentManager.Instance.SendMessage(
                    new Message(MessageId.SmartClient.MultiWindowCommand,
                        new MultiWindowCommandData
                        {
                            MultiWindowCommand = MultiWindowCommand.SetViewInWindow,
                            View = gridViewFqid,
                            Window = windowFqid
                        }), windowFqid);

                // Then set cameras in slots
                for (int i = 0; i < cameraFqids.Count; i++)
                {
                    EnvironmentManager.Instance.SendMessage(
                        new Message(MessageId.SmartClient.SetCameraInViewCommand,
                            new SetCameraInViewCommandData
                            {
                                Index = i,
                                CameraFQID = cameraFqids[i]
                            }), windowFqid);
                }
            });
        }

        public static void SwitchWorkspace(FQID workspaceFqid)
        {
            RunOnUiThread(() =>
            {
                EnvironmentManager.Instance.SendMessage(
                    new Message(MessageId.SmartClient.ShowWorkSpaceCommand, workspaceFqid));
            });
        }

        public static void ChangeMode(WorkSpaceState state)
        {
            RunOnUiThread(() =>
            {
                EnvironmentManager.Instance.SendMessage(
                    new Message(MessageId.SmartClient.ChangeWorkSpaceStateCommand, null, state));
            });
        }

        public static void SendApplicationControl(string command)
        {
            RunOnUiThread(() =>
            {
                EnvironmentManager.Instance.SendMessage(
                    new Message(MessageId.SmartClient.ApplicationControlCommand, command));
            });
        }

        public static void CloseWindow(FQID windowFqid)
        {
            RunOnUiThread(() =>
            {
                EnvironmentManager.Instance.SendMessage(
                    new Message(MessageId.SmartClient.MultiWindowCommand,
                        new MultiWindowCommandData
                        {
                            MultiWindowCommand = MultiWindowCommand.CloseSelectedWindow,
                            Window = windowFqid
                        }), windowFqid);
            });
        }

        public static void CloseAllWindows()
        {
            RunOnUiThread(() =>
            {
                EnvironmentManager.Instance.SendMessage(
                    new Message(MessageId.SmartClient.MultiWindowCommand,
                        new MultiWindowCommandData
                        {
                            MultiWindowCommand = MultiWindowCommand.CloseAllWindows
                        }));
            });
        }

        public static void ClearView(FQID windowFqid)
        {
            // Switch to empty 1x1 grid view (no cameras assigned)
            EnsureRemoteControlViews();
            var emptyViewFqid = FindGridViewFqid(1);
            if (emptyViewFqid != null)
                SwitchView(emptyViewFqid, windowFqid);
        }
    }
}
