<div class="show-title" markdown>

# RTSP Driver

A Milestone XProtect device driver that pulls RTSP streams from IP cameras and encoders. Supports H.264 and H.265/HEVC video with audio (AAC, G.711, PCM, G.726), dual streams per channel for adaptive streaming, and rich visual status frames showing connection state, errors, and diagnostics - unlike the built-in Universal Driver which shows no feedback when things go wrong.

## Quick Start

1. Add hardware in Management Client (see [Adding a Device](#adding-a-new-rtsp-device))
2. Set the **RTSP Path (Stream 1)** for each channel (e.g. `/axis-media/media.amp`)
3. Optionally set **RTSP Path (Stream 2)** for a secondary stream (e.g. lower resolution for adaptive streaming)
4. Video appears in Smart Client, audio is picked up automatically if the RTSP source contains an audio track

## Adding a New RTSP Device

After installing, add new hardware in the **Management Client**:

**IP Address:** Enter the IP address or hostname of the RTSP source (e.g. `10.0.0.48`).

**Credentials:** Enter the camera's RTSP username and password. These are used to authenticate with the RTSP source.

!!! info "Port in Add Hardware wizard is NOT the RTSP port"
    The port in the Add Hardware wizard is the HTTP management port (default 80). The actual RTSP port is configured separately per channel in the device settings (default 554).

!!! warning "Unique ports required for multiple instances"
    Each driver instance must use a unique port when adding hardware. If two instances share the same port in the Add Hardware wizard, the second one will fail to add and show as not responding. Use `localhost` or the server IP with a different port for each instance.

### Configuring Channels

After adding the hardware, configure each channel:

1. In Management Client, expand the hardware and select a channel
2. Set the **RTSP Path (Stream 1)** to the camera's primary stream path (e.g. full resolution)
3. Optionally set **RTSP Path (Stream 2)** to a secondary stream path (e.g. lower resolution for adaptive streaming)
4. Optionally adjust the **RTSP Port**, **Transport Protocol**, and **Channel Enabled** settings

The driver will connect immediately when a path is configured. Until then, the channel shows a "Not Configured" status frame with RTSP path examples for common camera brands.

### Audio

Audio is automatically demuxed from the **primary stream** (Stream 1) if the RTSP source contains an audio track. Each channel has a corresponding microphone device (Microphone 1 for Channel 1, etc.) that appears under the hardware in Management Client.

Supported audio codecs: **AAC**, **G.711** (u-law/A-law), **PCM**, **G.726**.

!!! tip "RTSP path must include audio"
    Some camera RTSP paths are video-only (e.g. Axis `?videocodec=h265`). Use a path that includes the audio track (e.g. `/axis-media/media.amp` without video-only parameters) for audio to work.

<video controls width="100%">
  <source src="../rtsp-vids/add.mp4" type="video/mp4">
</video>

## RTSP Path Examples

The RTSP Path is the path portion of the RTSP URL (without `rtsp://ip:port`). The driver constructs the full URL using the hardware IP, credentials, and per-channel port and path settings.

| Brand | Stream 1 (Primary) | Stream 2 (Secondary) |
|---|---|---|
| **Axis** | `/axis-media/media.amp` | `/axis-media/media.amp?resolution=480x270` |
| **Hikvision** | `/Streaming/Channels/101` | `/Streaming/Channels/102` |
| **Dahua** | `/cam/realmonitor?channel=1&subtype=0` | `/cam/realmonitor?channel=1&subtype=1` |
| **ONVIF** | `/onvif-media/media.amp` | *(camera-specific)* |
| **Vivotek** | `/live.sdp` | `/live2.sdp` |
| **Hanwha** | `/profile2/media.smp` | `/profile3/media.smp` |
| **Bosch** | `/video1` | `/video2` |

!!! tip "Leading slash"
    The driver auto-adds a leading `/` if you forget it - both `axis-media/media.amp` and `/axis-media/media.amp` work.

## Configuration

### Hardware Settings

| Setting | Default | Range | Description |
|---|---|---|---|
| **Connection Timeout** | 2 seconds | 1–30 | Timeout for RTSP connection and I/O reads. Also controls how quickly a camera disconnect is detected (TCP and UDP). |
| **Reconnect Interval** | 10 seconds | 1–60 | Wait time between reconnection attempts after a failure. |
| **RTP Buffer Size** | 256 KB | 32–4096 | UDP socket receive buffer size. Increase for high-bitrate streams over UDP to prevent packet loss during CPU spikes. Most users won't need to change this. |

### Device Settings (Per-Channel)

| Setting | Default | Description |
|---|---|---|
| **RTSP Port** | `554` | RTSP port on the camera. Standard is 554. Shared by both streams. |
| **RTSP Path (Stream 1)** | *(empty)* | Primary stream path (e.g. `/axis-media/media.amp`). Channel stays idle until configured. Audio is sourced from this stream. |
| **RTSP Path (Stream 2)** | *(empty)* | Secondary stream path for adaptive streaming (e.g. lower resolution). Leave empty to disable. |
| **Transport Protocol** | `Auto (prefer UDP)` | `Auto` uses UDP (standard for LAN surveillance). `TCP` forces interleaved RTP-over-TCP. `UDP` forces RTP-over-UDP. Applies to both streams. |
| **Channel Enabled** | `true` | Disable to stop pulling from this channel without removing the configuration. |

### Transport Protocol

| Option | Description | When to use |
|---|---|---|
| **Auto (prefer UDP)** | Uses UDP by default, the standard for LAN video surveillance | Default, works with most cameras on local networks |
| **TCP (interleaved)** | Forces RTP interleaved over the RTSP TCP connection | Firewalls blocking UDP, NAT traversal, reliable delivery |
| **UDP** | Forces RTP over separate UDP ports | Low-latency requirements, local networks with no packet loss |

### Events

| Event | Triggered when |
|---|---|
| **RTSP Stream Started** | The driver receives the first keyframe and begins delivering video |
| **RTSP Stream Stopped** | The RTSP connection is lost or the channel is stopped |

Use these to trigger recordings, send notifications, activate outputs, or raise alarms via XProtect Rules.

## Status Frames

When the channel is not streaming live video, the driver shows rich JPEG status frames with connection state and diagnostics. This is the key advantage over the built-in Universal Driver.

| State | Frame Shows |
|---|---|
| **Not Configured** | "Not Configured" with RTSP path examples for common camera brands |
| **Connecting** | "Connecting..." with URL, transport, and attempt number |
| **Awaiting Keyframe** | "Awaiting Keyframe..." - connected, waiting for first IDR frame |
| **Streaming** | Live video from the camera |
| **Reconnecting** | The error message prominently displayed with reconnect countdown |
| **Auth Failed** | Lock icon with "Authentication Failed" and instructions |
| **Connection Error** | The specific error (timeout, refused, DNS, etc.) |
| **Unsupported Codec** | Warning with codec name - only H.264 and H.265 are supported |
| **No Video Track** | Warning that the RTSP source has no video stream |
| **Channel Disabled** | "Channel Disabled" with instructions to enable |

## 4-Channel Architecture

Each driver instance supports **4 independent channels**. Each channel:

- Supports **2 video streams** (primary + secondary) for adaptive streaming
- Has a **microphone device** for audio from the primary stream
- Has its own RTSP connections, port, paths, and transport settings
- Each stream runs on a dedicated background thread
- Maintains its own frame buffer and reconnection logic
- Can connect to a different camera or stream

To monitor more than 4 cameras, add multiple driver instances with different ports in the Add Hardware wizard.

## Troubleshooting

| Problem | Solution |
|---|---|
| No video in Smart Client | Check RTSP Path is correct. Verify credentials. Check driver log. |
| "Not Configured" shown | Set the RTSP Path in Management Client for the channel. |
| "Authentication Failed" | Verify username/password match the camera's RTSP credentials. |
| "Stream not found (404)" | Check RTSP path - try accessing the full URL with VLC first. |
| "Connection timed out" | Verify camera IP is reachable. Check firewall rules. |
| "Connection refused" | Camera is not responding on the configured RTSP port. Check port setting. |
| "Unsupported Codec" | Camera is sending a codec other than H.264 or H.265. Change camera settings. |
| Shows 401 for wrong path | Some cameras (e.g. Axis) run authentication before path lookup, returning 401 for invalid paths instead of 404. This is camera behavior (same with VLC/ffprobe). Verify the path is correct first. |
| Camera unplug not detected | Increase or decrease **Connection Timeout**. Default 2s should detect quickly. |
| Choppy video over UDP | Increase **RTP Buffer Size** or switch to TCP transport. |
| "Hardware not responding" | Verify Recording Server is running. Check that driver DLLs are not blocked. |
| DLLs blocked / driver not loading | Right-click the ZIP before extracting → Properties → Unblock. |
| No audio | Verify the RTSP path includes audio. Some paths (e.g. `?videocodec=h265`) are video-only. Check the driver log for "No audio stream in RTSP source". |
| Microphone shows error | Audio is only sourced from Stream 1. If Stream 1 has no audio track, the microphone will show an error state. |
| Stream 2 not working | Verify the secondary RTSP path is valid. Stream 2 is independent — Stream 1 will continue working even if Stream 2 fails. |

### Testing with VLC

To verify an RTSP URL works before configuring the driver:

```
vlc rtsp://username:password@10.0.0.48:554/axis-media/media.amp
```

### Testing with FFmpeg

```bash
ffplay -rtsp_transport tcp rtsp://username:password@10.0.0.48:554/axis-media/media.amp
```

### Log Location

```
C:\ProgramData\Milestone\XProtect Recording Server\Logs\DriverFramework_RTSPDriver.log
```

## Known Limitations

- **H.264 and H.265 only**: Other video codecs (MJPEG, MPEG-4, etc.) are not supported
- **Audio from primary stream only**: Audio is always sourced from Stream 1. Stream 2 carries video only.

</div>
