using System;
using System.Collections.Generic;
using System.Linq;
using CommunitySDK;
using VideoOS.Platform;
using VideoOS.Platform.Client;
using VideoOS.Platform.Messaging;

namespace SmartBar.Client
{
    static class SmartBarHistory
    {
        private static readonly PluginLog Log = SmartBarDefinition.Log;
        private static readonly LinkedList<HistoryEntry> _history = new LinkedList<HistoryEntry>();
        private static int _maxHistory = 20;
        private static object _viewReceiver;
        private static bool _suppressNextViewChange;

        // Recent items: last N unique cameras/views used
        private static readonly List<RecentItem> _recentItems = new List<RecentItem>();

        // GoBack suppression: time-based to cover all cascading close/create cycles
        private static DateTime _goBackTime = DateTime.MinValue;
        private const int GoBackSuppressMs = 1000;

        // Per-viewer tracking: slot index + which window
        private static readonly Dictionary<ImageViewerAddOn, ViewerInfo> _viewerData = new Dictionary<ImageViewerAddOn, ViewerInfo>();
        private static readonly Dictionary<Guid, int> _nextSlotPerWindow = new Dictionary<Guid, int>();

        // Camera swap detection
        private static FQID _pendingClosedCamera;
        private static int _pendingClosedSlot = -1;
        private static Guid _pendingClosedWindowId;
        private static DateTime _pendingCloseTime = DateTime.MinValue;
        private static int _consecutiveCloses;
        private const int CloseCreateWindowMs = 500;

        // View destroy snapshot: cameras captured during batch close
        private static readonly List<SlotCamera> _pendingViewSnapshot = new List<SlotCamera>();

        // Window tracking
        private static Guid _currentBatchWindowId;
        private static int _knownWindowCount;
        private static bool _viewBatchOccurred;

        struct ViewerInfo
        {
            public int SlotIndex;
            public Guid WindowId;
        }

        public static void Install()
        {
            _viewReceiver = EnvironmentManager.Instance.RegisterReceiver(
                OnViewChanged,
                new MessageIdFilter(MessageId.SmartClient.SelectedViewChangedIndication));

            ClientControl.Instance.NewImageViewerControlEvent += OnNewImageViewer;

            var windows = Configuration.Instance.GetItemsByKind(Kind.Window);
            _knownWindowCount = windows.Count;
            if (windows.Count > 0)
            {
                _currentBatchWindowId = windows[0].FQID.ObjectId;
                _nextSlotPerWindow[_currentBatchWindowId] = 0;
                _viewBatchOccurred = true;
            }

            Log.Info("History tracking installed");
        }

        public static void Uninstall()
        {
            if (_viewReceiver != null)
            {
                EnvironmentManager.Instance.UnRegisterReceiver(_viewReceiver);
                _viewReceiver = null;
            }

            ClientControl.Instance.NewImageViewerControlEvent -= OnNewImageViewer;

            foreach (var kv in _viewerData)
                kv.Key.CloseEvent -= OnViewerClose;
            _viewerData.Clear();
            _nextSlotPerWindow.Clear();
        }

        private static void OnNewImageViewer(ImageViewerAddOn viewer)
        {
            try
            {
                int slotIndex;
                Guid windowId;

                var msSinceClose = (DateTime.UtcNow - _pendingCloseTime).TotalMilliseconds;
                bool goBackActive = (DateTime.UtcNow - _goBackTime).TotalMilliseconds < GoBackSuppressMs;

                bool isCameraSwap = _pendingClosedSlot >= 0
                    && _consecutiveCloses == 1
                    && msSinceClose < CloseCreateWindowMs
                    && !goBackActive;

                if (isCameraSwap)
                {
                    // Camera swap: inherit slot + window from closed viewer
                    slotIndex = _pendingClosedSlot;
                    windowId = _pendingClosedWindowId;
                }
                else if (goBackActive && _pendingClosedSlot >= 0 && _consecutiveCloses == 1)
                {
                    // GoBack single-camera replacement: inherit slot + window from closed viewer
                    slotIndex = _pendingClosedSlot;
                    windowId = _pendingClosedWindowId;
                }
                else
                {
                    if (_consecutiveCloses > 1)
                    {
                        // View switch batch: inherit window, reset slot counter
                        _currentBatchWindowId = _pendingClosedWindowId;
                        _nextSlotPerWindow[_currentBatchWindowId] = 0;
                    }
                    else if (_pendingClosedSlot == -1)
                    {
                        // No preceding close: startup or new window opened
                        var windows = Configuration.Instance.GetItemsByKind(Kind.Window);
                        if (windows.Count > _knownWindowCount)
                        {
                            _currentBatchWindowId = windows[windows.Count - 1].FQID.ObjectId;
                            _knownWindowCount = windows.Count;
                            _nextSlotPerWindow[_currentBatchWindowId] = 0;
                            Log.Info($"New window detected: {_currentBatchWindowId}");
                        }
                    }

                    if (!_nextSlotPerWindow.ContainsKey(_currentBatchWindowId))
                        _nextSlotPerWindow[_currentBatchWindowId] = 0;

                    slotIndex = _nextSlotPerWindow[_currentBatchWindowId]++;
                    windowId = _currentBatchWindowId;
                    if (!goBackActive)
                        _viewBatchOccurred = true;
                }

                _viewerData[viewer] = new ViewerInfo { SlotIndex = slotIndex, WindowId = windowId };
                viewer.CloseEvent += OnViewerClose;
                _consecutiveCloses = 0;

                var cam = viewer.CameraFQID;
                var camId = cam != null ? cam.ObjectId.ToString() : "null";
                Log.Info($"NewViewer slot={slotIndex} win={windowId} cam={camId} swap={isCameraSwap} goBack={goBackActive}");

                if (isCameraSwap)
                {
                    var prevId = _pendingClosedCamera != null ? _pendingClosedCamera.ObjectId.ToString() : "empty";
                    Log.Info($"PUSH Camera: slot={slotIndex} win={windowId} prev={prevId} -> {camId}");
                    var newCamName = ResolveCameraName(cam);
                    var desc = _pendingClosedCamera == null
                        ? $"Camera: {newCamName} added to slot {slotIndex}"
                        : $"Camera: {newCamName} replaced in slot {slotIndex}";
                    Push(new HistoryEntry
                    {
                        Type = HistoryType.Camera,
                        CameraFQID = _pendingClosedCamera,
                        SlotIndex = slotIndex,
                        WindowId = windowId,
                        Description = desc
                    });
                    if (cam != null)
                        AddRecent(RecentType.Camera, cam, newCamName);
                }

                _pendingClosedCamera = null;
                _pendingClosedSlot = -1;
            }
            catch (Exception ex)
            {
                Log.Error("OnNewImageViewer failed", ex);
            }
        }

        private static void OnViewerClose(object sender, EventArgs e)
        {
            try
            {
                var viewer = (ImageViewerAddOn)sender;
                viewer.CloseEvent -= OnViewerClose;

                int slotIndex = -1;
                Guid windowId = Guid.Empty;
                FQID closedCamera = viewer.CameraFQID;

                ViewerInfo info;
                if (_viewerData.TryGetValue(viewer, out info))
                {
                    _viewerData.Remove(viewer);
                    slotIndex = info.SlotIndex;
                    windowId = info.WindowId;
                }

                // Reset counter if last close was long ago (stale from window close etc.)
                if ((DateTime.UtcNow - _pendingCloseTime).TotalMilliseconds > CloseCreateWindowMs)
                {
                    _consecutiveCloses = 0;
                    _pendingViewSnapshot.Clear();
                }

                _consecutiveCloses++;
                _pendingClosedCamera = closedCamera;
                _pendingClosedSlot = slotIndex;
                _pendingClosedWindowId = windowId;
                _pendingCloseTime = DateTime.UtcNow;

                // Capture camera for view destroy snapshot (skip during GoBack)
                bool goBackActive = (DateTime.UtcNow - _goBackTime).TotalMilliseconds < GoBackSuppressMs;
                if (closedCamera != null && !goBackActive)
                    _pendingViewSnapshot.Add(new SlotCamera { SlotIndex = slotIndex, CameraFQID = closedCamera });

                Log.Info($"ViewerClose slot={slotIndex} win={windowId} cam={(closedCamera != null ? closedCamera.ObjectId.ToString() : "null")} consCloses={_consecutiveCloses}");
            }
            catch (Exception ex)
            {
                Log.Error("OnViewerClose failed", ex);
            }
        }

        private static object OnViewChanged(Message message, FQID dest, FQID source)
        {
            try
            {
                if (_suppressNextViewChange)
                {
                    _suppressNextViewChange = false;
                    Log.Info("View change suppressed (GoBack)");
                    return null;
                }

                var viewItem = message.Data as ViewAndLayoutItem;
                if (viewItem?.FQID == null) return null;

                Guid windowId;
                if (_viewBatchOccurred)
                {
                    // Normal view switch: NewViewers were created, use their window
                    _viewBatchOccurred = false;
                    windowId = _currentBatchWindowId;
                }
                else if (_consecutiveCloses > 1)
                {
                    // View switch to non-ImageViewer view (e.g., mixed layouts):
                    // batch close happened but no new ImageViewers were created
                    windowId = _pendingClosedWindowId;
                    Log.Info($"OnViewChanged: no new viewers but batch close detected ({_consecutiveCloses} closes)");
                }
                else
                {
                    Log.Info($"OnViewChanged: no viewer batch, ignoring focus event for {viewItem.Name}");
                    return null;
                }

                // Don't add duplicate for same view on same window
                if (_history.Count > 0)
                {
                    var last = _history.Last.Value;
                    if (last.Type == HistoryType.View
                        && last.ViewFQID?.ObjectId == viewItem.FQID.ObjectId
                        && last.WindowId == windowId)
                    {
                        Log.Info($"OnViewChanged: duplicate {viewItem.Name} on win={windowId}, skipping");
                        return null;
                    }
                }

                var snapshot = _pendingViewSnapshot.Count > 0 ? new List<SlotCamera>(_pendingViewSnapshot) : null;
                _pendingViewSnapshot.Clear();

                Log.Info($"PUSH View: {viewItem.Name} win={windowId} snapshot={snapshot?.Count ?? 0} (history will be {_history.Count + 1})");

                Push(new HistoryEntry
                {
                    Type = HistoryType.View,
                    ViewFQID = viewItem.FQID,
                    WindowId = windowId,
                    Description = "View: " + viewItem.Name,
                    ViewSnapshot = snapshot
                });
                AddRecent(RecentType.View, viewItem.FQID, viewItem.Name);
            }
            catch (Exception ex)
            {
                Log.Error("OnViewChanged failed", ex);
            }

            return null;
        }

        private static void Push(HistoryEntry entry)
        {
            _history.AddLast(entry);
            if (_history.Count > _maxHistory)
                _history.RemoveFirst();
        }

        public static void ApplyMaxHistory(int max)
        {
            _maxHistory = max;
            while (_history.Count > _maxHistory)
                _history.RemoveFirst();
        }

        public static bool CanGoBack => _history.Count > 0;

        public static void GoBack()
        {
            if (_history.Count == 0) return;

            var entry = _history.Last.Value;
            _history.RemoveLast();

            _goBackTime = DateTime.UtcNow;

            Log.Info($"GoBack: popped {entry.Type} win={entry.WindowId} slot={entry.SlotIndex}, remaining: {_history.Count}");

            try
            {
                FQID dest = FindWindowFQID(entry.WindowId);
                if (dest == null)
                {
                    Log.Info("GoBack: target window not found, skipping");
                    return;
                }

                if (entry.Type == HistoryType.View)
                {
                    // Find previous view on the SAME window
                    FQID previousView = null;
                    var node = _history.Last;
                    while (node != null)
                    {
                        if (node.Value.Type == HistoryType.View && node.Value.WindowId == entry.WindowId)
                        {
                            previousView = node.Value.ViewFQID;
                            break;
                        }
                        node = node.Previous;
                    }

                    if (previousView == null)
                    {
                        Log.Info("GoBack: no previous view for this window");
                        return;
                    }

                    _suppressNextViewChange = true;
                    Log.Info($"GoBack: SetViewInWindow view={previousView.ObjectId} dest={dest.ObjectId}");

                    EnvironmentManager.Instance.SendMessage(
                        new Message(MessageId.SmartClient.MultiWindowCommand,
                            new MultiWindowCommandData
                            {
                                MultiWindowCommand = MultiWindowCommand.SetViewInWindow,
                                View = previousView,
                                Window = dest
                            }), dest);

                    // Re-apply cameras that were in the destroyed view
                    if (entry.ViewSnapshot != null)
                    {
                        foreach (var sc in entry.ViewSnapshot)
                        {
                            Log.Info($"GoBack: restore camera slot={sc.SlotIndex} cam={sc.CameraFQID?.ObjectId}");
                            EnvironmentManager.Instance.SendMessage(
                                new Message(MessageId.SmartClient.SetCameraInViewCommand,
                                    new SetCameraInViewCommandData
                                    {
                                        Index = sc.SlotIndex,
                                        CameraFQID = sc.CameraFQID
                                    }), dest);
                        }
                    }
                }
                else if (entry.Type == HistoryType.Camera)
                {
                    Log.Info($"GoBack: SetCameraInView slot={entry.SlotIndex} cam={entry.CameraFQID?.ObjectId} dest={dest.ObjectId}");

                    EnvironmentManager.Instance.SendMessage(
                        new Message(MessageId.SmartClient.SetCameraInViewCommand,
                            new SetCameraInViewCommandData
                            {
                                Index = entry.SlotIndex,
                                CameraFQID = entry.CameraFQID
                            }), dest);
                }
            }
            catch (Exception ex)
            {
                Log.Error("GoBack failed", ex);
            }
        }

        private static string ResolveCameraName(FQID cameraFQID)
        {
            if (cameraFQID == null) return "Unknown";
            try
            {
                var item = Configuration.Instance.GetItem(cameraFQID);
                if (item != null) return item.Name;
            }
            catch { }
            return cameraFQID.ObjectId.ToString().Substring(0, 8);
        }

        /// <summary>Returns history descriptions from newest (index 0) to oldest.</summary>
        public static List<string> GetHistoryDescriptions()
        {
            var result = new List<string>();
            var node = _history.Last;
            var windowNumbers = new Dictionary<Guid, int>();
            // Pre-assign window numbers
            var windows = Configuration.Instance.GetItemsByKind(Kind.Window);
            for (int i = 0; i < windows.Count; i++)
                windowNumbers[windows[i].FQID.ObjectId] = i + 1;

            while (node != null)
            {
                var entry = node.Value;
                var winLabel = windowNumbers.ContainsKey(entry.WindowId)
                    ? $"W{windowNumbers[entry.WindowId]}"
                    : "W?";
                result.Add($"[{winLabel}] {entry.Description ?? entry.Type.ToString()}");
                node = node.Previous;
            }
            return result;
        }

        public static void GoBackN(int count)
        {
            for (int i = 0; i < count && _history.Count > 0; i++)
                GoBack();
        }

        private static void AddRecent(RecentType type, FQID fqid, string name)
        {
            if (fqid == null || !SmartBarConfig.ShowRecent) return;
            var id = fqid.ObjectId;
            _recentItems.RemoveAll(r => r.ObjectId == id);
            _recentItems.Insert(0, new RecentItem { Type = type, ObjectId = id, Name = name });
            while (_recentItems.Count > SmartBarConfig.MaxRecent)
                _recentItems.RemoveAt(_recentItems.Count - 1);
        }

        public static List<RecentItem> GetRecentItems()
        {
            return _recentItems.ToList();
        }

        private static FQID FindWindowFQID(Guid windowId)
        {
            var windows = Configuration.Instance.GetItemsByKind(Kind.Window);
            foreach (var w in windows)
            {
                if (w.FQID.ObjectId == windowId)
                    return w.FQID;
            }
            return windows.Count > 0 ? windows[0].FQID : null;
        }
    }

    enum HistoryType { View, Camera }
    enum RecentType { Camera, View }

    struct RecentItem
    {
        public RecentType Type;
        public Guid ObjectId;
        public string Name;
    }

    class HistoryEntry
    {
        public HistoryType Type;
        public FQID ViewFQID;
        public FQID CameraFQID;
        public Guid WindowId;
        public int SlotIndex;
        public string Description;
        /// <summary>Cameras that were in the destroyed view when this view change occurred.</summary>
        public List<SlotCamera> ViewSnapshot;
    }

    struct SlotCamera
    {
        public int SlotIndex;
        public FQID CameraFQID;
    }
}
