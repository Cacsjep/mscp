---
title: "Live Exporter Plugin for Milestone XProtect"
description: "Live Exporter plugin for Milestone XProtect Smart Client - toolbar flyout that mirrors the most recently clicked camera in independent playback and lets the operator mark a (start, end) range and add it to the Smart Client export list with a single click."
---

<div class="show-title" markdown>

# Live Exporter

Adds a **Live Exporter** button to the Live workspace toolbar. Clicking it opens a floating window that mirrors whichever camera the operator most recently clicked - tile, legacy Map icon, Smart Map item, or camera tree pick - in independent playback with its own scrubber. The operator scrubs to the desired start, clicks **Set start**, scrubs to the end, clicks **Set end**, then **Add to Export** drops the (camera, start to end) pair into the Smart Client's built-in export list with a native confirmation toast.

## Quick Start

1. Install the plugin and open Smart Client.
2. Switch to the **Live** workspace.
3. Click the **Live Exporter** button in the top toolbar. The flyout opens.
4. Click any camera in Smart Client (tile, Map icon, Smart Map item, or camera tree). The flyout loads that camera and starts playback.
5. Scrub the playback timeline to the desired start point.
6. Click **Set start**.
7. Scrub forward to the desired end point.
8. Click **Set end**.
9. Click **Add to Export**. Smart Client confirms with a toast and the (camera, start to end) pair appears in the Exports workspace.

## How camera selection follows clicks

The flyout subscribes to the Smart Client's global hotspot controller - the same selection signal the built-in Hotspot view item uses. Anything that puts a camera into the global selection updates the flyout:

| Source | Update? |
|---|---|
| Tile click | Yes |
| Legacy Map icon click | Yes |
| Smart Map item click | Yes |
| Camera tree double-click | Yes |
| Alarm list selection | Yes |

When the camera changes, any previously captured **Start** and **End** are cleared - they were anchored to the previous camera's timeline.

## How the timeline works

The flyout's video uses its own private playback controller, independent of any tile in the Smart Client view. Switching cameras, scrubbing, or closing the flyout does not affect the rest of the Smart Client.

| Action | Effect |
|---|---|
| Scrub the playback bar | Moves the playhead inside the flyout only. |
| Click **Set start** | Snapshots the current playhead time as the export start. |
| Click **Set end** | Snapshots the current playhead time as the export end. |
| Click **Reset** | Clears both captured times. |
| Click **Add to Export** | Sends the (camera, start to end) pair to the export list, shows a Smart Client toast, then auto-resets so the operator can mark another range. |
| Change camera (any click in Smart Client) | Loads the new camera and clears any pending Start / End. |

The **Add to Export** button is enabled only when a camera is loaded and both Start and End are set, with End strictly later than Start.

## Where it works

| Workspace | Behavior |
|---|---|
| **Live** | Flyout is available. |
| **Playback** | Flyout closes automatically; use the workspace's native export tools. |
| **Setup** | Flyout closes automatically. |

The toolbar button itself is always enabled in Live - even before the operator has clicked any camera. The flyout shows a "click any camera in Smart Client" hint until a selection arrives.

## Troubleshooting

| Problem | Fix |
|---|---|
| Live Exporter button missing from the toolbar | Plugin DLLs must be in `MIPPlugins\LiveExporter\`. Unblock the ZIP if you copied it manually. Restart Smart Client. |
| Flyout stays on "no camera selected" after clicking the Map | Confirm you clicked an actual camera icon, not an empty Map area or a hot zone. Check `MIPLog.txt` under category `LiveExporter` for `GlobalHotspotController unavailable` - this means the installed Smart Client version does not expose the internal API the plugin needs. |
| Add to Export does nothing | The button is disabled until both Start and End are set, with End later than Start. The status line beneath the chips shows why it is disabled. |
| Toast confirmation missing | The plugin sets `ShowConfirmationToasts = true` on the export command. If toasts are disabled globally in Smart Client settings, the export still happens silently - check the Exports workspace. |

</div>
