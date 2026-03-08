<div class="show-title" markdown>

# RTSP Driver

A Milestone XProtect device driver that pulls RTSP streams from IP cameras and encoders. Supports H.264 and H.265/HEVC with rich visual status frames showing connection state, errors, and diagnostics - unlike the built-in Universal Driver which shows no feedback when things go wrong.

## Quick Start

1. Add hardware in Management Client (see [Adding a Device](#adding-a-new-rtsp-device))
2. Set the **RTSP Path** for each channel (e.g. `/axis-media/media.amp`)
3. Video appears in Smart Client

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
2. Set the **RTSP Path** to the camera's stream path
3. Optionally adjust the **RTSP Port**, **Transport Protocol**, and **Channel Enabled** settings

The driver will connect immediately when a path is configured. Until then, the channel shows a "Not Configured" status frame with RTSP path examples for common camera brands.

<video controls width="100%">
  <source src="../rtsp-vids/add.mp4" type="video/mp4">
</video>

## RTSP Path Examples

The RTSP Path is the path portion of the RTSP URL (without `rtsp://ip:port`). The driver constructs the full URL using the hardware IP, credentials, and per-channel port and path settings.

| Brand | Full RTSP URL | Path Setting |
|---|---|---|
| **Axis** | `rtsp://ip/axis-media/media.amp` | `/axis-media/media.amp` |
| **Hikvision** | `rtsp://ip/Streaming/Channels/101` | `/Streaming/Channels/101` |
| **Dahua** | `rtsp://ip/cam/realmonitor?channel=1&subtype=0` | `/cam/realmonitor?channel=1&subtype=0` |
| **ONVIF** | `rtsp://ip/onvif-media/media.amp` | `/onvif-media/media.amp` |
| **Vivotek** | `rtsp://ip/live.sdp` | `/live.sdp` |
| **Hanwha** | `rtsp://ip/profile2/media.smp` | `/profile2/media.smp` |
| **Bosch** | `rtsp://ip/video1` | `/video1` |

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
| **RTSP Port** | `554` | RTSP port on the camera. Standard is 554. |
| **RTSP Path** | *(empty)* | Stream path on the camera (e.g. `/axis-media/media.amp`). Channel stays idle until configured. |
| **Transport Protocol** | `Auto (prefer UDP)` | `Auto` uses UDP (standard for LAN surveillance). `TCP` forces interleaved RTP-over-TCP. `UDP` forces RTP-over-UDP. |
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

- Has its own RTSP connection, port, path, and transport settings
- Runs on a dedicated background thread
- Maintains its own frame buffer and reconnection logic
- Can connect to a different camera or stream

To monitor more than 4 streams, add multiple driver instances with different ports in the Add Hardware wizard.

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

- **H.264 and H.265 only**: Other codecs (MJPEG, MPEG-4, etc.) are not supported
- **Video only**: Audio streams are ignored

</div>
