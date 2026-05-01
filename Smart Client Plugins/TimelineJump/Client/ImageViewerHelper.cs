using System;
using System.Collections.Generic;
using System.Linq;
using VideoOS.Platform;
using VideoOS.Platform.Client;

namespace TimelineJump
{
    /// <summary>
    /// Tracks live ImageViewerAddOn instances so we can find the operator's currently
    /// selected camera tile and detect when any tile is in independent playback.
    /// Lifted from the SCClientAction sample and extended to surface a global
    /// "any tile in independent playback?" signal so the toolbar button can be
    /// enabled in Live workspace only when there is actually something to jump.
    /// </summary>
    internal static class ImageViewerHelper
    {
        private static readonly object _gate = new object();
        private static readonly List<ImageViewerAddOn> _imageViewerAddOns = new List<ImageViewerAddOn>();
        private static bool _initialized;

        /// <summary>Raised whenever any tracked tile enters or leaves independent playback.</summary>
        public static event EventHandler IndependentPlaybackStateChanged;

        public static void Init()
        {
            lock (_gate)
            {
                if (_initialized) return;
                ClientControl.Instance.NewImageViewerControlEvent += OnNewImageViewer;
                _initialized = true;
            }
        }

        public static void Close()
        {
            ImageViewerAddOn[] snapshot;
            lock (_gate)
            {
                if (!_initialized) return;
                ClientControl.Instance.NewImageViewerControlEvent -= OnNewImageViewer;
                snapshot = _imageViewerAddOns.ToArray();
                _imageViewerAddOns.Clear();
                _initialized = false;
            }
            foreach (var addon in snapshot)
            {
                addon.CloseEvent -= OnAddOnClose;
                addon.IndependentPlaybackModeChangedEvent -= OnIndependentPlaybackModeChanged;
            }
        }

        private static void OnNewImageViewer(ImageViewerAddOn addon)
        {
            if (addon.ImageViewerType != ImageViewerType.CameraViewItem) return;
            bool wasIndependent;
            lock (_gate)
            {
                _imageViewerAddOns.Add(addon);
            }
            // Subscribe outside the lock so a synchronous fire of the event can't deadlock.
            addon.CloseEvent += OnAddOnClose;
            addon.IndependentPlaybackModeChangedEvent += OnIndependentPlaybackModeChanged;
            wasIndependent = addon.IndependentPlaybackEnabled;
            if (wasIndependent) RaiseStateChanged();
        }

        private static void OnAddOnClose(object sender, EventArgs e)
        {
            var addon = (ImageViewerAddOn)sender;
            addon.CloseEvent -= OnAddOnClose;
            addon.IndependentPlaybackModeChangedEvent -= OnIndependentPlaybackModeChanged;
            bool wasIndependent;
            try { wasIndependent = addon.IndependentPlaybackEnabled; }
            catch { wasIndependent = false; }
            lock (_gate)
            {
                _imageViewerAddOns.Remove(addon);
            }
            if (wasIndependent) RaiseStateChanged();
        }

        private static void OnIndependentPlaybackModeChanged(object sender, IndependentPlaybackModeEventArgs e)
        {
            RaiseStateChanged();
        }

        private static void RaiseStateChanged()
        {
            EventHandler handlers = IndependentPlaybackStateChanged;
            if (handlers == null) return;
            try { handlers(null, EventArgs.Empty); }
            catch (Exception ex) { TimelineJumpDefinition.Log.Error("IndependentPlaybackStateChanged handler failed", ex); }
        }

        private static List<ImageViewerAddOn> Snapshot()
        {
            lock (_gate) return _imageViewerAddOns.ToList();
        }

        public static ImageViewerAddOn GetGlobalSelectedImageViewer()
        {
            return Snapshot().FirstOrDefault(x => SafeIsSelected(x));
        }

        /// <summary>True if any tracked tile is currently in independent playback.</summary>
        public static bool AnyIndependentPlayback()
        {
            return Snapshot().Any(x => SafeIsIndependent(x));
        }

        /// <summary>First tile currently in independent playback, or null. Used as a fallback
        /// in Live mode where opening the flyout can take WPF focus and clear the global
        /// selection on the tile.</summary>
        public static ImageViewerAddOn GetFirstIndependentPlayback()
        {
            return Snapshot().FirstOrDefault(x => SafeIsIndependent(x));
        }

        // Property reads on a torn-down ImageViewerAddOn can throw on some SDK builds.
        // Treat any failure as "not selected" / "not independent" - the addon is on its
        // way out anyway and will be removed via OnAddOnClose.
        private static bool SafeIsSelected(ImageViewerAddOn x)
        {
            try { return x.IsGlobalSelected; } catch { return false; }
        }
        private static bool SafeIsIndependent(ImageViewerAddOn x)
        {
            try { return x.IndependentPlaybackEnabled; } catch { return false; }
        }
    }
}
