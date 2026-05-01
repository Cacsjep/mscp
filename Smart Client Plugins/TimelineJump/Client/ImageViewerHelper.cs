using System;
using System.Collections.Generic;
using System.Linq;
using VideoOS.Platform;
using VideoOS.Platform.Client;

namespace TimelineJump
{
    /// <summary>
    /// Tracks live ImageViewerAddOn instances so we can find the operator's currently
    /// selected camera tile. The MIP SDK exposes per-tile state via ImageViewerAddOn
    /// but does not provide a global "which tile is selected" API; we maintain it
    /// ourselves by listening to NewImageViewerControlEvent. Lifted from the
    /// SCClientAction sample (mipsdk-samples-plugin/SCClientAction/ImageViewerHelper.cs).
    /// </summary>
    internal static class ImageViewerHelper
    {
        private static readonly List<ImageViewerAddOn> _imageViewerAddOns = new List<ImageViewerAddOn>();
        private static bool _initialized;

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
                addon.CloseEvent -= OnAddOnClose;
            _imageViewerAddOns.Clear();
            _initialized = false;
        }

        private static void OnNewImageViewer(ImageViewerAddOn addon)
        {
            if (addon.ImageViewerType == ImageViewerType.CameraViewItem)
            {
                addon.CloseEvent += OnAddOnClose;
                _imageViewerAddOns.Add(addon);
            }
        }

        private static void OnAddOnClose(object sender, EventArgs e)
        {
            var addon = (ImageViewerAddOn)sender;
            addon.CloseEvent -= OnAddOnClose;
            _imageViewerAddOns.Remove(addon);
        }

        public static ImageViewerAddOn GetGlobalSelectedImageViewer()
        {
            // Snapshot to avoid modification while enumerating during teardown.
            return _imageViewerAddOns.ToList().FirstOrDefault(x => x.IsGlobalSelected);
        }
    }
}
