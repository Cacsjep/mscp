<div class="show-title" markdown>

# Timelapse

A Smart Client workspace plugin that generates timelapse videos from recorded camera footage. Select one or more cameras, pick a time range, and the plugin encodes them into an MP4 video,
and plays the result directly inside the Smart Client. Supports stitching up to 9 cameras into a single grid layout.

## Quick Start

1. Open XProtect Smart Client
2. Navigate to the **Timelapse** workspace tab
3. Click **+ Add Camera** to select cameras (up to 9)
4. Choose a time range using a preset (e.g. "Last 24 Hours") or set custom start/end dates and times
5. Adjust frame interval, output FPS, and resolution as needed
6. Review the estimate displayed in the center panel
7. Click **Generate Timelapse**
8. Watch the progress as frames are fetched and encoded
9. The video plays back automatically when complete
10. Click **Save As...** to export the MP4 to disk

## Camera Selection

Click **+ Add Camera** to open the standard Milestone camera picker. You can add up to 9 cameras. When multiple cameras are selected, they are stitched into a grid layout:

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

## Settings

### Frame Interval

Controls how often a frame is grabbed from the recording. Options range from every 10 seconds to every 1 hour. Shorter intervals produce smoother but longer videos with more frames to fetch.

### Output FPS

The playback speed of the generated video (5, 10, 15, 24, or 30 FPS). Higher FPS makes the timelapse play faster relative to the frame interval.

### Resolution

| Option | Description |
|---|---|
| Original | Full camera resolution per tile |
| Half (50%) | Each tile scaled to 50% |
| Quarter (25%) | Each tile scaled to 25% |

!!! info
    For multi-camera stitch, the total video resolution is per-tile resolution multiplied by the grid dimensions. Use Half or Quarter to keep file sizes manageable with many cameras.

## Estimate

Before generating, a live estimate is shown in the center panel with a label/value grid:

- **Cameras** - number of selected cameras
- **Layout** - stitch grid (e.g. 2x2)
- **Resolution** - selected resolution option
- **Time Span** - total duration and frame interval
- **Frames** - estimated total frame count
- **Video** - estimated output video duration and FPS

The estimate updates automatically when any setting changes.

## Recording Check

Before generation starts, the plugin verifies that each selected camera has recorded data available. If a camera has no recordings, you'll be notified before any frames are fetched.

## Timestamp Overlay

Expand the **Timestamp Overlay** section to burn a date/time stamp into each frame of the video. Disabled by default.

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

Click **Save As...** to export the MP4 file to any location. You can optionally open it in your default video player.

## Advanced Settings

Expand the **Advanced** section to configure parallel processing:

### Max Workers

Number of parallel threads used to fetch frames from the recording server (1-10). More workers fetch frames faster but increase load on the recording server. Default: 5.

### Batch Size

Number of frames fetched per batch before encoding (10-200). Larger batches improve throughput but use more memory. Default: 50.

!!! info
    Memory usage is bounded to approximately `batch size x frame size` at any time. With a batch size of 200 and 1080p frames, peak memory is roughly 1-1.5 GB. Reduce batch size if memory is a concern.

## Troubleshooting

| Problem | Fix |
|---|---|
| Timelapse tab not visible | Check role permissions in Management Client. Unblock ZIP if manual install. |
| "No recordings found" error | Ensure the camera has recorded data in the selected time range. |
| Sample frame fetch fails | Verify the camera is accessible and has recordings at the start time. |
| Video is a still image | Ensure the time range spans a period with changing footage or footage at all. Check frame interval isn't larger than the range. |
| Generation is slow | Increase Max Workers in Advanced settings. Use a shorter time range or larger frame interval. Reduce resolution. |
| High memory usage | Reduce Batch Size in Advanced settings. Use Half or Quarter resolution. |
| Playback not working | The generated MP4 requires a compatible codec. Windows 10+ includes H.264 support by default. |
</div>
