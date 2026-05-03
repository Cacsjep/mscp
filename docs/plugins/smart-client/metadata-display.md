---
title: "Metadata Display Widgets for Milestone XProtect"
description: "Metadata Display plugin for Milestone XProtect Smart Client - turn ONVIF metadata channels into dashboard widgets (lamp, number, gauge, text)."
---

<div class="show-title" markdown>

# Metadata Display

Render any value from a Milestone metadata channel as a dashboard widget in the Smart Client. One widget per value, four render styles (Lamp, Number, Gauge, Text).

Built for ONVIF metadata like: Axis `CameraApplicationPlatform` analytics (area occupancy, line crossing, object counts), digital I/O port states, vendor counters - anything emitted as `tt:Message` over the metadata stream.

<video controls width="100%">
  <source src="../vids/metadata_display.mp4" type="video/mp4">
</video>

## Quick Start

1. In **Setup** mode, drag a **Metadata Display** view item into a slot
2. Click **Open configuration...**
3. **Select channel...** -> pick a metadata channel
4. Click **Start Learn** to discover the topics and data keys flowing through the stream, then pick from the dropdowns
5. Choose a render type (Lamp / Number / Gauge / Text), tune the options, hit **Save**
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

- Color shifts between **Ok / Warn / Bad** based on **Threshold Min** / **Threshold Max**
- "High value is bad" toggle inverts the direction
- Min/Max chips below the value when thresholds are configured

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
- **Track thickness** - applies to both gauge thickness and bar height
- **Show ticks** + **Count** - tick marks evenly spaced inside the arc / above the bar

### Text

Plain text passthrough. Use for string values like license plates, names, statuses where the raw value is the message. Configurable font size; no background.

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

The configuration window has a live preview pane on the right. As soon as a metadata packet matches your Topic + Data key, the preview shows the same widget that will render in the view. The status line below the preview shows the last extracted value, age, and key.

If you change the Topic or Data key, the preview re-runs against the most recent cached XML, so you don't have to wait for a fresh packet to validate the choice.

## Stale Handling

Optional. **Mark stale after (seconds)** - if no matching packet arrives within that window, the widget dims and a "stale" badge appears in the corner. Set to 0 to disable.

## No-Data Indicator

When live mode starts, the widget shows a **Waiting for data...** indicator with a pulsing dot until the first matching packet arrives. This avoids showing a misleading default state (e.g. a gray Lamp circle) before any value has actually been received.

## Playback

Playback mode is supported: as the timeline cursor moves, the widget shows the value that was emitted at that timestamp. Useful for replaying analytics events alongside recorded video.

## Storage Format

The plugin stores all settings as MIP item properties on the view item. No external configuration files. Lamp rows serialize as `value=label:#color[:IconName]|...` so they survive backup / restore.

## Troubleshooting

| Problem | Fix |
|---|---|
| "Waiting for data..." never goes away | Check the channel is recording metadata. Use Inspect packet... to see what topics are flowing. Confirm Topic match mode + Data key are correct. |
| Plugin doesn't appear in Setup | Check `MIPPlugins\MetadataDisplay\` exists. Unblock the ZIP if installed manually. |
| Wrong value shown | Topic / Data key mismatch (case-insensitive but the names must match). Use Learn to pick from the actual stream. |
| Gauge value clipped or off-center | Set the Scale Min/Max to bracket your real value range. Reduce Title font size if it's eating the canvas. |

</div>
