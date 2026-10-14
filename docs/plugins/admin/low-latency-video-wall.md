---
title: "Low Latency Video Wall for Milestone XProtect"
description: "Low Latency Video Wall plugin for Milestone XProtect. Pulls H.264 straight off Axis cameras over RTSP and renders the whole wall on the GPU, driven by rules."
---

<div class="show-title" markdown>

# Low Latency Video Wall

A video wall that takes H.264 straight off Axis cameras over RTSP and draws every pane on the
GPU, bypassing the Image Server protocol. Camera addresses and credentials come from the
Milestone configuration, so nothing is typed twice, and the whole wall can be driven by rules.

One plugin, loaded into three environments from one DLL.

| Part | Environment | What it does |
|---|---|---|
| Camera overview | Administration | Every camera in the configuration with live VAPIX status and bulk configuration |
| Wall views | Administration | Named camera layouts, one configuration item per view |
| Wall assignments | Administration | Which view a user sees by default, and who a rule can target |
| Wall positions | Administration | Generated pane numbers, so a rule can name one |
| Rule actions | Service (Event Server) | Four actions that drive a wall |
| Wall workspace | Smart Client | Renders the wall |

## Quick Start

1. Open the **Management Client**
2. Open **Camera overview** under **Low Latency Video Wall** and check that the cameras you
   want are reachable, then use **Low latency mode on (all)** and **WDR off (all)**
3. Create a **Wall view**, lay out the panes on the grid and assign a camera to each
4. Create a **Wall assignment** for the operator, pointing at that view
5. Open the **Smart Client** as that operator and open the **Video Wall** workspace

## Why it is fast

```
RTSP -> demux -> h264 on d3d11va -> VideoProcessorBlt into a sub-rect -> one Present
            (per camera, own thread)        (all panes, one backbuffer)
```

The decoder and the renderer share one `ID3D11Device`, so a decoded frame is already a texture
the renderer can draw. No readback, no colour conversion on the CPU, no cross device sharing.

**One swapchain for the whole wall, not one per pane.** 48 swapchains would mean 48 `Present`
calls each paying the compositor hop. Instead there is a single child window, one clear, one
blit per pane into its own rectangle, and one `Present`.

**The stream resolution follows the pane size.** A pane 400x225 on the wall asks the camera for
480x270, not 1080p. This is what makes a large wall affordable, and it is why maximising a pane
restarts just that one stream at a higher resolution.

Measured against a real AXIS P3265-V on a 1600x900 wall:

| panes | streaming | total decode | pass cost | present | resolution picked |
|---|---|---|---|---|---|
| 1 | 1/1 | 49.5 fps | 0.02 ms | 49.5 /s | camera default |
| 4 | 4/4 | 197.3 fps | 0.11 ms | 58.0 /s | camera default |
| 16 | 16/16 | 812.5 fps | 0.33 ms | 60.6 /s | 480x270 |
| 24 | 18/24 | 918.7 fps | 0.36 ms | 58.3 /s | 320x180 |

A wall pass costs about **0.02 ms per pane**, so 48 panes is roughly **1 ms** against the
16.7 ms budget at 60 Hz. Packet off the socket to `Present` returning is **0.4 ms**; packet to
scanout is **20.4 ms**, and that 20 ms is Windows desktop composition, which is the floor for
any plugin hosted in a window.

## Camera overview

One row per camera, with what the camera itself reports over VAPIX next to it. The table fills
in two passes: the configuration read is instant and gives every row its name, then the per
camera VAPIX reads run eight at a time and land in their own row as they arrive, so a site with
200 cameras is readable within a second.

| Column | Meaning |
|---|---|
| **Camera** | Name, with a dot for reachability (green reachable, red not, grey not read yet). The address, model and recording server are in the tooltip |
| **Capture mode** | The sensor mode the camera is actually running |
| **WDR** | On, off, or absent in a 50/60 fps capture mode |
| **Low latency** | The single biggest win available, worth about 165 ms |
| **Cfg fps** | The configured frame rate cap |
| **Rotation** | A 90 or 270 degree rotation can halve the delivered frame rate |
| **H.264 profile** | Reported for reference |
| **Warnings** | Why this camera is not configured for low latency |

Two bulk buttons apply to every Axis camera at once: **Low latency mode on (all)** and
**WDR off (all)**. They apply immediately, with no confirmation and no undo, and every change
is written to the log with its old and new value first.

Non Axis hardware is listed and greyed out, because VAPIX cannot drive it.

## Glass to glass

The camera dominates everything. Measured end to end, same wall, same code:

| camera configuration | glass to glass |
|---|---|
| WDR on, low latency off (factory default) | 350 ms |
| WDR on, low latency on | 182 ms |
| WDR off, low latency off | 219 ms |
| WDR off, low latency on | 140 ms |
| 50 fps capture mode, no WDR, low latency on | 134 ms |

Low latency mode alone is worth about 165 ms and WDR off about another 35 ms. This is why the
Camera overview node exists.

!!! note "Capture mode lies"
    `CaptureMode` reads back as the new value while the sensor is still in the old mode. The
    reliable sign that a 50/60 fps mode is live is that the **WDR parameter disappears**, and
    until then the configured frame rate is silently clamped. The bulk configurator knows this
    and waits for the sensor rather than trusting the read back.

## Rule actions

Four actions, which between them cover an operator driven wall, a wall composed entirely by
rules, and a wall where a rule changes a single pane.

| Action | Inputs | Effect |
|---|---|---|
| **Set low latency video wall** | view profile, user | Switch the wall to a stored view |
| **Clear low latency video wall** | user | Show nothing |
| **Show cameras on low latency video wall** | cameras, user | Replace the wall with those cameras in a best fit grid |
| **Show camera at a position on low latency video wall** | camera, position and user | Put one camera in one numbered pane and leave the rest of the wall alone |

**Best fit** means the squarest grid that holds the cameras: 4 cameras are 2x2, 9 are 3x3,
6 are 3x2. A grid a rule composed is never saved, so it does not disturb the stored views.

**Positions** are numbered from one, left to right and then top to bottom. Asking for a
position the wall does not currently have grows the grid to the smallest one that does, so
"camera on position 7" works on a 2x2 wall instead of being silently dropped.

!!! warning "A rule can only target a user who has an assignment"
    Every input in the Milestone rule wizard is an item picker. There is no text box and no
    number field anywhere in it, so a user has to be an item before a rule can name one, and
    so does a pane number. That is why **Wall assignments** doubles as the list of users a rule
    can target, and why **Wall positions** exists at all.

    To make a user targetable without giving them a default view, create an assignment for them
    and leave the view empty.

## Smart Client

The wall fills the **Video Wall** workspace. Double click a pane to make it fill the wall, and
double click again or press Escape to go back. Maximising restarts that one stream at a higher
resolution, so the picture sharpens a moment after it grows.

Everything drawn on a pane is drawn into the swapchain, not in WPF, because the wall window
always paints over anything in front of it.

| Setting | Default | What it does |
|---|---|---|
| **Show the camera title bar** | on | A strip across the top of each pane with the camera name |
| **Scaling** | Keep the aspect ratio | Letterbox, or stretch the picture to the pane edges |
| **Show the view selection** | on | The row above the wall. Off leaves only the wall, for an unattended wall driven by rules |
| **Show the diagnostics overlay** | off | Frame rate, codec, resolution, hardware acceleration and timing per pane |

They live in **Settings** in the Smart Client and are stored per machine in
`%ProgramData%\Milestone\LowLatencyVideoWall\client.xml`.

A pane with no picture never shows a stale one. While it is connecting or reconnecting it shows
a progress ring, and once it has given up it shows a grey panel with the real underlying error,
including the technical message.

## Requirements

- **Axis cameras.** The wall speaks VAPIX and RTSP directly, so other hardware is listed and
  greyed out.
- **The Smart Client must be able to reach the cameras.** On many sites only the recording
  server can. The Camera overview shows reachability per camera.
- **The Smart Client role needs permission to read hardware passwords.** Credentials are read
  live through the configuration API and never stored by this plugin. Without that right every
  pane says so and stays black rather than failing silently.
- A GPU that can decode H.264 with D3D11VA. There is no software fallback on the wall path.

## Limits

- Live only. No playback, no PTZ, no audio.
- Up to 48 panes.
- Time to first picture is the camera's GOP. The stream URL deliberately does not override the
  keyframe interval, so a camera with a long GOP leaves a new pane on the progress ring for as
  long as that GOP lasts.
- Bulk configuration applies immediately, with no confirmation and no undo.

</div>
