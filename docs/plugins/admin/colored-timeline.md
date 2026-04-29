---
title: "Colored Timeline Plugin for Milestone XProtect"
description: "Colored Timeline plugin for Milestone XProtect: render any pair of camera events (edge motion, line crossings, object analytics, access control, alarms) as colored ribbons or per-event markers on the Smart Client playback timeline."
---

<div class="show-title" markdown>

# Colored Timeline

Built-in timeline only paints sequences in RED for motion or event/permanent recording. Edge motion and other camera-fired events do not appear visual diffrent on the playback timeline at all. This plugin closes that gap.

## Quick Start

1. Open the **Management Client**
2. Navigate to the **Timeline Rules** node in the sidebar
3. Right-click and **Create New** to add a rule
4. Give the rule a name and pick a ribbon color (or tick **Markers only** for marker-only rendering)
5. Click **Add Camera...** and select the cameras the rule applies to
6. Pick the **Start event** (and **Stop event** if you want a paired ribbon). You can also double-click a row in the **Events from the last 24 h** table to use it as the Start event; Shift+double-click sets the Stop event
7. Optionally tick **Marker** under either event side and choose an icon and color
8. Optionally tick **Auto-close** to cap unmatched Starts at a fixed timeout (the Stop event becomes optional)
9. Save. Smart Client picks up the change within a few seconds

<video controls width="100%">
  <source src="../vids/timeline.mp4" type="video/mp4">
</video>

## Prerequisites

### Device-event retention

In Management Client open **Tools > Options > Alarms and Events** and set device-event retention to at least as many days as your video retention. Ribbons and markers are rendered from historical event-log queries, so events that have aged out will not appear even if the recording still exists.

### Smart Client timeline settings

In Smart Client open **Settings > Timeline** and set both of:

- **Additional data** to **Show** (required for ribbon rendering)
- **Additional markers** to **Show** (required for marker rendering)

Without these, the plugin's ribbons and markers are silently suppressed by Smart Client. To enforce them across an installation, set the same two values in **Management Client > Smart Client Profiles > (your profile) > Timeline**. Profile values take effect once each operator's Smart Client reconnects.

## Camera Selection

Each rule applies only to the cameras listed under **Cameras**. A camera not in any rule's list gets no ribbon and no markers. The same camera can appear in multiple rules, in which case multiple ribbons stack on its timeline (one per matching rule).

- **Add Camera...** opens the Milestone camera picker
- **Remove** drops the selected camera from the list
- The Start / Stop event pickers and the events table are scoped automatically to the cameras you add

## Picking the Right Event

The **Start event** and **Stop event** pickers list every trigger event the selected cameras expose. For typical edge-motion setups you will find entries like *Motion Started (HW)* / *Motion Stopped (HW)*, plus vendor-specific names for object analytics, line crossings, license-plate hits, access-control events, and so on.

The events table on the right of the rule editor shows the most recent 24 hours of EventLog entries (newest first), so you can see what is actually firing on each camera before committing to it. Double-clicking a row sets the Start event; Shift+double-click sets the Stop event.

## Rendering Modes

A rule can render in three styles, controlled from the rule editor:

| Mode | What is drawn | When to use |
|---|---|---|
| **Ribbon** (default) | A colored span from each Start to its matching Stop | Edge motion, presence detection, anything with a clear duration |
| **Ribbon + markers** | A ribbon plus an icon at every Start and / or Stop | When you want both the span and a salient pin to scrub to |
| **Markers only** | Only the Start and / or Stop icons; no ribbon | Instantaneous events (button presses, alarms, license-plate reads, access-card swipes) |

Tick **Markers only (no ribbon)** at the top of the rule to switch to marker-only mode. The ribbon-color field hides itself, both Marker checkboxes auto-enable, and the Stop event becomes optional.

## Markers

Tick **Marker** under the Start event and / or Stop event to place an icon on the timeline at every individual event. Each side has its own:

- **Icon...** picker, with a curated set of marker-relevant FontAwesome glyphs (tags, comments, pins, flags, alerts, status, people, vehicles, access, hazards, direction arrows, and playback)
- **Color...** picker for the rendered glyph color

Hovering a marker in Smart Client shows a preview tooltip with the rule name, event display name, camera name, and timestamp.

## Auto-close

When a rule pairs Start and Stop events, an unmatched Start (no Stop event ever fires) would otherwise paint a ribbon that runs to the end of the visible window. Tick **Close pair if no stop event after** and choose a number of seconds to cap unmatched Starts at that duration.

| Setting | Behavior |
|---|---|
| **Off** (default) | Unmatched Starts are skipped entirely; only fully paired Start / Stop intervals are drawn |
| **On**, N seconds (1 - 3600) | Each unmatched Start is closed after N seconds; the **Stop event** field becomes optional |

## How a Ribbon Is Drawn

When the operator pans or zooms the Smart Client timeline, the plugin's `TimelineSequenceSource` is asked for sequences in the visible window. For each rule that applies to the camera in that view item, the plugin queries the event log scoped to the camera and the configured message text, then pairs Start / Stop:

- Each Start is paired with the next Stop at or after its timestamp
- Open intervals are either capped by the Auto-close timeout (if enabled) or skipped (if not)
- Overlapping Starts (a second Start before the previous Stop) are ignored
- The interval is drawn as a colored ribbon in the configured **Ribbon color**

Markers are rendered independently from the ribbon: every Start (or Stop) event in the visible window becomes one marker.

## Architecture

The plugin runs in two environments:

### Management Client (Admin UI)

Provides the configuration interface. 

### Smart Client (Background Plugin)

Runs invisibly in Smart Client. For every camera view item that opens, it walks the configured rules and registers a per matching rule (one ribbon source plus optional Start / Stop marker sources). Each source listens for timeline range requests and answers with paired intervals or markers queried from the event log.

## Limitations

- **Historical events only.** Live ribbon and marker updates require a pan, zoom, or seek to refresh the visible window
- **One color per rule.** To stack ribbons of different colors on the same camera, create multiple rules
- **Events must be retained.** Device-event retention must cover the playback range you want to colorize (see Prerequisites)
- **Smart Client toggles required.** Both *Additional data* and *Additional markers* must be set to *Show* in the Smart Client timeline settings (or in the Smart Client Profile), otherwise the plugin's ribbons / markers are silently suppressed

</div>
