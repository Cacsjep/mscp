# Low Latency Video Wall

Pulls H.264 straight off Axis cameras over RTSP and renders it on the GPU, bypassing the
Image Server protocol. Camera addresses and credentials come from the Milestone
configuration, so nothing is typed twice. The wall can be driven by a rule.

Every number below was measured against a real AXIS P3265-V, first with a single camera
prototype of this same video path and then with the wall renderer itself. See `report.md`
for the full measurement report.

## What it is

| Part | Environment | What it does |
|---|---|---|
| Camera overview | Administration | Every camera from the configuration with live VAPIX status and bulk configuration |
| Wall views | Administration | Named camera layouts, one configuration item per view |
| Wall assignment | Administration | Which view a user sees by default, and who a rule can target |
| Wall positions | Administration | Generated pane numbers, so a rule can name one |
| Rule actions | Service (Event Server) | Four actions that drive a wall |
| Wall workspace | SmartClient | Renders the wall |

One plugin, loaded into all three environments from one DLL.

## How the video path works

```
RTSP -> demux -> h264 on d3d11va -> VideoProcessorBlt into a sub-rect -> one Present
            (per camera, own thread)        (all panes, one backbuffer)
```

Decoder and renderer share one `ID3D11Device`, so a decoded frame is already a texture the
renderer can draw: `AVFrame.data[0]` is the NV12 texture array and `data[1]` is the slice.
No readback, no `sws_scale`, no cross-device sharing.

**One swapchain for the whole wall, not one per pane.** 48 swapchains would mean 48
`Present` calls each paying the compositor hop. Instead there is a single child HWND, one
clear, one `VideoProcessorBlt` per pane into its own rectangle, and one `Present`.

**Stream resolution follows pane size.** A pane 400x225 on the wall asks the camera for
480x270, not 1080p. This is what makes a large wall affordable, and it is why maximising a
pane restarts just that one stream at a higher resolution.

## Rules that are not obvious and must not be undone

All of these were measured, several of them the hard way.

1. **Never `Present(1)`.** It holds the D3D11 device lock across the vblank wait and starves
   every decode thread: 31.9 ms per decode against 0.37 ms. In a windowed flip-model
   swapchain `Present(0)` does not tear, because DWM still composites at its own vblank.
   Only the explicit `AllowTearing` flag tears.
2. **Present is coalesced**, not per frame arrival. 48 cameras at 25 fps are 1200 arrivals a
   second, and the flip model discards the backbuffer, so every present must redraw all 48
   panes. Presenting per arrival would be 57600 blits a second for no extra displayed frames.
3. **Time to first picture is the camera's GOP, not ours.** A camera does not emit a fresh
   IDR when a stream starts, so a new pane waits for the next keyframe in the camera's own
   cycle: measured at 2790 ms on factory defaults. The stream URL deliberately does not
   override this, so it is a camera side setting like every other one that matters here.
4. **Skip `avformat_find_stream_info`** when the SDP already gave a codec id and extradata.
   It costs about 160 ms of startup and buys nothing for RTSP.
5. **Warm the video engine at startup.** The first `CreateVideoProcessor` on an idle GPU
   costs about 2 s, which would otherwise land in the first frame.
6. **`CaptureMode` lies.** It reads back as the new value while the sensor is still in the
   old mode. The reliable tell that the 50/60 mode is live is that the `WDR` parameter
   disappears. `Image.I0.Stream.FPS` is silently clamped until then. `BulkConfigurator`
   encodes this.
7. **Credentials are read live and never stored.** `Hardware.ReadPasswordHardware()` works
   from the Smart Client, which is why there is no credential store here.

## Rule actions

| Action | Inputs | Effect |
|---|---|---|
| Set low latency video wall | view profile, user | Switch the wall to a stored view |
| Clear low latency video wall | user | Show nothing |
| Show cameras on low latency video wall | cameras, user | Replace the wall with those cameras in a best fit grid |
| Show camera at a position on low latency video wall | camera, position and user | Put one camera in one numbered pane, leave the rest alone |

Every input in the rule wizard is an item picker. There is no text box and no number field
anywhere in it, which is why a user has to be an assignment item before a rule can name one,
and why `WallPositionsItemManager` generates 48 position items whose only purpose is to get a
number into a rule.

`ActionDefinition` offers exactly two pickers, and "show camera at a position" needs three
inputs, so its second picker carries both the position and the user. `ItemKinds` is a
collection and every selection comes back as an FQID that carries its own `Kind`, so one
picker holding two kinds costs nothing to read apart.

A wall a rule composes is a `ViewProfile` that is never saved, so the session builder, the
renderer and the resolution picker all treat it exactly like a stored view, including the
rebuild on resize. Best fit is the squarest grid that holds the cameras, and a position past
the end of the wall grows the grid to the smallest one that contains it.

## Measured

Renderer, against a real AXIS P3265-V, one wall of 1600x900, same camera opened N times:

| panes | streaming | total decode | pass cost | present | worst latency | resolution picked |
|---|---|---|---|---|---|---|
| 1 | 1/1 | 49.5 fps | 0.02 ms | 49.5 /s | 0.4 ms | camera default |
| 4 | 4/4 | 197.3 fps | 0.11 ms | 58.0 /s | 9.6 ms | camera default |
| 16 | 16/16 | 812.5 fps | 0.33 ms | 60.6 /s | 10.2 ms | 480x270 |
| 24 | 18/24 | 918.7 fps | 0.36 ms | 58.3 /s | 10.3 ms | 320x180 |

Zero present errors and no device loss in any run.

A wall pass costs about **0.02 ms per pane**, so 48 panes is roughly **1 ms** against the
16.7 ms budget at 60 Hz. The renderer is not the limit.

**18 is the camera's limit, not ours.** One Axis camera serves about 18 concurrent RTSP
sessions and answers `503 Service Unavailable` beyond that, which is why the 24 pane run
shows 18 streaming. A real wall has 48 different cameras and does not hit this. The run is
still useful: it shows the wall degrades correctly, with six dead panes and zero errors.

### Glass to glass, from the proof of concept

| | |
|---|---|
| packet off the socket to `Present` returning | 0.4 ms |
| packet to scanout, measured via DXGI `SyncQPCTime` | 20.4 ms |

The 20 ms is Windows desktop composition, not us, and is the floor for any plugin hosted in
a window. Camera side settings dominate everything else:

| camera configuration | glass to glass |
|---|---|
| WDR on, low latency off (factory default) | 350 ms |
| WDR on, low latency on | 182 ms |
| WDR off, low latency off | 219 ms |
| WDR off, low latency on | 140 ms |
| 50 fps capture mode, low latency on | 134 ms |

Low latency mode alone is worth about 165 ms. This is why the Camera overview node exists.

## Deployment prerequisites

- **The Smart Client role needs permission to read hardware passwords.** Credentials are
  read live through the configuration API. Without that right every pane reports it and
  stays black rather than failing silently.
- **Clients must be able to reach the cameras directly.** Only the recording server can, on
  many sites. The Camera overview node shows reachability per camera.
- Axis only. Non-Axis hardware is listed and greyed out, because VAPIX cannot drive it.

## Known limits

- Live only. No playback, no PTZ, no audio.
- No software decode fallback on the wall path. A camera that cannot do D3D11VA logs once
  and its pane stays black.
- A rule can only target a user who already has an assignment row, because the rule wizard
  offers item pickers and has no text field. Create an assignment with no view selected to
  make a user targetable.
- Bulk configuration applies immediately with no confirmation and no undo, by design. Every
  change is written to the log with its old and new value first.

## Tuning dials

`WallRenderer.TargetPresentHz` (60), `PaceOnVBlank` (false), `AdaptStreamResolution` (true),
`ResolutionSettleMs` (1500). The first number to watch is `WallStatsSnapshot.AvgPassMs`
against the 16.7 ms budget.
