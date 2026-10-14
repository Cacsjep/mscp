# Low Latency Video Wall - measurement report

Date: 2026-10-14

Everything below was measured on real hardware against a real camera. Nothing is estimated
unless the line says so. Where a number turned out to be invalid it is still listed, with the
reason, because knowing which measurements lie is worth as much as the ones that do not.

## Test rig

| | |
|---|---|
| GPU | NVIDIA RTX 5070 Ti |
| Decode | H.264, d3d11va, shared `ID3D11Device` with the renderer |
| Present | one flip-model swapchain, `B8G8R8A8_UNorm`, `Present(0)` |
| Camera | AXIS P3265-V at 10.0.0.48 |
| Primary display | `\\.\DISPLAY1`, 3440x1440, about 100 Hz |
| Second display | `\\.\DISPLAY2`, advertises 240 Hz |
| Wall under test | 1600x900, same camera opened N times |

Opening one camera N times is a harder test than N different cameras: every decoder sits on
one camera's stream limit and one network path.

## 1. Where the time goes, single camera

Measured with a single camera prototype of this plugin's video path, before the wall
renderer existed.

| stage | time |
|---|---|
| decode, per frame | 0.29 to 0.87 ms |
| `VideoProcessorBlt` into the pane | 0.01 to 0.14 ms |
| `Present` | 0.07 ms |
| **packet off the socket to `Present` returning** | **0.4 ms** |
| **packet to scanout, via DXGI `SyncQPCTime`** | **20.4 ms** |

The gap between those last two is the whole story: our code costs 0.4 ms, and Windows desktop
composition costs 20 ms. The 20 ms is the floor for any plugin hosted in a window and is not
something a renderer can optimise away.

### Display rate

WPF composition ticks at about 60 to 69 per second, which is the ceiling the Smart Client's own
rendering lives under and the ceiling this plugin escapes by presenting to its own HWND.

DWM delivered about 100 Hz even though `\\.\DISPLAY2` advertises 240 Hz, because DISPLAY1 is
the primary at about 100 Hz and DWM composes at the slowest of them.

## 2. Present strategy

Three strategies, same camera, same scene.

| strategy | decode cost | latency | presents/s |
|---|---|---|---|
| **on new frame (chosen)** | **0.29 ms** | **0.4 ms** | one per arrival |
| vsync paced | 0.21 ms | 6.0 ms | display rate |
| uncapped | 6.75 ms | - | 2037 |

And the one that matters most:

| | decode cost |
|---|---|
| `Present(0)` | 0.37 ms |
| `Present(1)` | **31.9 ms** |

`Present(1)` holds the D3D11 device lock across the vblank wait and starves every decode
thread. It is an 86x regression in decode cost for no visible benefit, because in a windowed
flip-model swapchain `Present(0)` does not tear: DWM still composites at its own vblank. Only
the explicit `AllowTearing` flag tears.

## 3. Startup

Time to first frame, single camera.

| configuration | TTFF |
|---|---|
| camera defaults | 2790 ms |
| `?videokeyframeinterval=5` | 495 ms |
| plus skipping `avformat_find_stream_info` | **262 ms** |

The GOP is the dominant term. A decoder cannot output anything until an IDR arrives and the
camera does not send one on demand when you join, so you wait for the next one in its cycle:
on average half a GOP, worst case a whole one. The default GOP is about 32 frames, which is
1.3 s at 25 fps and stretches further under Zipstream. At `videokeyframeinterval=5` the worst
case is 5 frames, or 200 ms. The cost is bitrate, not latency.

Skipping `avformat_find_stream_info` is worth about 160 ms and is safe here because the SDP
already carries the codec id and the extradata.

One more startup cost, measured separately: the first `CreateVideoProcessor` on an idle GPU
takes about **2 s**. That is why the renderer warms the video engine at startup rather than
paying it on the first frame.

## 4. Decode ceiling, 1080p H.264

Synthetic ceiling, no network, decode only.

| decoder | 1 stream | 4 streams |
|---|---|---|
| d3d11va | 2019 fps | 2754 fps |
| software | 938 fps | - |

At realistic camera bitrates, hardware decode:

| bitrate | 1 stream | 4 streams |
|---|---|---|
| 4.6 Mbit/s | 1994 fps | 2426 fps |
| 9.2 Mbit/s | 1680 fps | 1890 fps |
| 18.4 Mbit/s | 1139 fps | 1192 fps |

**Extrapolated, not measured:** 2426 fps at 4.6 Mbit/s divided by 25 fps is about **97
concurrent 1080p25 streams** on this GPU. Treat that as an upper bound on the decoder alone.
It says nothing about network, memory or the camera side.

## 5. The wall renderer

The real `WallRenderer`, a 1600x900 wall, the same camera opened N times, measured after the
rate meters settled on steady state.

| panes | streaming | total decode | pass cost | present | worst latency | resolution picked |
|---|---|---|---|---|---|---|
| 1 | 1/1 | 49.5 fps | 0.02 ms | 49.5 /s | 0.4 ms | camera default |
| 4 | 4/4 | 197.3 fps | 0.11 ms | 58.0 /s | 9.6 ms | camera default |
| 16 | 16/16 | 812.5 fps | 0.33 ms | 60.6 /s | 10.2 ms | 480x270 |
| 24 | 18/24 | 918.7 fps | 0.36 ms | 58.3 /s | 10.3 ms | 320x180 |

Zero present errors and no device loss in any run.

A wall pass costs about **0.02 ms per pane**, so 48 panes is roughly **1 ms** against the
16.7 ms budget at 60 Hz. The renderer is not the limit.

**Resolution adaptation works.** The 480x270 and 320x180 rows are the renderer asking each
camera for a stream that matches the pane it will be drawn into, instead of decoding 1080p and
throwing most of it away. That is the mechanism that makes a large wall affordable.

**18 is the camera's limit, not ours.** One Axis camera serves about 18 concurrent RTSP
sessions and answers `503 Service Unavailable` beyond that, which is why the 24 pane run shows
18 streaming. A real wall has 48 different cameras and never hits this. The run is still worth
keeping: six panes dead, zero errors, the wall kept running. It degrades correctly.

## 6. Glass to glass, camera side

The number an operator actually perceives. Measured with a flashing patch on the primary
monitor detected back through the camera, two repetitions, 20 s each, verified delivered fps.

| camera configuration | glass to glass |
|---|---|
| 25 fps, WDR on, low latency off (factory default) | ~350 ms |
| 25 fps, WDR on, low latency on | ~182 ms |
| 25 fps, WDR off, low latency off | ~219 ms |
| 25 fps, WDR off, low latency on | ~140 ms |
| 50 fps capture mode, no WDR, low latency off | ~171 ms |
| **50 fps capture mode, no WDR, low latency on** | **~134 ms** (min 122, max 146) |

Low latency mode alone is worth about **165 ms**. Turning WDR off is worth about another
**35 ms**. Everything the renderer does, end to end, is 0.4 ms. The camera dominates by three
orders of magnitude, which is the entire reason the Camera overview node exists.

### What did not help

- **H.264 baseline profile**: no latency benefit. The reorder delay is 0 frames on High profile
  already, and baseline costs about **14% more bitrate**. Also `videoh264profile` is silently
  ignored when passed as an RTSP argument.
- **Zipstream off**: no latency benefit, same reorder delay, more bitrate. `videozstrength=0`
  returns HTTP 400; the valid values are `off,10,20,30,40,50`.

### The capture mode trap

`ImageSource.I0.Sensor.CaptureMode` reads back as the new value while the sensor is still in
the old mode. The reliable tell that mode 2 is actually live is that the **`WDR` parameter
disappears** and returns `Error -1`. Until then `Image.I0.Stream.FPS` is silently clamped to 25.

Four earlier rows labelled "50 fps" were in fact still running at 25 fps and were discarded as
invalid. The 50 fps rows above are the re-measured ones, with delivered fps verified.

The glass to glass probe itself also had to be rewritten: the first version used an absolute
brightness threshold and failed because the camera's auto exposure hunts. It now detects a
brightness *step* relative to the frame before the flip, in the matching direction.

## 7. What this adds up to

| | |
|---|---|
| our code, packet to `Present` | 0.4 ms |
| Windows desktop composition | 20 ms |
| camera, at its factory defaults | 350 ms |
| camera, configured for low latency | 134 ms |

The plugin's own contribution is a rounding error. The work that pays is on the camera, and
the second largest term is the Windows compositor, which no plugin can avoid.
