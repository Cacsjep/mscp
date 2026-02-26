# RTMPStreamer

A Milestone XProtect™ MIP plugin that streams live camera video to RTMP/RTMPS destinations. Pure H.264 passthrough from XProtect™ cameras with silent AAC audio track -- no transcoding, no FFmpeg, no native dependencies.

> [!IMPORTANT]
> This is an independent open source project and is **not affiliated with, endorsed by, or supported by Milestone Systems**. XProtect™ is a trademark of Milestone Systems A/S.

## Requirements

- Milestone XProtect™ (Professional+, Expert, Corporate, or Essential+)
- Event Server (for the BackgroundPlugin)
- Management Client (for configuration)
- Cameras configured with **H.264** (H.265 and MJPEG are not supported)

## Installation

### Installer (Recommended)

Download `MSCPlugins-vX.X-Setup.exe` from [Releases](https://github.com/Cacsjep/mscp/releases) and run as **Administrator**. Select **RTMP Streamer Plugin** in the component list.

### Manual (ZIP)

1. Download `RTMPStreamer-vX.X.zip` from [Releases](https://github.com/Cacsjep/mscp/releases)
2. **Unblock the ZIP before extracting** -- right-click the `.zip` -> Properties -> Unblock -> OK
3. Stop the **Milestone XProtect™ Event Server** service
4. Create a `MIPPlugins` folder in `C:\Program Files\Milestone\` (if it doesn't already exist)
5. Extract into `C:\Program Files\Milestone\MIPPlugins\RTMPStreamer\`
6. Start the **Milestone XProtect™ Event Server** service
7. Open the **Management Client** -- the plugin appears under **MIP Plug-ins > RTMP Streamer**

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
|------|-------|---------|
| Green | Streaming | Video is being sent to RTMP server |
| Normal | Starting/Connecting | Helper is initializing or connecting |
| Red | Error | Connection failed, codec error, etc. |
| Grey | Disabled | Stream is disabled via checkbox |

## Live Log

When you select an enabled stream, the detail panel shows a live log at the bottom displaying the most recent output from the streaming process in real time.

- The log shows the last 40 messages and refreshes automatically about twice per second
- Log lines are color-coded: **INFO** (green), **WARN** (yellow), **ERROR** (red), **DEBUG** (gray)

## Architecture

```
 ┌─────────────────────────────────────────────────────────┐
 │  Milestone XProtect™ Event Server (Windows Service)       │
 │                                                         │
 │  BackgroundPlugin                                       │
 │    - Reads config from Management Server                │
 │    - Launches one helper process per stream             │
 │    - Monitors health, auto-restarts on crash            │
 │                                                         │
 │  ┌───────────────────────────────────────────────────┐  │
 │  │  RTMPStreamerHelper.exe  (standalone MIP SDK)     │  │
 │  │                                                   │  │
 │  │  RawLiveSource ──► H.264 Annex B                  │  │
 │  │       │                                           │  │
 │  │       ▼                                           │  │
 │  │  GenericByteData Parser ──► FlvMuxer ──► RTMP(S)  │  │
 │  │       │                        │                  │  │
 │  │       │               Silent AAC audio            │  │
 │  └───────────────────────┬───────────────────────────┘  │
 └──────────────────────────│──────────────────────────────┘
                            │
                            │  RTMP/RTMPS publish
                            ▼
                  ┌─────────────────────┐
                  │  YouTube / Twitch / │
                  │  Facebook / Custom  │
                  └─────────────────────┘
```

### Components

- **Management Client (Admin UI)** -- Configuration interface for creating, editing, and deleting stream items with live status updates
- **Event Server (Background Plugin)** -- Runs as a background service, launches one helper process per enabled stream, monitors health with auto-restart
- **Helper Process (RTMPStreamerHelper.exe)** -- Standalone executable per stream that connects to the Recording Server, muxes H.264 into FLV, and publishes to RTMP

## Logging

### Event Server Logs

```
C:\ProgramData\Milestone\XProtect Event Server\Logs\
```

### Milestone System Log

Significant events are written to the Milestone System Log under the **RTMP Streaming** category:

| Event | Severity | When |
|---|---|---|
| Stream connected | Info | Helper successfully publishing to RTMP server |
| Stream error | Error | Connection failed, TLS error, codec mismatch, etc. |
| Stream stopped | Info | Stream stopped normally |
| Helper crashed | Warning | Helper process died unexpectedly, auto-restarting |
| Plugin started | Info | BackgroundPlugin initialized with N active streams |
| Plugin stopped | Info | BackgroundPlugin shutting down |

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

## Known Limitations

- **H.264 only** -- cameras must use H.264 encoding (not H.265 or MJPEG)
- **Silent audio only** -- the AAC audio track is silent (camera audio is not captured)
