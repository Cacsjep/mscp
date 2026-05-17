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

### Discovery

| Method | Path | Description |
|--------|------|-------------|
| `GET` | `/api/views` | List all views with FQID and path |
| `GET` | `/api/cameras` | List all cameras with FQID and group path |
| `GET` | `/api/workspaces` | List all workspaces |
| `GET` | `/api/windows` | List Smart Client windows |
| `GET` | `/api/status` | Server status and current SC mode |

Use the `id` field from discovery endpoints in all action requests.

### Actions

| Method | Path | Description |
|--------|------|-------------|
| `POST` | `/api/views/switch` | Switch to a view |
| `POST` | `/api/cameras/show` | Show N cameras with auto-layout |
| `POST` | `/api/cameras/set` | Set a camera in a specific view slot |
| `POST` | `/api/workspaces/switch` | Switch workspace |
| `POST` | `/api/application/control` | Application commands |
| `POST` | `/api/windows/close` | Close window(s) |
| `POST` | `/api/clear` | Clear/blank the view |
| `POST` / `GET` / `DELETE` | `/api/overlays[/{id}]` | Draw SVG overlays on cameras |

### Dynamic Camera Layout

Send an array of camera IDs to `/api/cameras/show` and a grid view is automatically created:

```json
POST /api/cameras/show
{
  "cameraIds": ["cam-guid-1", "cam-guid-2", "cam-guid-3", "cam-guid-4"]
}
```

The smallest grid layout that fits is selected automatically (1x1, 1x2, 2x2, 2x3, 3x3, up to 4x5 = 20 cameras max). Grid views are created in a "Remote Control" folder under Private Views.

### Application Commands

Available values for `POST /api/application/control`:

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

### Delayed Clear

Clear a view after a delay (useful for temporarily showing cameras then returning to blank):

```json
POST /api/clear
{
  "windowIndex": 0,
  "delaySeconds": 10
}
```

Maximum delay is 300 seconds (5 minutes).

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
| Port | HTTP port | `9500` |
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

- **Authentication**: All `/api/*` endpoints require a valid Bearer token. Swagger UI is accessible without authentication for convenience.
- **CORS**: Only same-origin requests are allowed from browsers. Non-browser HTTP clients (scripts, automation) work from any machine.
- **Token storage**: API tokens and PFX passwords are encrypted at rest using Windows DPAPI.
- **Default loopback**: The server defaults to `127.0.0.1`, limiting access to the local machine until explicitly configured otherwise.

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
