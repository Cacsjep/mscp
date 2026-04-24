---
title: "Timelapse Video Plugin for Milestone XProtect"
description: "Timelapse plugin for Milestone XProtect Smart Client. Generate timelapse videos from recorded cameras with Continuous and Event-based modes and multi-camera grid support."
---

<div class="show-title" markdown>

# Timelapse

A Smart Client workspace plugin that generates timelapse videos from recorded camera footage. Select one or more cameras, pick a time range, choose between **Continuous** or **Event-based** sampling, and the plugin encodes the result into an MP4 and plays it back inside the Smart Client. Supports stitching up to 9 cameras into a single grid.

## Quick Start

1. Open XProtect Smart Client
2. Navigate to the **Timelapse** workspace tab
3. Click **+ Add Camera** to select cameras (up to 9)
4. Choose a time range using a preset or set custom start/end dates and times
5. Pick a **Mode**. Continuous for permanent recordings, Event-based for motion or trigger-driven recordings
6. Adjust the mode-specific sampling settings, plus Output FPS and Resolution
7. Review the **Recording Sequences** and **Output Estimate** cards in the center panel
8. Click **Generate Timelapse**
9. Watch the progress as frames are fetched and encoded
10. The video plays back automatically when complete; **Save As…** exports the MP4

<video controls width="100%">
  <source src="../vids/timelaps_usage.mp4" type="video/mp4">
</video>

## Camera Selection

Click **+ Add Camera** to open the standard Milestone camera picker. Up to 9 cameras are supported. Multiple cameras are stitched into a grid:

| Cameras | Layout |
|---|---|
| 1 | 1x1 |
| 2 | 2x1 (side by side) |
| 3 | 3x1 |
| 4 | 2x2 |
| 5-6 | 3x2 |
| 7-9 | 3x3 |

Use **Clear** to remove all cameras at once.

## Time Range

### Presets

The preset dropdown provides quick time range selection:

- Last 4 Hours, Last 8 Hours, Last 24 Hours
- Last 2 Days, Last 4 Days, Last 6 Days
- Last Week, Last 2 Weeks, Last 3 Weeks, Last 4 Weeks

Selecting a preset automatically fills in the start/end date and time fields.

### Custom Range

Set the start and end date/time manually using the date pickers and hour/minute dropdowns. Start and end are shown side by side for easy comparison.

## Modes

Both modes use Milestone's native `RecordingSequence` data source as the ground truth. Only time ranges that actually contain recorded video on disk are sampled, so gaps never produce wasted frames.

### Continuous

**Use this for permanent recordings**, e.g. a construction site camera recording 24/7. The plugin walks each recording segment at a fixed spacing (**Frame Interval**). Gaps without recording are skipped automatically, so a range that contains partial coverage produces a shorter, dense timelapse rather than a long video padded with black frames.

Settings shown in Continuous mode:

- **Frame Interval**: spacing between sampled frames, from 10 s to 1 h

### Event-based

**Use this for motion- or trigger-driven recordings.** Each `RecordingSequence` block is treated as one event. Per event the plugin always includes the first frame, then samples additional frames at **Interval per event** until it reaches **Max frames / event**. Very short events fall back to evenly distributing **Min frames / event** across the event's duration.

Adjacent events separated by less than the **Event Merge Gap** (Advanced) are merged into a single event, so server-side fragmentation (e.g. a continuous motion stored as three back-to-back clips) does not produce duplicate "first frames" in the output.

Settings shown in Event-based mode:

| Setting | Default | Purpose |
|---|---|---|
| Interval per event | 10 s | Spacing between frames inside one event |
| Max frames / event | 10 | Cap that prevents one long event from dominating |
| Min frames / event | 1 | Floor for very short events |
| Event Merge Gap (Advanced) | 2 s | Adjacent events merged below this threshold |

## Output Settings

### Output FPS

Playback speed of the generated video (5, 10, 15, 24, or 30 FPS). Total frames ÷ FPS = video length. Lower FPS plays better for short frame counts; 24-30 feels like a normal video but requires more frames.

### Resolution

| Option | Description |
|---|---|
| Original | Full camera resolution per tile |
| Half (50%) | Each tile scaled to 50% |
| Quarter (25%) | Each tile scaled to 25% |

!!! info
    For multi-camera stitch, the total video resolution is per-tile resolution multiplied by the grid dimensions. Use Half or Quarter to keep file sizes manageable with many cameras.

## Recording Sequences Card

Whenever cameras or the time range change, the plugin queries the server for the actual recording blocks in the selected window (debounced, 300 ms). The **Recording Sequences** card in the center panel shows three headline metrics:

- **Sequences**: total recording blocks across all selected cameras
- **Recorded**: sum of all block durations
- **Coverage (max)**: highest per-camera `recorded ÷ window` ratio, so you can see at a glance whether any camera covered the full range

Expand **Per-camera breakdown** for a row per camera. Cameras with zero sequences are shown in red. They are not a hard failure (they appear as placeholder cells in the final video), but you usually want to either remove them or extend the range.

While the query is running a small loading row appears inside the card, and the hero clock icon turns into a spinner, so long queries (multi-day ranges on busy servers) are clearly visible instead of looking frozen.

## Output Estimate Card

The **Output Estimate** card updates live based on preflight data plus the current mode and sampling settings:

- **Cameras**: number of selected cameras
- **Layout**: stitch grid, e.g. 2x2
- **Resolution**: selected resolution option
- **Time Span**: total window and the effective interval label
- **Frames**: estimated total frame count after segment filtering
- **Video**: estimated output video duration at the selected FPS

Numbers here are real, not naive. They reflect the segments returned by the server, so "Frames" is the actual count you will get, not window ÷ interval.

## Multi-camera Timeline

When more than one camera is selected, the plugin builds a **union timeline** of all cameras' recording segments. Every generated frame is a single wall-clock moment, so all grid cells show the same time. A camera that was not recording at that moment renders one of two fallbacks depending on mode:

- **Continuous**: the cell shows that camera's last known frame, dimmed with a small "no recording" badge. Preserves the "scene continues" feel of continuous timelapse.
- **Event-based**: the cell is a black placeholder with the camera name and "no event" text. Makes it unambiguous that the cell has no footage for this moment rather than a frozen scene being current.

A camera with zero sequences in the range is not a hard failure. It simply shows the mode-appropriate fallback throughout the video.

## Timestamp Overlay

Expand the **Timestamp Overlay** section to burn a date/time stamp onto the whole canvas (not per cell). Disabled by default.

| Setting | Options | Default |
|---|---|---|
| Show Timestamp | On / Off | Off |
| Position | Top-Left, Top-Right, Bottom-Left, Bottom-Right | Bottom-Left |
| Format | Date + Time, Time only, Date only | Date + Time |
| Color | White, Black, Yellow, Red | White |
| Background | Dark shadow, Light shadow, None | Dark shadow |
| Font Size | Small, Medium, Large, Extra Large | Medium |

The semi-transparent background ensures readability regardless of scene content.

## Playback

After generation completes, the video plays automatically in the center panel with transport controls:

| Control | Action |
|---|---|
| Play / Pause | Toggle playback |
| Stop | Stop and reset to beginning |
| Seek slider | Drag to scrub; pauses during drag and resumes playback from new position |
| Time display | Shows current position and total duration |
| ✕ (Close) | Close the preview, release the temp MP4, and return to the configuration/estimate view |

Click **Save As…** to export the MP4 file to any location. You can optionally open it in your default video player.

## Advanced Settings

Expand the **Advanced** section:

### Max Workers

Number of parallel threads used to fetch frames from the recording server (1-10). More workers fetch frames faster but increase load on the recording server. Default: 5.

### Batch Size

Number of frames fetched per batch before encoding (10-200). Larger batches improve throughput but use more memory. Default: 50.

!!! info
    Memory usage is bounded to approximately `batch size × frame size` at any time. With a batch size of 200 and 1080p frames, peak memory is roughly 1-1.5 GB. Reduce batch size if memory is a concern.

### Event Merge Gap

Event-based mode only. Two recording blocks separated by less than this gap are merged into one event, so you don't get duplicate first frames when the server fragments a continuous motion into several short sequences. Default: 2 s.

## Tooltips

Every non-obvious field has a hover tooltip explaining what it does and how it interacts with the other settings. Tooltips stay open while the cursor is over the field, so you can read them comfortably.

## Logging

The plugin writes to `C:\ProgramData\Milestone\XProtect Smart Client\MIPLog.txt` under the categories `Timelapse` and `Timelapse.SequenceQuery`. Useful entries during generation:

- `Preflight start`: time range and camera count for the sequence query
- `GetData returned N raw entries`: how many sequences the server returned per camera
- `Preflight done: totalSeq=N totalRecorded=… coverageMax=…%`: summary posted to the UI
- `Generate: mode=… unionSegments=… timestamps=…`: sampling decision before encoding
- `Generate complete: output='…'` or `Generate failed`: final state

When reporting issues, attach the relevant window from this log.

## Troubleshooting

| Problem | Fix |
|---|---|
| Timelapse tab not visible | Check role permissions in Management Client. Unblock the ZIP if installed manually. |
| Preflight shows 0 sequences | The selected range has no recorded video for that camera. Extend the range or pick a different camera. Also verify the Smart Client clock and the server are in sync. |
| "No recordings found in the selected time range for any camera" | Every camera returned zero segments. Same causes as above. |
| Output video is very short / looks still | Frame count is too low for the selected FPS (e.g. 10 frames ÷ 24 FPS ≈ 0.4 s). Lower FPS, raise Max frames / event, shorten the event interval, or pick a range with more recorded content. |
| Output video will not play | MediaElement struggles with sub-1-second MP4s. Produce a longer clip (see above). |
| Sample frame fetch fails | Verify the camera is accessible and has recordings at the start time. |
| Generation is slow | Increase Max Workers in Advanced settings. Shorten the time range, raise the frame interval, or reduce resolution. |
| High memory usage | Reduce Batch Size in Advanced settings. Use Half or Quarter resolution. |
| Playback not working | The generated MP4 requires a compatible codec. Windows 10+ includes H.264 support by default. |
| Build of the plugin hangs | Close any running Smart Client. It holds `Timelapse.dll` open and prevents IL-repack from overwriting it. |

</div>
