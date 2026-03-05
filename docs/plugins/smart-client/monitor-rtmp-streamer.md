<div class="show-title" markdown>

# Monitor RTMP Streamer

A Smart Client plugin that captures desktop monitors and streams them via RTMP.
Its usefull if you want to record what operators do.

<video controls width="100%">
  <source src="../vids/rtmp_mon_usage.mp4" type="video/mp4">
</video>

## Quick Start

1. Open the Monitor RTMP Streamer panel
4. Select monitors and configure the RTMP destination
5. Start streaming

## Troubleshooting

| Problem | Fix |
|---|---|
| Plugin not showing | Check DLLs in `MIPPlugins\MonitorRTMPStreamer\`. Unblock ZIP if manual install. |
| Stream not connecting | Verify the RTMP destination URL is correct and reachable. Check firewall for port 1935. |
| Poor quality | Adjust encoding settings. Ensure adequate CPU/GPU resources for capture + encode. |
