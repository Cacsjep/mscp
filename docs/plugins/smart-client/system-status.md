---
title: "System Status Plugin for Milestone XProtect"
description: "System Status plugin for Milestone XProtect Smart Client - a toolbar button and flyout that shows how many enabled cameras are online versus their total, lists each camera with its online state, and lists the currently connected users together with the client type each one is using (Smart Client, Management Client, Standalone)."
---

<div class="show-title" markdown>

# System Status

Adds a **System Status** button to the Smart Client toolbar. Hovering the button shows a live summary such as `3/19 Cameras  4 Users`, and clicking it opens a flyout with two lists: every enabled camera with its current online state, and every currently connected user with the client type they are using.

## Quick Start

1. Install the plugin and open Smart Client.
2. In the Live or Playback workspace, find the **System Status** button in the top toolbar.
3. Hover the button to read the quick counts in the tooltip.
4. Click the button to open the flyout. Click again, press **Esc**, or click the **X** to close it.

<img src="../img/sysstat1.PNG" width="75%">
<img src="../img/sysstat2.PNG" width="75%">

## What it shows

The flyout has two sections that refresh on their own while it is open.

### Cameras

One row per enabled camera, with a colored dot and an online state label.

| Indicator | Meaning |
|---|---|
| Green dot, **Online** | The recording server currently reports the camera as responding. |
| Red dot, **Offline** | The camera is enabled but not responding right now. |

The section header shows the online count over the total, for example `ONLINE 3 / 19`. Only enabled cameras are counted. Disabled devices are never shown.

### Users

One row per connected user, with the client type on the right.

| Client type | Source |
|---|---|
| **Smart Client** | An operator running the Smart Client. |
| **Management Client** | An administrator running the Management Client. |
| **Standalone** | A standalone MIP application or integration. |

Background services such as the Event Server and the Log Server are filtered out, so the list reflects real people and integrations rather than system accounts. When one user holds more than one session, the row shows a count such as `Smart Client (x2)`.

## Show only offline

The Cameras section has a **Show only offline** toggle. Turning it on filters the list to cameras that are currently offline, which is useful on large systems where you only want to see what needs attention. The header switches to `OFFLINE n / total` while the toggle is active. Turn it off with **Show all**.

## How it works

All data is gathered by a single background component that runs for the whole Smart Client session and talks to the Event Server over one message channel. The toolbar button and the flyout are thin views that subscribe to it, so they open already populated and update together.

| Question | Source |
|---|---|
| Which cameras are enabled | The configuration device tree, walked down to the real camera devices. |
| Which cameras are online | The current device state reported by the Event Server, where a responding camera counts as online. |
| Who is connected | The list of MIP environments currently connected to the Event Server. |
| Which client type each user has | The server type carried on each connected endpoint. |

The data refreshes on a short interval and also when the set of connected clients changes, so the counts stay current without any operator action.

## Where it works

| Workspace | Behavior |
|---|---|
| **Live** | Button visible, flyout available. |
| **Playback** | Button visible, flyout available. |

## Permissions

The connected user needs the rights to read device status and to see other connected clients. If the logged-in account lacks those rights, cameras can read as offline and the user list can come back empty. Use an account with the appropriate view and status permissions.

## Troubleshooting

| Problem | Fix |
|---|---|
| System Status button missing from the toolbar | Plugin DLLs must be in `MIPPlugins\SystemStatus\`. Unblock the ZIP if you copied it manually, then restart Smart Client. |
| All cameras read as offline | The account may lack status rights, or the recording servers are not reachable from the client. Check `MIPLog.txt` under category `SystemStatus - SC BG`. |
| Camera list is empty | The plugin walks the device tree for enabled cameras. The log line `Loaded N enabled camera(s)` shows how many were found. A count of zero means no enabled cameras are configured or visible to the account. |
| A user shows twice | A single client can register more than one session. Rows are grouped per user and client type, and a repeated session is shown as `(xN)`. |
| Client type is missing for a user | Older Smart Client builds may not report the server type on an endpoint. The row still shows the user, just without the type label. |

</div>
