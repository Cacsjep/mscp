using System;
using System.Collections.Generic;
using VideoOS.Platform;
using VideoOS.Platform.Client;
using VideoOS.Platform.Messaging;

namespace SmartBar.Client
{
    static class SmartBarHistory
    {
        private static readonly LinkedList<HistoryEntry> _history = new LinkedList<HistoryEntry>();
        private static int _maxHistory = 20;
        private static object _viewReceiver;
        private static bool _suppressNext;
        private static DateTime _lastViewChange = DateTime.MinValue;
        private const int CameraSuppressMs = 2000; // ignore camera changes for 2s after view switch

        // Camera tracking — viewer close/create cycle detection
        private static readonly Dictionary<ImageViewerAddOn, int> _viewerSlots = new Dictionary<ImageViewerAddOn, int>();
        private static int _nextSlotIndex;
        private static FQID _pendingClosedCamera;
        private static int _pendingClosedSlot = -1;
        private static DateTime _pendingCloseTime = DateTime.MinValue;
        private static int _consecutiveCloses; // >1 means view switch, not camera swap
        private const int CloseCreateWindowMs = 500;
        private static bool _suppressCameraSwap; // prevent GoBack from creating new history

        public static void Install()
        {
            _viewReceiver = EnvironmentManager.Instance.RegisterReceiver(
                OnViewChanged,
                new MessageIdFilter(MessageId.SmartClient.SelectedViewChangedIndication));

            ClientControl.Instance.NewImageViewerControlEvent += OnNewImageViewer;
        }

        public static void Uninstall()
        {
            if (_viewReceiver != null)
            {
                EnvironmentManager.Instance.UnRegisterReceiver(_viewReceiver);
                _viewReceiver = null;
            }

            ClientControl.Instance.NewImageViewerControlEvent -= OnNewImageViewer;

            foreach (var kv in _viewerSlots)
            {
                kv.Key.CloseEvent -= OnViewerClose;
            }
            _viewerSlots.Clear();
        }

        private static void OnNewImageViewer(ImageViewerAddOn viewer)
        {
            int slotIndex;

            // A camera swap is exactly 1 close → 1 create in quick succession.
            // Multiple consecutive closes = view switch (not a camera swap).
            var msSinceClose = (DateTime.UtcNow - _pendingCloseTime).TotalMilliseconds;
            bool isCameraSwap = _pendingClosedSlot >= 0
                && _consecutiveCloses == 1
                && msSinceClose < CloseCreateWindowMs
                && !_suppressCameraSwap;

            if (isCameraSwap)
            {
                slotIndex = _pendingClosedSlot;
            }
            else
            {
                // Multiple closes = view switch; reset slot counter for the new view's viewers
                if (_consecutiveCloses > 1)
                    _nextSlotIndex = 0;
                slotIndex = _nextSlotIndex++;
            }

            _viewerSlots[viewer] = slotIndex;
            viewer.CloseEvent += OnViewerClose;
            _consecutiveCloses = 0; // reset on create

            var cam = viewer.CameraFQID;
            System.Diagnostics.Debug.WriteLine(
                $"[SmartBar] NewImageViewer slot {slotIndex}: CameraFQID={(cam != null ? cam.ObjectId.ToString() : "null")}, consCloses={_consecutiveCloses}");

            if (isCameraSwap)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[SmartBar] Camera swap detected in slot {_pendingClosedSlot}: " +
                    $"{(_pendingClosedCamera != null ? _pendingClosedCamera.ObjectId.ToString() : "empty")} -> " +
                    $"{(cam != null ? cam.ObjectId.ToString() : "empty")}");

                Push(new HistoryEntry
                {
                    Type = HistoryType.Camera,
                    CameraFQID = _pendingClosedCamera,
                    SlotIndex = _pendingClosedSlot
                });
            }
            else if (_suppressCameraSwap)
            {
                System.Diagnostics.Debug.WriteLine("[SmartBar] NewImageViewer: suppressed (GoBack in progress)");
                _suppressCameraSwap = false;
            }

            // Clear pending
            _pendingClosedCamera = null;
            _pendingClosedSlot = -1;
        }

        private static void OnViewerClose(object sender, EventArgs e)
        {
            var viewer = (ImageViewerAddOn)sender;
            viewer.CloseEvent -= OnViewerClose;

            int slotIndex = -1;
            FQID closedCamera = viewer.CameraFQID;
            if (_viewerSlots.TryGetValue(viewer, out slotIndex))
                _viewerSlots.Remove(viewer);

            System.Diagnostics.Debug.WriteLine(
                $"[SmartBar] ViewerClose slot {slotIndex}: CameraFQID={(closedCamera != null ? closedCamera.ObjectId.ToString() : "null")}");

            // Store pending close info for camera swap detection
            _consecutiveCloses++;
            _pendingClosedCamera = closedCamera;
            _pendingClosedSlot = slotIndex;
            _pendingCloseTime = DateTime.UtcNow;
        }

        private static object OnViewChanged(Message message, FQID dest, FQID source)
        {
            if (_suppressNext)
            {
                _suppressNext = false;
                return null;
            }

            _lastViewChange = DateTime.UtcNow;

            var viewItem = message.Data as ViewAndLayoutItem;
            if (viewItem?.FQID == null) return null;

            // Don't add duplicate
            if (_history.Count > 0)
            {
                var last = _history.Last.Value;
                if (last.Type == HistoryType.View && last.ViewFQID?.ObjectId == viewItem.FQID.ObjectId)
                    return null;
            }

            System.Diagnostics.Debug.WriteLine($"[SmartBar] View history push: {viewItem.Name} (total: {_history.Count + 1})");

            Push(new HistoryEntry
            {
                Type = HistoryType.View,
                ViewFQID = viewItem.FQID
            });

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
            System.Diagnostics.Debug.WriteLine($"[SmartBar] GoBack called, history count: {_history.Count}");
            if (_history.Count == 0) return;

            var entry = _history.Last.Value;
            _history.RemoveLast();
            System.Diagnostics.Debug.WriteLine($"[SmartBar] GoBack: popped {entry.Type}, remaining: {_history.Count}");

            if (entry.Type == HistoryType.View)
            {
                // Need to find the view before this one to go back to
                // The entry we just popped IS the current view, so find previous view
                FQID previousView = null;
                var node = _history.Last;
                while (node != null)
                {
                    if (node.Value.Type == HistoryType.View)
                    {
                        previousView = node.Value.ViewFQID;
                        break;
                    }
                    node = node.Previous;
                }

                if (previousView == null)
                {
                    System.Diagnostics.Debug.WriteLine("[SmartBar] GoBack: no previous view found");
                    return;
                }

                _suppressNext = true;
                var windows = Configuration.Instance.GetItemsByKind(Kind.Window);
                var dest = windows.Count > 0 ? windows[0].FQID : null;

                System.Diagnostics.Debug.WriteLine("[SmartBar] GoBack: navigating to previous view");
                EnvironmentManager.Instance.SendMessage(
                    new Message(MessageId.SmartClient.MultiWindowCommand,
                        new MultiWindowCommandData
                        {
                            MultiWindowCommand = MultiWindowCommand.SetViewInWindow,
                            View = previousView,
                            Window = dest
                        }), dest);
            }
            else if (entry.Type == HistoryType.Camera)
            {
                var windows = Configuration.Instance.GetItemsByKind(Kind.Window);
                var dest = windows.Count > 0 ? windows[0].FQID : null;

                System.Diagnostics.Debug.WriteLine($"[SmartBar] GoBack: restoring camera in slot {entry.SlotIndex}");
                _suppressCameraSwap = true;
                EnvironmentManager.Instance.SendMessage(
                    new Message(MessageId.SmartClient.SetCameraInViewCommand,
                        new SetCameraInViewCommandData
                        {
                            Index = entry.SlotIndex,
                            CameraFQID = entry.CameraFQID
                        }), dest);
            }
        }
    }

    enum HistoryType { View, Camera }

    class HistoryEntry
    {
        public HistoryType Type;
        public FQID ViewFQID;
        public FQID CameraFQID;
        public int SlotIndex;
    }
}
