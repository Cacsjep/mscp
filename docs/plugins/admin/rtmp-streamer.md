<div class="show-title" markdown>

# RTMP Streamer

A Milestone XProtect admin plugin that streams live camera video to RTMP/RTMPS destinations. Pure H.264 passthrough from XProtect cameras with silent AAC audio track. No transcoding, no FFmpeg, no native dependencies.

## Quick Start

1. Open the **Management Client**, the plugin appears under **MIP Plug-ins > RTMP Streamer**
3. Right-click **RTMP Streams** and select **Create New**
4. Pick a camera and enter the RTMP destination URL
5. Click **Save**, streaming starts automatically

<video controls width="100%">
  <source src="../vids/rtmp_usage.mp4" type="video/mp4">
</video>

## Configuration

All configuration is done in the **Management Client** under **MIP Plug-ins > RTMP Streamer > RTMP Streams**.

1. Right-click **RTMP Streams** and select **Create New**
2. Enter a name for the stream
3. Click **Select camera...** to pick a camera
4. Enter the RTMP destination URL, for example:
    - YouTube: `rtmp://a.rtmp.youtube.com/live2/xxxx-xxxx-xxxx-xxxx`
    - Twitch: `rtmps://live.twitch.tv/app/live_xxxxxxxxx`
    - Facebook: `rtmps://live-api-s.facebook.com:443/rtmp/FBxxxxxxxxx`
    - Custom: `rtmp://your-server:1935/live/stream-key`
5. Check **Allow untrusted certificates** if using a self-signed RTMPS server
6. Click **Save**

### Status Icons

| Icon | State | Meaning |
|---|---|---|
| Green | Streaming | Video is being sent to RTMP server |
| Normal | Starting/Connecting | Helper is initializing or connecting |
| Red | Error | Connection failed, codec error, etc. |
| Grey | Disabled | Stream is disabled via checkbox |

## Live Log

When you select an enabled stream, the detail panel shows a live log at the bottom displaying the most recent output from the streaming process in real time.

- The log shows the last 40 messages and refreshes automatically about twice per second
- Log lines are color-coded: **INFO** (green), **WARN** (yellow), **ERROR** (red), **DEBUG** (gray)

## Requirements

- Cameras must be configured with **H.264** encoding (H.265 and MJPEG are not supported)
- Event Server must be running (the BackgroundPlugin manages streaming)
- Management Client for configuration

## Troubleshooting

| Problem | Solution |
|---|---|
| No video on RTMP server | Check Event Server logs. Ensure camera uses H.264 encoding. |
| Helper keeps restarting | Check System Log for crash entries. Common: wrong server URI, camera not found. |
| "Helper exe not found" | Ensure `RTMPStreamerHelper.exe` is in same directory as `RTMPStreamer.dll`. |
| YouTube/Twitch rejects stream | Ensure correct URL format. Twitch/Facebook require `rtmps://`. |
| "Camera is using H.265 codec" | Change camera to H.264 in Recording Server configuration. |
| RTMP connection refused | Verify RTMP server is running. Check firewall for port 1935/443. |
| Certificate error with RTMPS | Check **Allow untrusted certificates** in stream config. |
| DLLs blocked / plugin not loading | Unblock the ZIP before extracting. |

### Log Locations

**Event Server logs:**
```
C:\ProgramData\Milestone\XProtect Event Server\Logs\
```

**Milestone System Log**, significant events under the **RTMP Streaming** category:

| Event | Severity | When |
|---|---|---|
| Stream connected | Info | Helper successfully publishing to RTMP server |
| Stream error | Error | Connection failed, TLS error, codec mismatch, etc. |
| Stream stopped | Info | Stream stopped normally |
| Helper crashed | Warning | Helper process died unexpectedly, auto-restarting |
| Plugin started | Info | BackgroundPlugin initialized with N active streams |
| Plugin stopped | Info | BackgroundPlugin shutting down |

## Known Limitations

- **H.264 only**: cameras must use H.264 encoding (not H.265 or MJPEG)
- **Silent audio only**: the AAC audio track is silent (camera audio is not captured)

