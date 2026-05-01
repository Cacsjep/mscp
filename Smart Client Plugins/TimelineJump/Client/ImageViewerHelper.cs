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
        private static readonly List<ImageViewerAddOn> _imageViewerAddOns = new List<ImageViewerAddOn>();
        private static bool _initialized;

        /// <summary>Raised whenever any tracked tile enters or leaves independent playback.</summary>
        public static event EventHandler IndependentPlaybackStateChanged;

        public static void Init()
        {
            if (_initialized) return;
            ClientControl.Instance.NewImageViewerControlEvent += OnNewImageViewer;
            _initialized = true;
        }

        public static void Close()
        {
            if (!_initialized) return;
            ClientControl.Instance.NewImageViewerControlEvent -= OnNewImageViewer;
            foreach (var addon in _imageViewerAddOns)
            {
                addon.CloseEvent -= OnAddOnClose;
                addon.IndependentPlaybackModeChangedEvent -= OnIndependentPlaybackModeChanged;
            }
            _imageViewerAddOns.Clear();
            _initialized = false;
        }

        private static void OnNewImageViewer(ImageViewerAddOn addon)
        {
            if (addon.ImageViewerType == ImageViewerType.CameraViewItem)
            {
                addon.CloseEvent += OnAddOnClose;
                addon.IndependentPlaybackModeChangedEvent += OnIndependentPlaybackModeChanged;
                _imageViewerAddOns.Add(addon);
                if (addon.IndependentPlaybackEnabled)
                    RaiseStateChanged();
            }
        }

        private static void OnAddOnClose(object sender, EventArgs e)
        {
            var addon = (ImageViewerAddOn)sender;
            addon.CloseEvent -= OnAddOnClose;
            addon.IndependentPlaybackModeChangedEvent -= OnIndependentPlaybackModeChanged;
            var wasIndependent = addon.IndependentPlaybackEnabled;
            _imageViewerAddOns.Remove(addon);
            if (wasIndependent)
                RaiseStateChanged();
        }

        private static void OnIndependentPlaybackModeChanged(object sender, IndependentPlaybackModeEventArgs e)
        {
            RaiseStateChanged();
        }

        private static void RaiseStateChanged()
        {
            try { IndependentPlaybackStateChanged?.Invoke(null, EventArgs.Empty); }
            catch (Exception ex) { TimelineJumpDefinition.Log.Error("IndependentPlaybackStateChanged handler failed", ex); }
        }

        public static ImageViewerAddOn GetGlobalSelectedImageViewer()
        {
            // Snapshot to avoid modification while enumerating during teardown.
            return _imageViewerAddOns.ToList().FirstOrDefault(x => x.IsGlobalSelected);
        }

        /// <summary>True if any tracked tile is currently in independent playback.</summary>
        public static bool AnyIndependentPlayback()
        {
            return _imageViewerAddOns.ToList().Any(x => x.IndependentPlaybackEnabled);
        }
    }
}
