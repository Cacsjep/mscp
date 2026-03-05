<div class="show-title" markdown>

# Monitor RTMP Streamer

A Smart Client plugin that captures desktop monitors and streams them via RTMP.

## Quick Start

1. Open the Monitor RTMP Streamer panel
4. Select monitors and configure the RTMP destination
5. Start streaming

## Features

- Multi-monitor capture with per-monitor toggle
- H.264 encoding via FFmpeg (bundled, no external install needed)
- RTMP streaming with auto-reconnect
- Live status dashboard (capture timing, stream status, uptime)
- Visual monitor layout configuration

## Troubleshooting

| Problem | Fix |
|---|---|
| Plugin not showing | Check DLLs in `MIPPlugins\MonitorRTMPStreamer\`. Unblock ZIP if manual install. |
| Stream not connecting | Verify the RTMP destination URL is correct and reachable. Check firewall for port 1935. |
| Poor quality | Adjust encoding settings. Ensure adequate CPU/GPU resources for capture + encode. |
