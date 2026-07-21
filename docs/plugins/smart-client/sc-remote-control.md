---
title: "Remote Control REST API Plugin for Milestone XProtect"
description: "Remote Control plugin for Milestone XProtect Smart Client, control Smart Client remotely via REST API with Swagger UI, draw SVG overlays on live video."
---

<div class="show-title" markdown>

# Remote Control

Control the Milestone XProtect Smart Client remotely via a REST API with interactive Swagger UI documentation. External systems, automation scripts, or control room software can switch views, display cameras, change workspaces, and more - all over HTTP.

## Quick Start

1. Install the plugin and open Smart Client
2. Go to **Settings > Remote Control**
3. The server starts automatically on `127.0.0.1:9500`
4. Click **Open Swagger UI** to explore the API interactively
5. Copy your API token and click **Authorize** in Swagger UI to authenticate

<video controls width="100%">
  <source src="../vids/rdemo.mp4" type="video/mp4">
</video>

## API Endpoints

All endpoints require a `Bearer` token in the `Authorization` header. Use the Swagger UI Authorize button or pass it directly.

```
Authorization: Bearer <your-token>
```

| Method | Path | Description |
|--------|------|-------------|
| `GET` | `/api/views` | List all views |
| `GET` | `/api/cameras` | List all enabled cameras |
| `GET` | `/api/workspaces` | List all workspaces |
| `GET` | `/api/windows` | List Smart Client windows |
| `GET` | `/api/status` | Server status and current SC mode |
| `POST` | `/api/views/switch` | Switch a window to a view |
| `POST` | `/api/cameras/show` | Show N cameras with auto-layout |
| `POST` | `/api/cameras/set` | Set a camera in a specific view slot |
| `POST` | `/api/workspaces/switch` | Switch workspace |
| `POST` | `/api/application/control` | Fullscreen, side panel, window state |
| `POST` | `/api/windows/close` | Close one or all windows |
| `POST` | `/api/clear` | Switch a window to an empty 1x1 view |
| `POST` / `GET` / `DELETE` | `/api/overlays[/{id}]` | Draw SVG overlays on cameras |

Use the `id` field from the discovery endpoints in all action requests.

### Conventions

These apply to every endpoint and are easy to trip over.

**Responses are camelCase, and null fields are omitted.** A field documented below as optional in a response is simply absent when it has no value, rather than present as `null`. JSON is the only supported format; the `Accept` header is ignored.

**`200` means "accepted", not "done".** Every action endpoint hands its work to the Smart Client UI thread and returns immediately without waiting. A success response confirms the command was queued and its arguments validated, not that the client carried it out. A few internal failure paths (a missing grid view, no WPF application context) abandon the work silently after the response has already been sent. If an action must be confirmed, poll a discovery endpoint afterwards.

**IDs are `ObjectId` GUIDs.** Every `id` in and out of the API is the FQID's ObjectId, not a serialized FQID. Cameras are always resolved against the master site, so **cameras on federated or child sites will not resolve**.

**A malformed ID and an unknown ID both return `404`, never `400`.** Every resolver parses the GUID and returns "not found" on failure, so a typo and a deleted camera are indistinguishable in the response.

**Error shapes are not uniform.** Validation failures return `400` with `{"message": "..."}`. Resolution failures return `404` with an **empty body** on most endpoints; `/api/cameras/show` is the exception and returns `{"error": "..."}`. Auth failures return `401` with `{"error": "..."}`.

**Omitted integers become `0`, omitted booleans become `false`.** The request models use non-nullable types, so there is no way to distinguish "field omitted" from "field explicitly `0`". This matters most for `windowIndex`, where omission means the main window.

### Window targeting

`windowIndex` selects which Smart Client window an action applies to. Valid values are `0` to `count - 1`, where the count and ordering come from `GET /api/windows`. Index `0` is the main window.

!!! warning "Out-of-range indices do not fail"
    An index that is negative or past the end **silently falls back to window 0**, and the response echoes back the index you requested rather than the one actually used. `{"windowIndex": 99}` returns `{"success": true, "windowIndex": 99}` while acting on the main window. There is no error to detect this, so validate against `GET /api/windows` before sending.

The index is positional within the live window enumeration, so it is **not stable**: opening or closing a floating window renumbers the others. Re-fetch `/api/windows` rather than caching indices.

Only `views/switch`, `cameras/show`, `cameras/set`, `windows/close`, and `clear` accept `windowIndex`. `workspaces/switch` and `application/control` apply application-wide and take no target.

A `404` on `windows/close` or `clear` means no windows exist at all, which in practice only happens during shutdown.

### Discovery

#### `GET /api/views`

Returns a flat array of every view in the tree. View group folders are not included.

| Field | Type | Description |
|-------|------|-------------|
| `id` | string | View ObjectId GUID, for use as `viewId` |
| `name` | string | View name |
| `path` | string | Breadcrumb of **parent folders only**, joined with ` › `. Excludes the view's own name. |

```json
[
  { "id": "3f2a...", "name": "Lobby Overview", "path": "Views › Private › Floor 1" }
]
```

!!! note "Empty array is ambiguous"
    All five discovery endpoints swallow internal errors and return `[]` rather than a `5xx`. An empty result means either "nothing configured" or "lookup failed"; check MIPLog.txt to tell them apart.

#### `GET /api/cameras`

| Field | Type | Description |
|-------|------|-------------|
| `id` | string | Camera ObjectId GUID, for use as `cameraId` |
| `name` | string | Camera name from the Management Server |
| `path` | string | Camera group breadcrumb, joined with ` › `. **Includes** the group's own name, unlike `/api/views`. |

Built by walking the camera group tree on the master site, which has three consequences:

- **Disabled cameras are omitted.**
- **Cameras that belong to no camera group are omitted.**
- **A camera in two groups appears twice**, once per group path, with the same `id`.

Requires a live Management Server connection. Note that `/api/cameras/set` resolves cameras through a different path and will accept IDs this endpoint does not list.

#### `GET /api/workspaces`

| Field | Type | Description |
|-------|------|-------------|
| `id` | string | Workspace ObjectId GUID, for use as `workspaceId` |
| `name` | string | Workspace name |

No `path` field; workspaces are not nested.

#### `GET /api/windows`

| Field | Type | Description |
|-------|------|-------------|
| `id` | string | Window ObjectId GUID (informational; actions take `index`, not this) |
| `name` | string | Window name |
| `index` | int | Zero-based position, and the value to pass as `windowIndex` |

`index` is assigned during enumeration and is not persisted. See [Window targeting](#window-targeting) for the stability caveat.

#### `GET /api/status`

| Field | Type | Description |
|-------|------|-------------|
| `status` | string | Always `"running"`. If you can reach this endpoint the server is up, so it has no other value. |
| `mode` | string | Current Smart Client mode, for example `ClientLive` or `ClientPlayback`. Falls back to `"Unknown"` if the mode cannot be read. |
| `listenUrl` | string | Configured listen URL. **Absent** if the server has been stopped. |
| `version` | string | Plugin assembly version, falling back to `"1.0.0"`. |

### Actions

#### `POST /api/views/switch`

Switches a window to an existing view.

| Field | Type | Required | Default | Notes |
|-------|------|----------|---------|-------|
| `viewId` | string | yes | - | ObjectId from `GET /api/views` |
| `windowIndex` | int | no | `0` | See [Window targeting](#window-targeting) |

```json
POST /api/views/switch
{ "viewId": "3f2a...", "windowIndex": 0 }
```

**`200`** `{"success": true, "viewId": "...", "windowIndex": 0}`
**`400`** `{"message": "viewId is required"}` when the body or `viewId` is missing or empty.
**`404`** empty body when `viewId` is not a valid GUID, or is a GUID that is not in the view tree.

!!! note "Folder IDs are accepted but do nothing"
    The lookup searches folders as well as views, so passing a view group's ObjectId returns `200`. The client has no view to load and nothing visible happens. Only use IDs from `GET /api/views`, which lists views only.

#### `POST /api/cameras/show`

Picks a grid layout that fits the camera count, switches the window to it, and fills the slots in order. This is the main entry point for ad-hoc camera display.

| Field | Type | Required | Default | Notes |
|-------|------|----------|---------|-------|
| `cameraIds` | string[] | yes | - | Non-empty, **maximum 20 entries**. Order determines slot order. |
| `windowIndex` | int | no | `0` | See [Window targeting](#window-targeting) |

```json
POST /api/cameras/show
{ "cameraIds": ["cam-guid-1", "cam-guid-2", "cam-guid-3", "cam-guid-4"] }
```

**`200`** `{"success": true, "cameraCount": 4, "windowIndex": 0}`. Note the response returns a count, not the ID list.
**`400`** `{"message": "cameraIds array is required and must not be empty"}`
**`400`** `{"message": "Maximum 20 cameras per request"}`
**`404`** `{"error": "Camera not found: <id>"}` naming the **first** unresolvable ID. Validation stops there, so IDs after it are unchecked. Nothing has been dispatched at this point, so a failed request changes nothing.

The layout is the first one in this order whose capacity covers the request:

| Cameras | Layout |
|---------|--------|
| 1 | 1x1 |
| 2 | 1x2 |
| 3 | 1x3 |
| 4 | 2x2 |
| 5-6 | 2x3 |
| 7-8 | 2x4 |
| 9 | 3x3 |
| 10-12 | 3x4 |
| 13-16 | 4x4 |
| 17-20 | 4x5 |

Unused slots stay blank. Since the largest layout is 4x5, 20 is a hard ceiling rather than a tunable limit.

!!! warning "This writes to the user's configuration"
    On first use the plugin creates a view group named **Remote Control** under the user's first view group, containing ten saved views (`1x1` through `4x5`). These are persistent, visible in the Smart Client view tree, and are not removed when the plugin stops. They are created once and reused. `/api/clear` triggers the same setup.

Slot fills are dispatched immediately after the view switch without waiting for it to complete. On a slow client the first request after a view change can occasionally land in the previous view; sending the same request twice is a safe workaround, since it is idempotent.

#### `POST /api/cameras/set`

Places one camera in one slot of whatever view the target window is **already showing**. Unlike `cameras/show`, it does not switch views and does not create anything.

| Field | Type | Required | Default | Notes |
|-------|------|----------|---------|-------|
| `cameraId` | string | yes | - | Camera ObjectId |
| `slotIndex` | int | no | `0` | Zero-based slot in the current view. **Not validated.** |
| `windowIndex` | int | no | `0` | See [Window targeting](#window-targeting) |

```json
POST /api/cameras/set
{ "cameraId": "cam-guid-1", "slotIndex": 2, "windowIndex": 0 }
```

**`200`** `{"success": true, "cameraId": "...", "slotIndex": 2, "windowIndex": 0}`
**`400`** `{"message": "cameraId is required"}`
**`404`** empty body for a malformed or unresolvable `cameraId`.

`slotIndex` is passed through with no bounds checking. A negative or out-of-range slot returns `200` and is discarded by the client. Confirm the current view's slot count via `GET /api/views` and your own layout knowledge before targeting a high index.

This endpoint resolves cameras through the configuration API rather than the camera group tree, so it **accepts cameras that `GET /api/cameras` does not list**, including disabled ones and cameras outside any group.

#### `POST /api/workspaces/switch`

| Field | Type | Required | Default | Notes |
|-------|------|----------|---------|-------|
| `workspaceId` | string | yes | - | ObjectId from `GET /api/workspaces` |

Application-wide; takes no `windowIndex`.

**`200`** `{"success": true, "workspaceId": "..."}`
**`400`** `{"message": "workspaceId is required"}`
**`404`** empty body for a malformed or unknown ID.

#### `POST /api/application/control`

| Field | Type | Required | Default | Notes |
|-------|------|----------|---------|-------|
| `command` | string | yes | - | One of the values below. **Case-insensitive.** |

| Command | Description |
|---------|-------------|
| `ToggleFullscreen` | Toggle fullscreen mode |
| `EnterFullscreen` | Enter fullscreen |
| `ExitFullscreen` | Exit fullscreen |
| `ShowSidePanel` | Show the side panel |
| `HideSidePanel` | Hide the side panel |
| `Maximize` | Maximize the window |
| `Minimize` | Minimize the window |
| `Restore` | Restore the window |

**`200`** `{"success": true, "command": "..."}` echoing your original casing.
**`400`** `{"message": "command is required. Available: ToggleFullscreen, ..."}` - the same message is returned for a missing command and an unrecognized one, so there is no distinct "unknown command" error.

!!! note "These are not the MIP SDK constant names"
    The API uses `ToggleFullscreen` / `EnterFullscreen` / `ExitFullscreen`, whereas the SDK spells the equivalents `ToggleFullScreenMode` / `EnterFullScreenMode` / `ExitFullScreenMode`. Sending the SDK spelling returns `400`.

Applies to the main application window; there is no per-window targeting.

#### `POST /api/windows/close`

| Field | Type | Required | Default | Notes |
|-------|------|----------|---------|-------|
| `all` | bool | no | `false` | When `true`, closes all floating windows and **`windowIndex` is ignored** |
| `windowIndex` | int | no | `0` | See [Window targeting](#window-targeting) |

**`200`** with `all: true` → `{"success": true, "message": "All floating windows closed"}`
**`200`** otherwise → `{"success": true, "windowIndex": 0}`
**`404`** empty body when no windows exist. Cannot occur with `all: true`, which is handled before the lookup.

!!! danger "An empty body closes the main window"
    An omitted `windowIndex` defaults to `0`, so `POST /api/windows/close` with no body at all is equivalent to `{"windowIndex": 0}` and targets the main Smart Client window. Always send an explicit body.

#### `POST /api/clear`

| Field | Type | Required | Default | Notes |
|-------|------|----------|---------|-------|
| `windowIndex` | int | no | `0` | See [Window targeting](#window-targeting) |
| `delaySeconds` | int | no | `0` | **Clamped to 0-300 without error.** Negative becomes `0`, over 300 becomes 300. |

```json
POST /api/clear
{ "windowIndex": 0, "delaySeconds": 10 }
```

**`200`** immediate → `{"success": true, "message": "View cleared", "windowIndex": 0}`
**`200`** delayed → `{"success": true, "message": "View will be cleared in 10 seconds", "windowIndex": 0}`, returned right away.
**`404`** empty body when no windows exist.

!!! warning "Clear switches views, it does not blank the current one"
    Despite the name, this does not empty the view the user is on. It switches the window to the plugin's saved **1x1** view with an empty camera slot, creating the `Remote Control` view group first if needed, exactly as `/api/cameras/show` does. The user's previous view is left untouched but is no longer displayed, and afterwards the window sits on a plugin-owned view rather than a blank screen.

Delayed clears are held in memory only:

- They **cannot be cancelled** once scheduled. Sending a second `/api/clear` schedules a second one rather than replacing the first.
- They are **lost if the Smart Client or plugin restarts** before firing.
- The target window is resolved when the request arrives. If that window is closed during the wait, the delayed clear targets a window that no longer exists and does nothing.



### SVG Overlays

External systems can push SVG graphics that render on top of any camera in the Smart Client. The overlay appears in every viewport currently showing the target camera (main window, floating windows, every slot of a grid), and automatically re-applies when the user switches views or drags the camera into a new slot.

| Method | Path | Description |
|--------|------|-------------|
| `POST` | `/api/overlays` | Create or replace an overlay (upsert by `overlayId`) |
| `GET` | `/api/overlays` | List active overlays |
| `GET` | `/api/overlays/{id}` | Get one overlay including the original SVG |
| `DELETE` | `/api/overlays/{id}` | Remove one overlay |
| `DELETE` | `/api/overlays?cameraId=...` | Clear all overlays for a camera (omit query to clear everything) |

#### Request body

```json
POST /api/overlays
{
  "overlayId":   "alarm-12345",
  "cameraId":    "<camera-guid>",
  "svg":         "<svg viewBox=\"0 0 1000 1000\">...</svg>",
  "ttlSeconds":  60,
  "zOrder":      100
}
```

- `overlayId` is caller-supplied and stable. Posting the same `overlayId` again replaces the overlay in place without flicker, ideal for live meters that update many times per second.
- `cameraId` is the FQID from `GET /api/cameras`.
- `ttlSeconds` is optional. Omit or pass `0` for "persist until DELETE". The store is in-memory; everything clears on Smart Client restart.
- `zOrder` defaults to `100`. Higher numbers draw on top of lower ones.

#### Coordinate space

Author your SVG against a `viewBox`. If the `viewBox` attribute is missing, the plugin assumes `0 0 1000 1000`. Coordinates are scaled to the rendered viewport at draw time, so a shape at `x=500` lands at the horizontal centre regardless of the camera's resolution or aspect ratio.

#### Supported SVG subset

`rect`, `circle`, `ellipse`, `line`, `polyline`, `polygon`, `path` (full `d=` command set), `text`, `g` with `transform="translate|scale|rotate|matrix"`. Style attributes honored: `fill`, `stroke`, `stroke-width`, `opacity`, `fill-opacity`, `stroke-opacity`, `font-family`, `font-size`, `font-weight`, `font-style`. Both presentation attributes and inline `style="..."` are read.

Caps to keep the UI responsive against a misbehaving integrator: max 500 shapes per overlay, max 32 overlays per camera, max 50 KB SVG body.

#### Off-screen targets

If you post an overlay for a camera that is not currently displayed in any viewport, the API still returns `201` with a `warning` field. The overlay is queued; the moment the camera appears in any viewport, it renders automatically. This is by design, you can pre-load overlays before switching views.

#### Example: simple shapes

```json
POST /api/overlays
{
  "overlayId": "demo-box",
  "cameraId":  "<camera-guid>",
  "svg": "<svg viewBox='0 0 1000 1000'>
    <rect x='100' y='100' width='300' height='200' fill='red' fill-opacity='0.4' stroke='red' stroke-width='4'/>
    <text x='110' y='90' fill='red' font-size='40' font-weight='bold'>INTRUDER</text>
  </svg>"
}
```

#### Example: live multi-gauge strip

A common ask is to put a live gauge on top of a camera, e.g. tank level, occupancy, queue length, battery, air quality bracket. The external sensor posts the same `overlayId` on every tick; the plugin updates in place with no flicker.

See [Example: live multi-gauge overlay](#example-live-multi-gauge-overlay) at the bottom of this page for a full runnable script that composes four gauge styles (semi-circle with needle, segmented donut, horizontal band, thermometer) into a single SVG and animates it. A self-contained variant also ships with the plugin sources as `Smart Client Plugins/SCRemoteControl/test-api.py` (use `--demo` to skip the test pass and just animate the strip).

!!! tip "Tips for live overlays"
    - Reuse the same `overlayId` on every tick. A fresh POST replaces the shapes in place, no flicker, no allocation churn.
    - Author everything against the `0..1000` viewBox so the overlay rescales cleanly with the viewport.
    - Compute value-dependent colors in your code before building the SVG, the plugin does not evaluate `<style>` blocks or CSS selectors.
    - When the sensor goes offline, `DELETE /api/overlays/{id}` so a stale value does not linger on screen.
    - For overlays that should auto-clear after a fixed time (alarms, transient annotations), set `ttlSeconds`. The plugin prunes them server-side.

## Settings

Open **Settings > Remote Control** in the Smart Client.

### Server Status

Shows whether the server is running, the listen URL, and any errors. Buttons:

- **Restart Server** - Applies settings and restarts the HTTP server
- **Open Swagger UI** - Opens the interactive API documentation in the browser

### Network Configuration

| Setting | Description | Default |
|---------|-------------|---------|
| Listen Interface | Network interface to bind to | `127.0.0.1` (loopback) |
| Port | HTTP port, must be in the range 1024-65535 (falls back to `9500` if outside it) | `9500` |
| Use HTTPS | Enable TLS encryption | Off |
| PFX Certificate | Certificate file for HTTPS (`.pfx` format) | - |
| PFX Password | Password for the PFX file | - |

!!! note "Listen Interface"
    The default `127.0.0.1` only allows connections from the local machine. Select **All Interfaces (0.0.0.0)** or a specific IP to allow remote access. When using `0.0.0.0`, ensure your firewall is configured appropriately.

!!! warning "HTTPS"
    When HTTPS is disabled, API tokens are sent in plaintext over the network. Use HTTPS when the listen interface is not loopback, or ensure the network is trusted.

### API Tokens

Tokens authenticate API requests. At least one token is always required.

- **Add Token** - Generate a new random 256-bit token
- **Copy** - Copy the token value to clipboard
- **Remove** - Delete a token (cannot remove the last one)

## Security

- **Authentication**: All `/api/*` endpoints require a valid Bearer token, except `OPTIONS` preflight requests. There is no way to disable authentication - an empty or missing token always fails. Tokens are compared in fixed time. A failure returns `401` with `{"error": "Unauthorized. Provide a valid Bearer token in the Authorization header."}`.
- **Token header**: The documented form is `Authorization: Bearer <token>`. The raw form `Authorization: <token>` without a scheme is also accepted.
- **CORS**: Browser requests are allowed only when `Origin` matches the configured listen URL or a `localhost` / `127.0.0.1` variant on the same port. There is no wildcard. The allowed methods are `GET`, `POST`, and `OPTIONS` - **`DELETE` is not included**, so browser-based overlay deletion from an allowed origin is blocked by preflight. Non-browser HTTP clients (scripts, automation) are unaffected and work from any machine.
- **Token storage**: API tokens and PFX passwords are encrypted at rest using Windows DPAPI.
- **Default loopback**: The server defaults to `127.0.0.1`, limiting access to the local machine until explicitly configured otherwise.

### Unauthenticated routes

Everything outside `/api/` is served without a token:

| Path | Description |
|------|-------------|
| `/` | Redirects to `/swagger` |
| `/swagger`, `/swagger/*` | Swagger UI and its bundled JS and CSS |
| `/swagger/docs/v1` | Generated OpenAPI 2.0 specification |

!!! warning "The API specification is publicly readable"
    `/swagger/docs/v1` returns the full endpoint list, request schemas, and parameter names to any caller, with no token. On loopback this is harmless. If you bind to `0.0.0.0` or a routable address, anyone who can reach the port can enumerate the entire API surface even though they cannot call it. Restrict the port at the firewall when exposing the server beyond the local machine.

## Example: Python

```python
import requests

BASE = "http://localhost:9500"
TOKEN = "your-token-here"
HEADERS = {"Authorization": f"Bearer {TOKEN}"}

# List cameras
cameras = requests.get(f"{BASE}/api/cameras", headers=HEADERS).json()
for cam in cameras:
    print(f"{cam['name']} ({cam['id']})")

# Show first 4 cameras in a 2x2 grid
ids = [cam["id"] for cam in cameras[:4]]
requests.post(f"{BASE}/api/cameras/show",
              json={"cameraIds": ids}, headers=HEADERS)

# Clear after 10 seconds
requests.post(f"{BASE}/api/clear",
              json={"delaySeconds": 10}, headers=HEADERS)
```

## Example: Go

```go
package main

import (
	"bytes"
	"encoding/json"
	"fmt"
	"io"
	"net/http"
)

const (
	base  = "http://localhost:9500"
	token = "your-token-here"
)

func api(method, path string, body any) (map[string]any, error) {
	var reqBody io.Reader
	if body != nil {
		b, _ := json.Marshal(body)
		reqBody = bytes.NewReader(b)
	}
	req, _ := http.NewRequest(method, base+path, reqBody)
	req.Header.Set("Authorization", "Bearer "+token)
	req.Header.Set("Content-Type", "application/json")
	resp, err := http.DefaultClient.Do(req)
	if err != nil {
		return nil, err
	}
	defer resp.Body.Close()
	var result map[string]any
	json.NewDecoder(resp.Body).Decode(&result)
	return result, nil
}

func main() {
	// List cameras
	resp, _ := api("GET", "/api/cameras", nil)
	fmt.Println(resp)

	// Show 2 cameras in auto-layout
	api("POST", "/api/cameras/show", map[string]any{
		"cameraIds": []string{"cam-guid-1", "cam-guid-2"},
	})

	// Clear after 10 seconds
	api("POST", "/api/clear", map[string]any{
		"delaySeconds": 10,
	})
}
```

## Example: PowerShell

```powershell
$base = "http://localhost:9500"
$headers = @{ Authorization = "Bearer your-token-here" }

# List views
$views = Invoke-RestMethod -Uri "$base/api/views" -Headers $headers
$views | Format-Table name, path

# Switch to first view
$body = @{ viewId = $views[0].id } | ConvertTo-Json
Invoke-RestMethod -Uri "$base/api/views/switch" -Method POST `
    -Headers $headers -ContentType "application/json" -Body $body
```

## Example: live multi-gauge overlay

Composes four gauge styles into a single SVG and pushes the same `overlayId` every ~300 ms so the strip animates in place. Uses only the supported subset (`rect`, `circle`, `polyline`, `polygon`, `line`, `text`) - no `text-anchor`, no gradients, no CSS - and centers text manually by offsetting `x`. Referenced from the [SVG Overlays](#svg-overlays) section.

```python
import math
import time
import requests

BASE = "http://localhost:9500"
TOKEN = "your-token-here"
HEADERS = {"Authorization": f"Bearer {TOKEN}"}
CAMERA = "<camera-guid>"

GAUGE_BANDS = [(0, 20, "#ef4444"), (20, 40, "#f97316"), (40, 60, "#facc15"),
               (60, 80, "#84cc16"), (80, 100, "#22c55e")]
COOL_BANDS  = [(0, 20, "#1d4ed8"), (20, 40, "#2563eb"), (40, 60, "#0ea5e9"),
               (60, 80, "#06b6d4"), (80, 100, "#14b8a6")]
PINK_BANDS  = [(0, 20, "#e11d48"), (20, 40, "#f97316"), (40, 60, "#facc15"),
               (60, 80, "#a3e635"), (80, 100, "#22c55e")]

def _text_w(s, fs, k=0.50): return len(s) * fs * k
def _band_color(v, bands=GAUGE_BANDS):
    for lo, hi, c in bands:
        if lo <= v <= hi: return c
    return bands[-1][2]
def _polar(cx, cy, r, deg):
    rad = math.radians(deg)
    return cx + r * math.cos(rad), cy + r * math.sin(rad)
def _arc(cx, cy, r, a, b, steps=18):
    return " ".join(f"{x:.1f},{y:.1f}" for x, y in
                    (_polar(cx, cy, r, a + (b - a) * i / steps) for i in range(steps + 1)))

def _semi(parts, cx, cy, r, v, num, ring_w=10, bands=GAUGE_BANDS):
    for lo, hi, c in bands:
        parts.append(f"<polyline points='{_arc(cx, cy, r, 180 - hi*1.8, 180 - lo*1.8)}' "
                     f"fill='none' stroke='{c}' stroke-width='{ring_w}' "
                     f"stroke-linecap='round' stroke-linejoin='round'/>")
    deg = 180 - v * 1.8
    nx, ny = _polar(cx, cy, r - 14, deg)
    parts.append(f"<line x1='{cx}' y1='{cy}' x2='{nx:.1f}' y2='{ny:.1f}' "
                 f"stroke='#d1d5db' stroke-width='3' stroke-linecap='round'/>")
    parts.append(f"<circle cx='{cx}' cy='{cy}' r='6' fill='#111827' "
                 f"stroke='white' stroke-opacity='0.55' stroke-width='1'/>")
    top = cy - 36
    parts.append(f"<rect x='{cx - 15}' y='{top}' width='30' height='18' rx='8' ry='8' "
                 f"fill='#111827' fill-opacity='0.92' stroke='white' "
                 f"stroke-opacity='0.25' stroke-width='1'/>")
    parts.append(f"<text x='{cx - _text_w(num, 12)/2:.1f}' y='{top + 14}' "
                 f"fill='white' font-size='12' font-weight='bold'>{num}</text>")

def _donut(parts, cx, cy, r, v):
    parts.append(f"<rect x='{cx - r - 16}' y='{cy - r - 16}' width='{2*r + 32}' "
                 f"height='{2*r + 32}' rx='18' ry='18' fill='#05070a' fill-opacity='0.36' "
                 f"stroke='white' stroke-opacity='0.08' stroke-width='1'/>")
    for i, (_, _, c) in enumerate(COOL_BANDS):
        parts.append(f"<polyline points='{_arc(cx, cy, r, -90 + i*72, -90 + (i+1)*72)}' "
                     f"fill='none' stroke='{c}' stroke-width='12' stroke-linecap='round'/>")
    deg = -90 + v * 3.6
    sx, sy = _polar(cx, cy, 16, deg)
    nx, ny = _polar(cx, cy, r - 9, deg)
    tx, ty = _polar(cx, cy, r, deg)
    parts.append(f"<line x1='{sx:.1f}' y1='{sy:.1f}' x2='{nx:.1f}' y2='{ny:.1f}' "
                 f"stroke='white' stroke-width='3' stroke-linecap='round'/>")
    parts.append(f"<circle cx='{tx:.1f}' cy='{ty:.1f}' r='4' fill='white' "
                 f"stroke='#111827' stroke-width='1.5'/>")
    parts.append(f"<circle cx='{cx}' cy='{cy}' r='13' fill='#111827' fill-opacity='0.92'/>")
    n = str(int(round(v)))
    parts.append(f"<text x='{cx - _text_w(n, 12)/2:.1f}' y='{cy + 3}' "
                 f"fill='white' font-size='12' font-weight='bold'>{n}</text>")

def _linear(parts, x, y, w, h, v, bands=PINK_BANDS):
    parts.append(f"<rect x='{x}' y='{y}' width='{w}' height='{h}' rx='{h/2}' ry='{h/2}' "
                 f"fill='#05070a' fill-opacity='0.40' stroke='white' "
                 f"stroke-opacity='0.08' stroke-width='1'/>")
    ix, iy, iw, ih = x + 6, y + 6, w - 12, h - 12
    for lo, hi, c in bands:
        parts.append(f"<rect x='{ix + iw*lo/100:.1f}' y='{iy}' "
                     f"width='{iw*(hi-lo)/100:.1f}' height='{ih}' "
                     f"rx='{max(2, ih/3):.1f}' ry='{max(2, ih/3):.1f}' fill='{c}'/>")
    mx = ix + iw * v / 100
    cw, ch, cy_ = 22, 14, y - 22
    parts.append(f"<rect x='{mx - cw/2:.1f}' y='{cy_}' width='{cw}' height='{ch}' "
                 f"rx='5' ry='5' fill='#111827' fill-opacity='0.92' "
                 f"stroke='white' stroke-opacity='0.25' stroke-width='1'/>")
    parts.append(f"<polygon points='{mx:.1f},{y + 1} {mx - 5:.1f},{cy_ + ch} "
                 f"{mx + 5:.1f},{cy_ + ch}' fill='#111827' stroke='white' "
                 f"stroke-opacity='0.5' stroke-width='1'/>")
    p = str(int(round(v)))
    parts.append(f"<text x='{mx - _text_w(p, 8)/2:.1f}' y='{cy_ + 10}' "
                 f"fill='white' font-size='8' font-weight='bold'>{p}</text>")

def _thermo(parts, x, y, v, bands=COOL_BANDS):
    ow, th, br, iw = 14, 150, 13, 10
    cx, cy = x + ow/2, y + th + 2
    col = _band_color(v, bands)
    parts.append(f"<rect x='{x - 22}' y='{y - 12}' width='70' height='196' rx='18' ry='18' "
                 f"fill='#05070a' fill-opacity='0.36' stroke='white' "
                 f"stroke-opacity='0.08' stroke-width='1'/>")
    parts.append(f"<circle cx='{cx}' cy='{cy}' r='{br}' fill='white' fill-opacity='0.12'/>")
    parts.append(f"<rect x='{x}' y='{y}' width='{ow}' height='{th + 6}' rx='7' ry='7' "
                 f"fill='white' fill-opacity='0.12'/>")
    ix, iy, ih = x + (ow - iw)/2, y + 7, th - 10
    parts.append(f"<rect x='{ix}' y='{iy}' width='{iw}' height='{ih}' "
                 f"rx='3' ry='3' fill='#0b0f14'/>")
    parts.append(f"<circle cx='{cx}' cy='{cy}' r='{br - 4}' fill='{col}'/>")
    lh = ih * v / 100
    ly = iy + ih - lh
    parts.append(f"<rect x='{ix}' y='{ly:.1f}' width='{iw}' height='{lh + 6:.1f}' "
                 f"rx='3' ry='3' fill='{col}'/>")
    parts.append(f"<line x1='{x - 10}' y1='{ly:.1f}' x2='{x - 2}' y2='{ly:.1f}' "
                 f"stroke='{col}' stroke-width='2'/>")
    pct = f"{int(round(v))}%"
    cw, ch = 28, 14
    cxp, cyp = x + 20, ly - ch/2
    parts.append(f"<rect x='{cxp}' y='{cyp:.1f}' width='{cw}' height='{ch}' rx='5' ry='5' "
                 f"fill='#111827' fill-opacity='0.92'/>")
    parts.append(f"<text x='{cxp + (cw - _text_w(pct, 8))/2:.1f}' y='{cyp + 10:.1f}' "
                 f"fill='white' font-size='8' font-weight='bold'>{pct}</text>")

def gauge_svg(value: float) -> str:
    v = max(0.0, min(100.0, float(value)))
    # Four correlated channels so the demo looks alive without four sensors.
    a, b, c, d = v, min(100, v*0.88 + 6), max(0, 100 - v*0.45), min(100, 35 + v*0.5)
    parts = ["<rect x='18' y='18' width='964' height='324' rx='24' ry='24' "
             "fill='#05070a' fill-opacity='0.10'/>"]
    _semi(parts,   150, 118, 44, a, str(int(round(a))))
    _donut(parts,  385, 104, 42, b)
    _linear(parts, 520, 184, 210, 26, c)
    _thermo(parts, 835, 92, d)
    return "<svg viewBox='0 0 1000 360'>" + "".join(parts) + "</svg>"

# Push the same overlayId every ~300 ms; the plugin upserts in place.
t0 = time.time()
for _ in range(200):
    v = 50 + 45 * math.sin((time.time() - t0) * 0.6)
    requests.post(f"{BASE}/api/overlays", headers=HEADERS, json={
        "overlayId": "demo-gauge-strip",
        "cameraId":  CAMERA,
        "svg":       gauge_svg(v),
    })
    time.sleep(0.3)
```

</div>
