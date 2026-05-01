---
title: "Timeline Jump Plugin for Milestone XProtect"
description: "Timeline Jump plugin for Milestone XProtect Smart Client - jump the playback timeline backward or forward by a fixed increment (seconds, minutes, hours, days) without dragging the timeline."
---

<div class="show-title" markdown>

# Timeline Jump

Adds a **Jump** button to the toolbar that opens a small floating panel with one-click increments (`10s`, `30s`, `1m`, `10m`) and a custom Value/Unit picker so you can jump the playback timeline backward or forward by any amount of time without scrubbing.

<video controls width="100%">
  <source src="../vids/timejump_usage.mp4" type="video/mp4">
</video>

## Quick Start

1. Install the plugin and open Smart Client.
2. Switch to the **Playback** workspace.
3. Click the **Jump** button in the top toolbar.
4. Click any of the **BACKWARD** / **FORWARD** chips, or pick a value + unit and press **Back** / **Forward**.
5. The timeline jumps by exactly that amount. The flyout stays open so you can keep stepping; drag the header to move it, press **Esc** or click the **X** to close.

## How it routes the jump

The plugin picks the right timeline target automatically:

| Situation | Jump target |
|---|---|
| Playback workspace, no tile in independent playback | **Master timeline** - all tiles bound to the master move together. |
| Playback or Live workspace, **selected tile is in independent playback** | **Only that tile's timeline** moves. |
| Live workspace, no tile in independent playback | Button is **disabled** - there is nothing meaningful to jump on the live edge. |

The button's enabled state follows the workspace and the per-tile independent-playback state in real time, so it lights up as soon as you turn on independent playback for a camera and goes dark again when you turn it off.

## Snap to nearest recording

If your jump lands in a gap with no recorded data, the plugin snaps to the closest recording in the direction of the jump so the operator visibly moves to the next event instead of freezing on the last decoded frame:

| Target falls... | Result |
|---|---|
| **Inside** a recorded sequence | Jump to the exact target time. |
| **In a gap, jumping forward** | Jump to the **start** of the next recorded sequence on that camera. |
| **In a gap, jumping backward** | Jump to the **end** of the previous recorded sequence on that camera. |
| Camera has no recording within 30 days either side | Jump to the raw target (the tile may show the last decoded frame). |

In independent playback mode the snap is always applied to the tile being jumped. In master Playback mode the snap targets the selected tile's coverage when there is one; with no tile selected the raw target is used so all tiles receive the same `Goto` time.

## Quick chips

Pre-set increments, one click each:

| Direction | Steps |
|---|---|
| **BACKWARD** | `-10m`, `-1m`, `-30s`, `-10s` |
| **FORWARD** | `+10s`, `+30s`, `+1m`, `+10m` |

## Custom jump

For anything outside the chip set:

| Field | Values |
|---|---|
| **Value** | `1`, `2`, `5`, `10`, `15`, `30`, `45` |
| **Unit** | `Seconds`, `Minutes`, `Hours`, `Days` |

Then press **Back** to subtract, **Forward** to add. Example: `Value = 15`, `Unit = Minutes`, `Forward` jumps the timeline forward 15 minutes.

## Where it works

| Workspace | Behavior |
|---|---|
| **Playback** | Jump button always available. Master timeline by default; per-tile if that tile is in independent playback. |
| **Live** | Jump button visible but disabled until a tile is put into independent playback. Once independent playback is active on any tile the button enables; clicking jumps the selected independent tile. |

## Troubleshooting

| Problem | Fix |
|---|---|
| Jump button missing from the toolbar | Plugin DLLs must be in `MIPPlugins\TimelineJump\`. Unblock the ZIP if you copied it manually. Restart Smart Client. |
| Jump button is grayed in Live | Expected. Enable independent playback on a tile (the per-tile clock icon on the camera) and the button activates. |
| Quick chip click does nothing visible | The camera has no recordings within 30 days of the target. Check `MIPLog.txt` under category `TimelineJump` to see what target time was sent. |
| Flyout opens off-screen on multi-monitor | The flyout positions itself near the cursor and clamps to the working area; if it gets stuck, drag it from the title-bar area. |

</div>
