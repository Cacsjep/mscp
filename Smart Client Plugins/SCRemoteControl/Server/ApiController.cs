using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Http;
using System.Web.Http.Description;
using SCRemoteControl.Api;
using SCRemoteControl.Overlay;
using VideoOS.Platform;
using VideoOS.Platform.Client;
using VideoOS.Platform.Messaging;

namespace SCRemoteControl.Server
{
    /// <summary>Remote Control API</summary>
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

            if (request.CameraIds.Count > 20)
                return BadRequest("Maximum 20 cameras per request");

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

        /// <summary>Send application control command. Available: ToggleFullscreen, EnterFullscreen, ExitFullscreen, ShowSidePanel, HideSidePanel, Maximize, Minimize, Restore</summary>
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

        // ── Overlays ──

        /// <summary>
        /// Upsert an SVG overlay on a camera. POSTing the same overlayId replaces the
        /// existing overlay in place. The overlay renders on every viewport currently
        /// showing the camera, and re-applies when the camera is brought back into view.
        /// </summary>
        [HttpPost, Route("overlays")]
        [ResponseType(typeof(OverlayUpsertResponse))]
        public IHttpActionResult UpsertOverlay(CreateOverlayRequest request)
        {
            if (request == null) return BadRequest("body required");
            if (string.IsNullOrWhiteSpace(request.OverlayId)) return BadRequest("overlayId is required");
            if (string.IsNullOrWhiteSpace(request.CameraId)) return BadRequest("cameraId is required");
            if (string.IsNullOrWhiteSpace(request.Svg)) return BadRequest("svg is required");

            if (!Guid.TryParse(request.CameraId, out var cameraGuid) || cameraGuid == Guid.Empty)
                return BadRequest("cameraId is not a valid GUID");

            // Validate that the camera actually exists in the configuration. This catches
            // typos early; the overlay would otherwise just silently never display.
            var fqid = SmartClientHelper.FindItemFqid(request.CameraId, Kind.Camera);
            if (fqid == null) return Content(System.Net.HttpStatusCode.NotFound, new { error = "camera not found: " + request.CameraId });

            try
            {
                var result = OverlayManager.Instance.Upsert(
                    request.OverlayId,
                    cameraGuid,
                    request.Svg,
                    request.TtlSeconds,
                    request.ZOrder ?? 100);

                var response = new OverlayUpsertResponse
                {
                    OverlayId = request.OverlayId,
                    CameraId = request.CameraId,
                    ShapeCount = result.ShapeCount,
                    ZOrder = request.ZOrder ?? 100,
                    ExpiresAt = result.ExpiresAt,
                    Replaced = result.Replaced,
                    Displayed = result.Displayed,
                };
                if (!result.Displayed)
                    response.Warning = "camera is not currently displayed in any viewport, overlay queued";

                return Content(result.Replaced ? System.Net.HttpStatusCode.OK : System.Net.HttpStatusCode.Created, (object)response);
            }
            catch (SvgParseException ex) { return BadRequest("svg parse failed: " + ex.Message); }
            catch (ArgumentException ex) { return BadRequest(ex.Message); }
            catch (InvalidOperationException ex) { return Content(System.Net.HttpStatusCode.Conflict, new { error = ex.Message }); }
        }

        /// <summary>List active overlays</summary>
        [HttpGet, Route("overlays")]
        [ResponseType(typeof(List<OverlayDto>))]
        public IHttpActionResult ListOverlays()
        {
            var list = OverlayManager.Instance.List();
            var dtos = list.Select(r => new OverlayDto
            {
                OverlayId = r.OverlayId,
                CameraId = r.CameraId.ToString(),
                ZOrder = r.ZOrder,
                ExpiresAt = r.ExpiresAt,
                ShapeCount = r.Parsed?.Shapes.Count ?? 0,
                Displayed = OverlayManager.Instance.AnyAddOnShowsCamera(r.CameraId),
            }).ToList();
            return Ok(dtos);
        }

        /// <summary>Get one overlay including the original SVG body</summary>
        [HttpGet, Route("overlays/{id}")]
        [ResponseType(typeof(OverlayDetailDto))]
        public IHttpActionResult GetOverlay(string id)
        {
            var r = OverlayManager.Instance.Get(id);
            if (r == null) return NotFound();
            return Ok(new OverlayDetailDto
            {
                OverlayId = r.OverlayId,
                CameraId = r.CameraId.ToString(),
                ZOrder = r.ZOrder,
                ExpiresAt = r.ExpiresAt,
                ShapeCount = r.Parsed?.Shapes.Count ?? 0,
                Displayed = OverlayManager.Instance.AnyAddOnShowsCamera(r.CameraId),
                Svg = r.Svg,
            });
        }

        /// <summary>Remove one overlay</summary>
        [HttpDelete, Route("overlays/{id}")]
        public IHttpActionResult DeleteOverlay(string id)
        {
            var removed = OverlayManager.Instance.Remove(id);
            if (!removed) return NotFound();
            return Ok(new { success = true, overlayId = id });
        }

        /// <summary>
        /// Bulk delete. Pass cameraId to clear only overlays for that camera, omit to
        /// clear every overlay.
        /// </summary>
        [HttpDelete, Route("overlays")]
        public IHttpActionResult DeleteOverlays([FromUri] string cameraId = null)
        {
            int count;
            if (!string.IsNullOrWhiteSpace(cameraId))
            {
                if (!Guid.TryParse(cameraId, out var guid)) return BadRequest("cameraId is not a valid GUID");
                count = OverlayManager.Instance.RemoveByCamera(guid);
            }
            else
            {
                count = OverlayManager.Instance.RemoveAll();
            }
            return Ok(new { success = true, removed = count });
        }

        /// <summary>Clear/blank the view (optionally with delay)</summary>
        [HttpPost, Route("clear")]
        public IHttpActionResult ClearView(ClearRequest request)
        {
            var windowIndex = request?.WindowIndex ?? 0;
            var delaySeconds = Math.Max(0, Math.Min(request?.DelaySeconds ?? 0, 300));

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

    /// <summary>Send an application control command to the Smart Client</summary>
    public class AppControlRequest
    {
        /// <summary>
        /// Command to execute. Values:
        /// ToggleFullscreen, EnterFullscreen, ExitFullscreen,
        /// ShowSidePanel, HideSidePanel,
        /// Maximize, Minimize, Restore
        /// </summary>
        /// <example>ToggleFullscreen</example>
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

    // ── Overlay DTOs ──

    /// <summary>Upsert request body. Same overlayId replaces an existing overlay in place.</summary>
    public class CreateOverlayRequest
    {
        /// <summary>
        /// Caller-supplied stable key (e.g. "alarm-12345"). Required.
        /// POSTing the same id with a new body updates the overlay without flicker.
        /// </summary>
        public string OverlayId { get; set; }

        /// <summary>Camera FQID from GET /api/cameras</summary>
        public string CameraId { get; set; }

        /// <summary>
        /// SVG document. Supported elements: rect, circle, ellipse, line, polyline,
        /// polygon, path, text, g. Default viewBox is "0 0 1000 1000" when absent.
        /// </summary>
        public string Svg { get; set; }

        /// <summary>Optional expiry in seconds. Omit or 0 for "persist until DELETE".</summary>
        public int? TtlSeconds { get; set; }

        /// <summary>Z-order, higher draws on top. Default 100.</summary>
        public int? ZOrder { get; set; }
    }

    public class OverlayUpsertResponse
    {
        public string OverlayId { get; set; }
        public string CameraId { get; set; }
        public int ShapeCount { get; set; }
        public int ZOrder { get; set; }
        public DateTime? ExpiresAt { get; set; }
        /// <summary>True if the same overlayId replaced an existing overlay.</summary>
        public bool Replaced { get; set; }
        /// <summary>True if at least one viewport currently shows the target camera.</summary>
        public bool Displayed { get; set; }
        /// <summary>Set when the target camera is not currently displayed anywhere.</summary>
        public string Warning { get; set; }
    }

    public class OverlayDto
    {
        public string OverlayId { get; set; }
        public string CameraId { get; set; }
        public int ZOrder { get; set; }
        public int ShapeCount { get; set; }
        public DateTime? ExpiresAt { get; set; }
        public bool Displayed { get; set; }
    }

    public class OverlayDetailDto : OverlayDto
    {
        /// <summary>Original SVG document as posted.</summary>
        public string Svg { get; set; }
    }
}
