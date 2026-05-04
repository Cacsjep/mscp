---
title: "Remote Control REST API Plugin for Milestone XProtect"
description: "Remote Control plugin for Milestone XProtect Smart Client — control Smart Client remotely via REST API with Swagger UI for automation."
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

</div>
