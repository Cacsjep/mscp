---
title: "View Carousel Plugin for Milestone XProtect"
description: "View Carousel plugin for Milestone XProtect Smart Client — automatically cycle through entire view layouts on a timer."
---

<div class="show-title" markdown>

# View Carousel

Cycles through entire views on a timer inside a single pane. Cameras and plugin tiles render live, and you can place multiple carousels in the same view to run them in parallel.

<video controls width="100%">
  <source src="../vids/vc_usage.mp4" type="video/mp4">
</video>

## Quick Start

1. In **Setup** mode, drag **View Carousel** into a view slot
2. The **Carousel Setup** dialog opens automatically
3. Add views from the tree on the left to the selected list on the right
4. Set the default carousel time (5–300 seconds)
5. Click **Ok**, then switch to **Live** mode

## Carousel Setup

Open via the **Carousel Setup** button in the view item or the Properties panel (Setup mode). The dialog has a two-column layout: available views on the left, selected views on the right. Use **Add/Remove** to pick views, **Move up/Move down** to reorder. Each view can use the default time (5–300 sec, default 10) or a custom override.

### Controls

Hover over the plugin to reveal playback controls:

| Control | Action |
|---|---|
| **◀** | Jump to previous view |
| **⏸** | Pause / resume carousel |
| **▶** | Jump to next view |

The current view index and name are shown next to the controls.

## What renders inside the carousel

| In the original view | In the carousel |
|---|---|
| Camera | Renders live |
| Plugin tiles (Notepad, WebViewer, MetadataDisplay, Weather, Timelapse, FlexView, RemoteManager, SnapReport, LPR, Sticky Notes, Adaptive View, Blurring, and most other plugins) | Renders live |
| Empty slot | Standard empty pane |
| Hotspot, Map, Smart Map, Matrix, Alarm List, Alarm Preview, HTML page, Image, Text, Image and text, System monitor, native Carousel | Pane shows a label like *Map - not supported in carousel* |

When a pane shows the *not supported in carousel* label it means that view item is a built-in Smart Client item that can only be drawn by the main grid, not inside another pane. The rest of the view continues to cycle normally; only that one tile is replaced with the label so it's obvious instead of looking like a broken view.

## Playback & Setup Mode

The carousel is only active in **Live** mode. 

