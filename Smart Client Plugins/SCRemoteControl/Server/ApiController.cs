using System;
using System.Collections.Generic;
using System.Web.Http;
using System.Web.Http.Description;
using SCRemoteControl.Api;
using VideoOS.Platform;
using VideoOS.Platform.Client;
using VideoOS.Platform.Messaging;

namespace SCRemoteControl.Server
{
    /// <summary>SC Remote Control API</summary>
    [RoutePrefix("api")]
    public class RemoteApiController : ApiController
    {
        // ── Discovery ──

        /// <summary>List all views with FQID</summary>
        [HttpGet, Route("views")]
        [ResponseType(typeof(List<ItemDto>))]
        public IHttpActionResult GetViews() => Ok(SmartClientHelper.GetViews());

        /// <summary>List all cameras with FQID</summary>
        [HttpGet, Route("cameras")]
        [ResponseType(typeof(List<ItemDto>))]
        public IHttpActionResult GetCameras() => Ok(SmartClientHelper.GetCameras());

        /// <summary>List all workspaces with FQID</summary>
        [HttpGet, Route("workspaces")]
        [ResponseType(typeof(List<WorkspaceDto>))]
        public IHttpActionResult GetWorkspaces() => Ok(SmartClientHelper.GetWorkspaces());

        /// <summary>List Smart Client windows</summary>
        [HttpGet, Route("windows")]
        [ResponseType(typeof(List<WindowDto>))]
        public IHttpActionResult GetWindows() => Ok(SmartClientHelper.GetWindows());

        /// <summary>Get server status and current SC mode</summary>
        [HttpGet, Route("status")]
        [ResponseType(typeof(StatusDto))]
        public IHttpActionResult GetStatus()
        {
            string mode;
            try { mode = EnvironmentManager.Instance.Mode.ToString(); }
            catch { mode = "Unknown"; }

            return Ok(new StatusDto
            {
                Status = "running",
                Mode = mode,
                ListenUrl = RemoteControlServer.Instance.ListenUrl,
                Version = typeof(RemoteApiController).Assembly.GetName().Version?.ToString() ?? "1.0.0"
            });
        }

        // ── Actions ──

        /// <summary>Switch to a view</summary>
        [HttpPost, Route("views/switch")]
        public IHttpActionResult SwitchView(SwitchViewRequest request)
        {
            if (request == null || string.IsNullOrEmpty(request.ViewId))
                return BadRequest("viewId is required");

            var viewFqid = SmartClientHelper.FindViewFqid(request.ViewId);
            if (viewFqid == null) return NotFound();

            var windowFqid = SmartClientHelper.GetWindowFqid(request.WindowIndex);
            SmartClientHelper.SwitchView(viewFqid, windowFqid);
            return Ok(new { success = true, request.ViewId, request.WindowIndex });
        }

        /// <summary>Show cameras with auto-layout (send N camera IDs, grid is created automatically)</summary>
        [HttpPost, Route("cameras/show")]
        public IHttpActionResult ShowCameras(ShowCamerasRequest request)
        {
            if (request?.CameraIds == null || request.CameraIds.Count == 0)
                return BadRequest("cameraIds array is required and must not be empty");

            var cameraFqids = new List<FQID>();
            foreach (var id in request.CameraIds)
            {
                var fqid = SmartClientHelper.FindItemFqid(id, Kind.Camera);
                if (fqid == null) return Content(System.Net.HttpStatusCode.NotFound, new { error = $"Camera not found: {id}" });
                cameraFqids.Add(fqid);
            }

            var windowFqid = SmartClientHelper.GetWindowFqid(request.WindowIndex);
            SmartClientHelper.ShowCameras(cameraFqids, windowFqid);
            return Ok(new { success = true, cameraCount = cameraFqids.Count, request.WindowIndex });
        }

        /// <summary>Set a single camera in a specific view slot</summary>
        [HttpPost, Route("cameras/set")]
        public IHttpActionResult SetCamera(SetCameraRequest request)
        {
            if (request == null || string.IsNullOrEmpty(request.CameraId))
                return BadRequest("cameraId is required");

            var cameraFqid = SmartClientHelper.FindItemFqid(request.CameraId, Kind.Camera);
            if (cameraFqid == null) return NotFound();

            var windowFqid = SmartClientHelper.GetWindowFqid(request.WindowIndex);
            SmartClientHelper.SetCameraInSlot(request.SlotIndex, cameraFqid, windowFqid);
            return Ok(new { success = true, request.CameraId, request.SlotIndex, request.WindowIndex });
        }

        /// <summary>Switch workspace</summary>
        [HttpPost, Route("workspaces/switch")]
        public IHttpActionResult SwitchWorkspace(SwitchWorkspaceRequest request)
        {
            if (request == null || string.IsNullOrEmpty(request.WorkspaceId))
                return BadRequest("workspaceId is required");

            var fqid = SmartClientHelper.FindWorkspaceFqid(request.WorkspaceId);
            if (fqid == null) return NotFound();

            SmartClientHelper.SwitchWorkspace(fqid);
            return Ok(new { success = true, request.WorkspaceId });
        }

        /// <summary>Change Smart Client mode (Normal or Setup)</summary>
        [HttpPost, Route("mode/change")]
        public IHttpActionResult ChangeMode(ChangeModeRequest request)
        {
            if (request == null || string.IsNullOrEmpty(request.Mode))
                return BadRequest("mode is required (Normal or Setup)");

            if (!Enum.TryParse<WorkSpaceState>(request.Mode, true, out var state))
                return BadRequest("Invalid mode. Use 'Normal' or 'Setup'");

            SmartClientHelper.ChangeMode(state);
            return Ok(new { success = true, mode = state.ToString() });
        }

        /// <summary>Send application control command</summary>
        [HttpPost, Route("application/control")]
        public IHttpActionResult ApplicationControl(AppControlRequest request)
        {
            var commandMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["ToggleFullscreen"] = ApplicationControlCommandData.ToggleFullScreenMode,
                ["EnterFullscreen"] = ApplicationControlCommandData.EnterFullScreenMode,
                ["ExitFullscreen"] = ApplicationControlCommandData.ExitFullScreenMode,
                ["ShowSidePanel"] = ApplicationControlCommandData.ShowSidePanel,
                ["HideSidePanel"] = ApplicationControlCommandData.HideSidePanel,
                ["Maximize"] = ApplicationControlCommandData.Maximize,
                ["Minimize"] = ApplicationControlCommandData.Minimize,
                ["Restore"] = ApplicationControlCommandData.Restore,
            };

            if (request == null || string.IsNullOrEmpty(request.Command) || !commandMap.ContainsKey(request.Command))
                return BadRequest($"command is required. Available: {string.Join(", ", commandMap.Keys)}");

            SmartClientHelper.SendApplicationControl(commandMap[request.Command]);
            return Ok(new { success = true, request.Command });
        }

        /// <summary>Close window(s)</summary>
        [HttpPost, Route("windows/close")]
        public IHttpActionResult CloseWindow(CloseWindowRequest request)
        {
            if (request?.All == true)
            {
                SmartClientHelper.CloseAllWindows();
                return Ok(new { success = true, message = "All floating windows closed" });
            }

            var windowFqid = SmartClientHelper.GetWindowFqid(request?.WindowIndex ?? 0);
            if (windowFqid == null) return NotFound();

            SmartClientHelper.CloseWindow(windowFqid);
            return Ok(new { success = true, windowIndex = request?.WindowIndex ?? 0 });
        }

        /// <summary>Clear/blank the view (optionally with delay)</summary>
        [HttpPost, Route("clear")]
        public IHttpActionResult ClearView(ClearRequest request)
        {
            var windowIndex = request?.WindowIndex ?? 0;
            var delaySeconds = request?.DelaySeconds ?? 0;

            var windowFqid = SmartClientHelper.GetWindowFqid(windowIndex);
            if (windowFqid == null) return NotFound();

            if (delaySeconds > 0)
            {
                var capturedFqid = windowFqid;
                _ = System.Threading.Tasks.Task.Delay(delaySeconds * 1000).ContinueWith(t =>
                    SmartClientHelper.ClearView(capturedFqid));
                return Ok(new { success = true, message = $"View will be cleared in {delaySeconds} seconds", windowIndex });
            }

            SmartClientHelper.ClearView(windowFqid);
            return Ok(new { success = true, message = "View cleared", windowIndex });
        }
    }

    // ── Request/Response DTOs (Swashbuckle generates OpenAPI schema from these) ──

    public class SwitchViewRequest
    {
        /// <summary>View FQID from GET /api/views</summary>
        public string ViewId { get; set; }
        /// <summary>Target window (0 = main)</summary>
        public int WindowIndex { get; set; }
    }

    public class ShowCamerasRequest
    {
        /// <summary>Array of camera FQIDs from GET /api/cameras</summary>
        public List<string> CameraIds { get; set; }
        /// <summary>Target window (0 = main)</summary>
        public int WindowIndex { get; set; }
    }

    public class SetCameraRequest
    {
        /// <summary>Camera FQID</summary>
        public string CameraId { get; set; }
        /// <summary>0-based slot index in current view</summary>
        public int SlotIndex { get; set; }
        /// <summary>Target window (0 = main)</summary>
        public int WindowIndex { get; set; }
    }

    public class SwitchWorkspaceRequest
    {
        /// <summary>Workspace FQID from GET /api/workspaces</summary>
        public string WorkspaceId { get; set; }
    }

    public class ChangeModeRequest
    {
        /// <summary>Normal or Setup</summary>
        public string Mode { get; set; }
    }

    public class AppControlRequest
    {
        /// <summary>ToggleFullscreen, EnterFullscreen, ExitFullscreen, ShowSidePanel, HideSidePanel, Maximize, Minimize, Restore</summary>
        public string Command { get; set; }
    }

    public class CloseWindowRequest
    {
        /// <summary>Target window index</summary>
        public int WindowIndex { get; set; }
        /// <summary>Close all floating windows</summary>
        public bool All { get; set; }
    }

    public class ClearRequest
    {
        /// <summary>Target window (0 = main)</summary>
        public int WindowIndex { get; set; }
        /// <summary>Delay before clearing (0 = immediate)</summary>
        public int DelaySeconds { get; set; }
    }

    // ── Response DTOs ──

    public class ItemDto
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Path { get; set; }
    }

    public class WorkspaceDto
    {
        public string Id { get; set; }
        public string Name { get; set; }
    }

    public class WindowDto
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public int Index { get; set; }
    }

    public class StatusDto
    {
        public string Status { get; set; }
        public string Mode { get; set; }
        public string ListenUrl { get; set; }
        public string Version { get; set; }
    }
}
