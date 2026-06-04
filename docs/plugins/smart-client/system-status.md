---
title: "System Status Plugin for Milestone XProtect"
description: "System Status plugin for Milestone XProtect Smart Client - a toolbar button that opens a System Health window with three tables: recording servers and their storage, every enabled camera with live stream statistics (FPS, bitrate, resolution, codec), used storage and recording span, and the currently connected users."
---

<div class="show-title" markdown>

# System Status

Adds a **System Status** button to the Smart Client toolbar. Hovering the button shows a live summary such as `3/19 Cameras  4 Users`. Clicking it opens the **System Health** window, a resizable overview with three tables: recording servers and their storage, cameras with live stream statistics, and connected users.

## Quick Start

1. Install the plugin and open Smart Client.
2. In the Live or Playback workspace, find the **System Status** button in the top toolbar.
3. Hover the button to read the quick counts in the tooltip.
4. Click the button to open the System Health window. Click again, press **Esc**, or click the **X** to close it.

<video controls width="100%">
  <source src="../vids/sys.mp4" type="video/mp4">
</video>

## What it shows

The window stacks three tables, each with its own section header, summary line, and CSV export button. The dividers between them can be dragged to give a table more room, and the whole window can be enlarged up to your screen.

### Recording Servers

One row per storage on each recording server, plus the recorder's attach and connection state.

| Column | Meaning |
|---|---|
| Status dot | Green when the recorder is attached and connected, red otherwise. |
| Recorder | The recording server host. |
| State | The attach and connection state reported by the recorder. |
| Storage | The recording or archive storage name. |
| Path | The media database path. |
| Storage usage | A bar showing used percent, green under 90, orange under 95, red at 95 and above. Hover for the used, free, and total figures. |

### Cameras

One row per enabled camera. A toggle above the table switches between **Cameras** and **Streams**.

In **Cameras** mode each row aggregates the camera and its streams:

| Column | Meaning |
|---|---|
| Status dot | Green online, red offline, gray when the camera's recorder did not answer (state unknown). |
| Camera | The camera name. |
| Recorder | The owning recording server. Shown in red when that recorder is offline. |
| Streams | How many video streams the recorder is currently serving for the camera. |
| Resolution, Codec, FPS, Bitrate | Live figures for the camera's primary stream (FPS and bitrate update on the live refresh). |
| Storage used | How much recording space the camera occupies on its recorder. |
| Storage % | That used space as a percentage of the recorder's total configured storage. |
| First rec, Last rec | The oldest and newest recorded timestamps, loaded when the window opens or refreshes. |
| Span | The recorded coverage between first and last, shown compactly (for example `89.9 Days`). |

In **Streams** mode the table flattens to one row per stream across all cameras, so you can see every stream at once without expanding anything. It lists camera, recorder, stream name, resolution, codec, FPS, requested FPS, bitrate, and the stream role (recording, live, or both).

### Users

One row per connected user, with the client type.

| Client type | Source |
|---|---|
| **Smart Client** | An operator running the Smart Client. |
| **Management Client** | An administrator running the Management Client. |
| **Standalone** | A standalone MIP application or integration. |

Background services such as the Event Server and the Log Server are filtered out, so the list reflects real people and integrations rather than system accounts. When one user holds more than one session, the row shows a count such as `Smart Client (x2)`.

## Working with the tables

| Action | How |
|---|---|
| Sort | Click any column header. An arrow shows the active sort direction. Numbers, sizes, FPS, bitrate, storage, and span sort by value. |
| Filter cameras | Type in the **Filter** box to match camera or recorder name, or use the **All / Online / Offline** buttons. The filter also applies in Streams mode. |
| Group | The **Group** button groups cameras by recorder, and streams by camera, with collapsible group headers. |
| Resize columns | Drag the divider between two column headers. |
| Export | The CSV icon on each table exports exactly what is shown, respecting the current sort and filter. |

## Live auto-refresh

The **Auto 2s** button toggles live updates. While it is on, the camera stream figures (FPS, bitrate, resolution, online state, used storage) refresh every two seconds. The update is merged in place, so your selection, scroll position, and sort order are preserved and the recording-range cells are not re-queried on every tick. Storage figures and recorder state refresh on a slower cycle and on a manual **Refresh** (or **F5**). Turn auto off to hold the current view. All querying stops the moment the window is closed.

## How it works

Two layers feed the window.

A background component runs for the whole Smart Client session and talks to the Event Server over one message channel. It keeps the per-camera online state and the connected-user list current, which powers the toolbar tooltip and the online dots even before the window is opened. This layer is light and does not talk to the recording servers.

While the System Health window is open, it queries each recording server's status service directly for storage, recorder state, and the live video statistics of that recorder's cameras. These per-recorder calls run in parallel. First and last recorded timestamps are read from the recorded-sequence data for each camera when the window opens or refreshes.

| Question | Source |
|---|---|
| Which cameras are enabled | The configuration device tree, walked down to the real camera devices. |
| Which cameras are online | The current device state reported by the Event Server. |
| FPS, bitrate, resolution, codec, used storage | The recording server's status service (video device statistics). |
| Storage used and free per server | The recording server's recording and archive storage status. |
| First and last recording, span | The recorded-sequence data for the camera. |
| Who is connected, and client type | The MIP environments currently connected to the Event Server. |

## When a recording server is offline

If a recording server does not answer, its cameras are shown with a gray dot and the recorder name in red, since their real state is unknown. That recorder still appears in the Recording Servers table with a red dot and an `Unknown` state. The plugin fails fast on an unreachable recorder rather than waiting on long timeouts, and it skips recording-range queries for those cameras. When the server comes back, storage, state, and recording ranges recover on their own within a few seconds.

## Where it works

| Workspace | Behavior |
|---|---|
| **Live** | Button visible, window available. |
| **Playback** | Button visible, window available. |

## Permissions

The connected user needs the rights to read device status, storage and recorder status, recorded sequences, and to see other connected clients. If the logged-in account lacks those rights, cameras can read as offline, storage and statistics can come back empty, and the user list can be short. Use an account with the appropriate view and status permissions.

## Troubleshooting

| Problem | Fix |
|---|---|
| System Status button missing from the toolbar | Plugin DLLs must be in `MIPPlugins\SystemStatus\`. Unblock the ZIP if you copied it manually, then restart Smart Client. |
| Cameras show but stream statistics are empty | A camera only reports statistics while it is actively streaming or recording. An enabled but idle camera shows online with no live figures. |
| A whole recorder's cameras are gray | That recording server did not answer. Confirm it is running and reachable from the client, then check `MIPLog.txt` under category `SystemStatus - SC BG`. |
| Storage % is empty for a camera | The percentage needs the recorder's total configured storage, which arrives with the storage refresh. It fills in once a full refresh completes. |
| First and last recording read as a dash | Ranges load on open and refresh, and are skipped for offline recorders. A failed query reads as `error` in `MIPLog.txt`. |
| Performance on large systems | The log records per-recorder timings on each full fetch and flags any recorder slower than 1.5 seconds, which helps pinpoint a slow or unreachable server. |

</div>
