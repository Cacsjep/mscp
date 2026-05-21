using System;
using System.Linq;
using System.Reflection;
using VideoOS.Platform;

namespace LiveExporter.Client
{
    /// <summary>
    /// Tracks the camera last clicked by the operator via the Smart Client's internal
    /// GlobalHotspotController - the same hook the built-in Hotspot uses. Covers tile
    /// clicks, legacy Map clicks, Smart Map clicks, camera-tree picks, and alarm-list
    /// selections. Legacy Map does NOT emit a public MIP message on camera click; this
    /// controller is the only reliable single source of truth.
    ///
    /// Reached via reflection so the project never compile-links to internal Smart Client
    /// assemblies (VideoOS.RemoteClient.Application, VideoOS.Shared). Those DLLs are not
    /// on NuGet and not present on CI; reflection keeps the build on the public NuGet
    /// platform API while still reaching the internal hook at runtime inside Smart Client.
    /// </summary>
    internal static class HotspotCameraSource
    {
        private static readonly object _gate = new object();
        private static object _controller;
        private static EventInfo _changedEvent;
        private static PropertyInfo _hotspotCameraProp;
        private static PropertyInfo _cameraDeviceIdProp;
        private static Delegate _handler;
        private static bool _initialized;

        public static event EventHandler CameraChanged;

        public static void Init()
        {
            lock (_gate)
            {
                if (_initialized) return;
                try
                {
                    var appAsm = FindLoadedAssembly("VideoOS.RemoteClient.Application");
                    var appType = appAsm?.GetType("VideoOS.RemoteClient.Application.Application");
                    var ctrlMgr = appType?.GetProperty("ControllerManager", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                    var appCtrl = ctrlMgr?.GetType().GetProperty("ApplicationController")?.GetValue(ctrlMgr);
                    _controller = appCtrl?.GetType().GetProperty("GlobalHotspotController")?.GetValue(appCtrl);
                    if (_controller == null)
                    {
                        LiveExporterDefinition.Log.Error("GlobalHotspotController unavailable - flyout cannot observe selection clicks", null);
                        return;
                    }

                    var ctrlType = _controller.GetType();
                    _changedEvent = ctrlType.GetEvent("HotspotCameraChangedEvent",
                                        BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
                    _hotspotCameraProp = ctrlType.GetProperty("HotspotCamera");
                    if (_changedEvent == null || _hotspotCameraProp == null)
                    {
                        LiveExporterDefinition.Log.Error("GlobalHotspotController surface changed - missing HotspotCameraChangedEvent or HotspotCamera", null);
                        _controller = null;
                        return;
                    }

                    var method = typeof(HotspotCameraSource).GetMethod(nameof(OnHotspotCameraChanged),
                                    BindingFlags.NonPublic | BindingFlags.Static);
                    _handler = Delegate.CreateDelegate(_changedEvent.EventHandlerType, method);
                    _changedEvent.AddEventHandler(_controller, _handler);
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
                try
                {
                    if (_controller != null && _changedEvent != null && _handler != null)
                        _changedEvent.RemoveEventHandler(_controller, _handler);
                }
                catch { }
                _controller = null;
                _changedEvent = null;
                _hotspotCameraProp = null;
                _cameraDeviceIdProp = null;
                _handler = null;
                _initialized = false;
            }
        }

        /// <summary>Camera FQID of the most recently clicked camera, or null when none yet.</summary>
        public static FQID GetCurrentCameraFqid()
        {
            try
            {
                if (_controller == null || _hotspotCameraProp == null) return null;
                object cam = null;
                try { cam = _hotspotCameraProp.GetValue(_controller); }
                catch { }
                if (cam == null) return null;
                if (_cameraDeviceIdProp == null)
                    _cameraDeviceIdProp = cam.GetType().GetProperty("DeviceId");
                if (!(_cameraDeviceIdProp?.GetValue(cam) is Guid deviceId) || deviceId == Guid.Empty)
                    return null;
                var item = Configuration.Instance.GetItem(deviceId, Kind.Camera);
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

        private static Assembly FindLoadedAssembly(string simpleName)
        {
            return AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => string.Equals(a.GetName().Name, simpleName, StringComparison.OrdinalIgnoreCase));
        }
    }
}
