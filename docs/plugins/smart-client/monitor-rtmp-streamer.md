<div class="show-title" markdown>

# Monitor RTMP Streamer

A Smart Client plugin that captures desktop monitors and streams them via RTMP.
It's useful if you want to record what operators do.

<video controls width="100%">
  <source src="../vids/rtmp_mon_usage.mp4" type="video/mp4">
</video>

## Quick Start

1. Open the Monitor RTMP Streamer panel
2. Select which monitors to capture (click to toggle; if none are selected, all are captured)
3. Enter the RTMP destination URL (e.g. `rtmp://server:1935/live/stream`)
4. Choose a frame rate (1–10 FPS)
5. Click **Save & Restart Stream**

Leave the RTMP URL empty to disable streaming entirely — no capture or encoding will run.

## Settings

### Monitors

Click monitors in the visual layout to toggle them on or off. Enabled monitors are highlighted in blue. When no monitors are explicitly selected, all monitors are captured.

Multiple monitors are stitched side-by-side into a single wide image.

### RTMP URL

The destination RTMP URL. Supports any RTMP-compatible server (e.g. Nginx-RTMP, Wowza, YouTube Live, Twitch). The stream uses H.264 video in a FLV container.

### Frame Rate

Controls how many frames per second are captured and streamed (1–10 FPS). Higher values produce smoother video but increase CPU and network usage. Check the **Max FPS** value in the status panel to see what your system can sustain.

## Capture Modes

The capture mode is auto-detected:

| Mode | Method | Typical Speed | When Used |
|---|---|---|---|
| **DXGI (GPU)** | Desktop Duplication API | ~5–20ms per frame | Default on local console sessions |
| **GDI (CPU)** | CopyFromScreen | ~40ms per monitor at 1080p | Fallback (e.g. RDP sessions, lock screen) |

DXGI provides fast GPU-accelerated capture regardless of resolution or monitor count. GDI is used automatically when DXGI is unavailable and scales linearly with resolution. More monitors means a wider stitched image and higher encode time.

If DXGI loses access (e.g. display configuration change, UAC prompt), it automatically reinitialises. If reinitialisation fails, it falls back to GDI.

## Status Panel

The status panel updates every 500ms and shows:

- **Capture** — number of active monitors
- **Resolution** — stitched output resolution (e.g. 7280 x 1440 for three monitors)
- **Capture Mode** — DXGI (GPU) or GDI (CPU)
- **Performance** — capture time + encode time = total cycle time per frame
- **Max FPS** — estimated maximum sustainable FPS based on measured cycle time
- **Stream** — connection status and RTMP URL
- **Uptime** — how long the current stream has been running

## Troubleshooting

| Problem | Fix |
|---|---|
| Plugin not showing | Check DLLs in `MIPPlugins\MonitorRTMPStreamer\`. Unblock ZIP if manual install. |
| Stream not connecting | Verify the RTMP URL is correct and reachable. Check firewall for port 1935. |
| High capture time | DXGI should be ~5–20ms. If showing GDI, you may be on RDP. Reconnect via console. |
| FPS warning in status | Your cycle time exceeds the frame budget. Lower the FPS or reduce monitor count. |
| "Reconnecting in Xs..." | RTMP connection was lost. The plugin retries automatically after 5 seconds. |
| DXGI fell back to GDI | Display config changed or access was lost. Restart the stream to retry DXGI. |
