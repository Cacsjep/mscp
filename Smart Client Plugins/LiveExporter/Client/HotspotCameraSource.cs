using System;
using VideoOS.Platform;
using SCApplication = VideoOS.RemoteClient.Application.Application;
using SCHotspotController = VideoOS.RemoteClient.Application.Controllers.HotspotController.IGlobalHotspotController;
using SCCamera = VideoOS.Shared.ServerAPI.Camera;

namespace LiveExporter.Client
{
    /// <summary>
    /// Tracks the camera last clicked by the operator. Covers tile clicks, legacy Map clicks,
    /// Smart Map clicks, camera-tree picks, and alarm-list selections via the Smart Client's
    /// global hotspot controller (internal API) - the same hook the built-in Hotspot uses.
    /// Legacy Map does NOT emit a public MIP message on camera click; this controller is the
    /// only reliable single source of truth.
    /// </summary>
    internal static class HotspotCameraSource
    {
        private static readonly object _gate = new object();
        private static SCHotspotController _controller;
        private static EventHandler _handler;
        private static bool _initialized;

        public static event EventHandler CameraChanged;

        public static void Init()
        {
            lock (_gate)
            {
                if (_initialized) return;
                try
                {
                    _controller = SCApplication.ControllerManager?.ApplicationController?.GlobalHotspotController;
                    if (_controller == null)
                    {
                        LiveExporterDefinition.Log.Error("GlobalHotspotController unavailable - flyout cannot observe selection clicks", null);
                        return;
                    }
                    _handler = OnHotspotCameraChanged;
                    _controller.HotspotCameraChangedEvent += _handler;
                    _initialized = true;
                }
                catch (Exception ex)
                {
                    LiveExporterDefinition.Log.Error("HotspotCameraSource Init failed", ex);
                }
            }
        }

        public static void Close()
        {
            lock (_gate)
            {
                if (!_initialized) return;
                try { if (_controller != null && _handler != null) _controller.HotspotCameraChangedEvent -= _handler; }
                catch { }
                _controller = null;
                _handler = null;
                _initialized = false;
            }
        }

        /// <summary>Camera FQID of the most recently clicked camera, or null when none yet.</summary>
        public static FQID GetCurrentCameraFqid()
        {
            try
            {
                SCCamera cam = null;
                try { cam = _controller?.HotspotCamera; }
                catch { }
                if (cam == null || cam.DeviceId == Guid.Empty) return null;
                var item = Configuration.Instance.GetItem(cam.DeviceId, Kind.Camera);
                return item?.FQID;
            }
            catch (Exception ex)
            {
                LiveExporterDefinition.Log.Error("GetCurrentCameraFqid failed", ex);
                return null;
            }
        }

        private static void OnHotspotCameraChanged(object sender, EventArgs e)
        {
            try { CameraChanged?.Invoke(null, EventArgs.Empty); }
            catch (Exception ex) { LiveExporterDefinition.Log.Error("CameraChanged handler failed", ex); }
        }
    }
}
