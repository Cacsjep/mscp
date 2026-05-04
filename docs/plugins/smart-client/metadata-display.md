---
title: "Metadata Display Widgets for Milestone XProtect"
description: "Metadata Display plugin for Milestone XProtect Smart Client - turn ONVIF metadata channels into dashboard widgets (lamp, number, gauge, text, line chart)."
---

<div class="show-title" markdown>

# Metadata Display

Render any value from a Milestone metadata channel as a dashboard widget in the Smart Client. One widget per value, five render styles (Lamp, Number, Gauge, Text, Line Chart).

Built for ONVIF metadata like: Axis `CameraApplicationPlatform` analytics (area occupancy, line crossing, object counts), digital I/O port states, vendor counters - anything emitted as `tt:Message` over the metadata stream.

<video controls width="100%">
  <source src="../vids/metadata_display.mp4" type="video/mp4">
</video>

## Prerequisites

Before the widget can show anything, the camera and the VMS need to be set up to actually deliver metadata to the Smart Client:

- **Metadata channel enabled on the camera** - in Management Client, expand the camera under **Devices > Metadata**, select the metadata channel, and on its **Settings** tab set **Metadata stream > Event data** to **Yes**. This is what makes the camera publish its ONVIF events (analytics, I/O port states, vendor counters) on the stream the plugin reads. **Analytics data** and **PTZ data** can stay **No** unless you specifically need bounding boxes or PTZ position - the widget reads `tt:Message` items, which arrive under Event data.
- **Metadata recording enabled** - on the metadata channel, set **Recording** to enabled. This is required for the **Playback** mode and for the Line Chart's archive backfill (the wide-window view, the cold-start seed in Playback, and the in-pane window picker all read from recorded data). Without recordings the widget still works in Live mode but cannot show any history.
- **Recording rule for the metadata channel** - in Management Client under **Rules**, make sure a rule records the metadata channel (either always-on, or on the same trigger as the corresponding video). The default recording rule typically covers cameras but not their metadata channels - check that the rule's device list includes the metadata items, otherwise nothing ends up in the archive even though recording is "enabled" on the channel.
- **User permissions** - the Smart Client user needs **Live** and **Playback** rights on the metadata channel. Without these the channel won't appear in the configuration's channel picker, or the widget will sit on **Waiting for data...** forever.

If the configuration's **Inspect packet...** button shows fresh XML and Learn discovers topics, prerequisites are met. If it stays empty, the channel is not flowing - fix the camera or rule before tuning the widget.

## Quick Start

1. In **Setup** mode, drag a **Metadata Display** view item into a slot
2. Click **Open configuration...**
3. **Select channel...** -> pick a metadata channel
4. Click **Start Learn** to discover the topics and data keys flowing through the stream, then pick from the dropdowns
5. Choose a render type (Lamp / Number / Gauge / Text / Line Chart), tune the options, hit **Save**
6. Switch to **Live** - the widget starts displaying as soon as a matching packet arrives

## Render Types

### Lamp

Discrete state indicator. Map raw values to a label, color, and optional FontAwesome icon. Ideal for I/O ports, alarm states, on/off counters.

- Each row: `value -> label : color : icon`
- Icon picker has ~140 curated solid icons with search
- Falls back to a colored circle when no icon is set
- Unmapped values render with the raw value as the label

### Number

Big-number readout with optional unit suffix and threshold colors.

- Value and unit share the same baseline so the readout looks like a single piece of typography (e.g. `43.99 km/h`)
- Color shifts between **Ok / Warn / Bad** based on **Threshold Min** / **Threshold Max** when **Enable thresholds** is on
- "High value is bad" toggle inverts the direction
- Min/Max chips below the value use a warn triangle for the lower bound and a critical circle for the upper bound, color-coded to the threshold direction

### Gauge

Five styles, all driven by the same numeric/threshold settings as the Number widget plus a **Scale Min/Max** range:

| Style | When to use |
|---|---|
| **Modern - Half arc (180°)** | Compact KPI tile, single quick-glance value |
| **Modern - Three-quarter arc (270°)** | More resolution per unit of arc; bigger needles |
| **Classic - Half arc (180°)** | Traditional speedometer with three colored bands and needle |
| **Classic - Three-quarter arc (270°)** | Classic style with extra range |
| **Bar** | Side-by-side rows; reads well at small heights |

Modern styles draw a single same-thickness progress arc that fills the threshold color. Classic styles draw three colored bands plus a needle. Both honor:

- **Show value** + **Font size** for the inline numeric readout
- **Track thickness** - applies to both gauge thickness and bar height (default 6 for arc gauges, 2 for the bar; max 20)
- **Show ticks** + **Count** - tick marks evenly spaced inside the arc / above the bar
- **Enable thresholds** - turns the colored band model on or off; when off, gauges show a single neutral track

The Bar gauge places the value, unit, and the Min/Max scale labels in a single row underneath the bar so nothing collides with the ticks above. The vertical value indicator is intentionally thin so it doesn't hide the underlying scale.

### Text

Plain text passthrough. Use for string values like license plates, names, statuses where the raw value is the message. Configurable font size; no background.

### Line Chart

Time-series chart for numeric values. Plots a rolling history of the data key against time, with optional thresholds, an envelope (min/max band), zoom and pan, and an in-pane time-window picker.

- **Time window** - choose how far back the chart looks. Presets cover **60 seconds**, **5 / 10 / 30 minutes**, **1 hour**, **6 hours**, and **24 hours**, plus a free-form **Custom** entry in seconds. The saved value is the default; the in-pane picker (top-right of the chart) lets viewers temporarily switch windows without going into Setup mode.
- **Backfill from archive** - long windows (over 60 seconds) are seeded from recorded data when the chart appears, so a 6-hour view shows real history immediately instead of waiting 6 hours to fill. A loading spinner is shown while the seed query runs.
- **Aggregation** - **Mean** (default), **Min**, or **Max**. The chart aggregates samples into time buckets sized for the chosen window so very wide windows still render quickly. With **Show envelope** enabled, the chart additionally draws a dashed min/max band around the aggregated line.
- **Line type** - **Straight** (default), **Smooth** (curved), or **Step** (discrete level changes).
- **Line color**, **thickness**, **fill area**, **show markers** - styling for the main series.
- **Y-axis Min / Max** - clamps the value axis. Leave blank to auto-fit.
- **Thresholds** - when **Enable thresholds** is on, warn (Min) and critical (Max) values are drawn as dashed horizontal lines (no filled bands - the line stays readable through them).
- **Zoom and pan** - mouse wheel zooms the time axis; drag pans. While zoomed in **Live** mode the chart auto-pauses (a small "Paused (click to resume live)" badge appears) so the view does not jump as new data arrives. Click the badge to resume the rolling window.

#### In-pane Window Picker

The small badge in the top-right corner of the chart shows the current effective window (e.g. `5m`, `6h`). Click it to temporarily switch:

- Pick any preset (60 seconds up to 24 hours) - the chart is reloaded for the new range, with a loading indicator while the archive backfill runs
- Pick **Default (xx)** to revert to the saved Setup value
- An asterisk (e.g. `1h*`) on the badge label means a session override is active

The override is **session only**: it is dropped when the configuration is saved or the view is reopened, and never modifies the saved configuration. It needs no permission and applies to whichever pane is in front of you.

#### Live vs Playback

- **Live** - new samples stream in and the right edge advances. Auto-pause kicks in when you zoom or pan so you can study a region without it sliding away.
- **Playback** - zoom and pan are always on (there is no live tail to fight with) and the auto-pause badge is suppressed. Moving the timeline cursor moves the chart's cursor line; jumping further than half the visible window triggers a fresh range scan from the archive. The chart seeds itself at the current playback time on entry, so you do not need to scrub once to populate it.

## What to Read

The "What to read" section is the bridge from the raw stream to a single value:

- **Topic match** - filter incoming messages by ONVIF topic, with **Contains / Exact / EndsWith** matching modes (default: Exact)
- **Data key** - the `tt:SimpleItem Name` to pull the value from
- **Inspect packet...** - opens a syntax-highlighted XML viewer of the latest captured packet, so you can see exactly what the camera is emitting
- **Source filter** (advanced) - constrain to specific Source SimpleItem name=value pairs (e.g. `port=1` for I/O input port 1)

### Learn

Click **Start Learn** to subscribe to the live stream and watch the discovered topics + data keys populate the dropdowns. Stop when you have what you need. The data-key dropdown is filtered to only the keys observed under the currently-selected topic, so changing the topic refreshes the available keys.

## Title and Density

Every widget has the same theme tokens (palette, type scale) so a wall of mixed widgets reads coherently.

- **Title** - optional title text above the widget. Position **Left / Center / Right**, font size, color
- **Density** - **Compact** (0.82x), **Comfortable** (1.0x), **Spacious** (1.18x). Multiplier applied to non-user-set sizes (lamp label, number value/unit, gauge unit, title) so widgets line up across panes without forcing identical absolute sizes everywhere

User-set sizes (Title font size, Gauge value font size, Text font size, Lamp icon size) are **not** scaled by Density - if you set them explicitly, they are taken as-is.

## Live Preview

The configuration window has a live preview pane on the right. As soon as a metadata packet matches your Topic + Data key, the preview shows the same widget that will render in the view. Until a value arrives, the preview shows the same **Waiting for data...** indicator that the live view uses, so what you see in Setup matches what users will see at runtime.

If you change the Topic or Data key, the preview re-runs against the most recent cached XML, so you don't have to wait for a fresh packet to validate the choice. The Line Chart preview is sized at 16:9 inside the configuration window so the chart shape matches the runtime pane.

## Stale Handling

Optional. **Mark stale after (seconds)** - if no matching packet arrives within that window, the widget dims and a "stale" badge appears in the corner. Useful for catching dead channels: if the camera silently stops emitting metadata, the widget visibly fades instead of pretending the last value is still current. Set to 0 to disable.

## No-Data and Loading Indicators

A pulsing dot with a status line is shown:

- **Waiting for data...** - live mode is up, no matching packet yet
- **Loading recent values from archive...** - line chart cold-start in playback mode while the seed query runs
- **Loading last 6h from archive...** - line chart cold-start in live mode (or after a window switch) while history is being backfilled

The chart appears as soon as the loading step finishes, regardless of whether any samples were found.

## Playback

Playback mode is supported: as the timeline cursor moves, the widget shows the value that was emitted at that timestamp. Useful for replaying analytics events alongside recorded video.

For Line Chart specifically, the chart is seeded with archive data on entry (no scrubbing needed to populate), the cursor line tracks the timeline position, and large jumps trigger a fresh range scan around the new cursor.

## Storage Format

The plugin stores all settings as MIP item properties on the view item. No external configuration files. Lamp rows serialize as `value=label:#color[:IconName]|...` so they survive backup / restore.

## Troubleshooting

| Problem | Fix |
|---|---|
| "Waiting for data..." never goes away | Check the channel is recording metadata. Use Inspect packet... to see what topics are flowing. Confirm Topic match mode + Data key are correct. |
| Plugin doesn't appear in Setup | Check `MIPPlugins\MetadataDisplay\` exists. Unblock the ZIP if installed manually. |
| Wrong value shown | Topic / Data key mismatch (case-insensitive but the names must match). Use Learn to pick from the actual stream. |
| Gauge value clipped or off-center | Set the Scale Min/Max to bracket your real value range. Reduce Title font size if it's eating the canvas. |
| Line Chart starts empty for a wide window | Recorded metadata is required for backfill. Confirm a recording rule covers the metadata channel (see Prerequisites). Without recordings the chart still works, it just fills only with new live samples. |
| Channel does not appear in the configuration channel picker | Either the metadata stream is not enabled on the camera, the user has no rights on the channel, or the channel is in a folder you cannot browse. See Prerequisites. |
| Playback view stays empty | The metadata channel exists but is not being recorded. Add or extend a recording rule so the metadata channel is included. |
| Line Chart "Paused" badge keeps appearing | You are zoomed in or panned in Live mode, which auto-pauses to keep the view stable. Click the badge to resume the rolling live window. |
| In-pane window picker change does not persist | This is by design - the picker is session-only. Open the configuration and change **Time window** there to make it permanent. |

</div>
