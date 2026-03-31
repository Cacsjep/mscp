---
title: "FlexView Plugin for Milestone XProtect"
description: "FlexView plugin for Milestone XProtect Smart Client — design custom view layouts beyond standard templates with a free-form canvas."
---

<div class="show-title" markdown>

# FlexView

Design custom view layouts beyond the standard view templates. FlexView provides a canvas where you can freely create, resize, move, and arrange panes, then save the result as a standard XProtect view.

## Quick Start

1. Open the **FlexView** workspace tab in the Smart Client
2. Click and drag on the grid to create panes (minimum 2x2 cells)
3. Arrange and resize panes to your desired layout
4. Click **Save** to open the save dialog, enter a name, select a folder, and confirm

<video controls width="100%">
  <source src="../vids/flex_usage.mp4" type="video/mp4">
</video>

!!! info 
    Install it only where you need it. There is no security control to let users show or hide this plugin, so install it only on Smart Clients where you want to give users the ability to do this.

## Creating Panes

Click and drag on empty cells to create a new pane. A preview outline shows the pane dimensions while dragging. Release to place. The pane turns red if it would overlap an existing pane. Minimum pane size is 2x2 cells.

## Moving & Resizing

- **Move** - Click and drag a pane's body to reposition it
- **Resize** - Drag the bottom-right corner handle of a pane to resize it
- **Hover** - Panes highlight when hovered, showing the resize handle
- Cursor changes to indicate the available action (move, resize direction)
- Panes cannot overlap - invalid placements revert automatically

## Editing Existing Views

Click **Open View** to browse all Private and Shared views. Select a view to load its layout onto the grid. The view name is displayed in the toolbar. Camera names are shown in blue on each pane when available. After editing:

- Existing camera assignments are preserved for unchanged slots
- New panes become empty slots
- Removed panes are cleanly dropped from the layout

## Saving Views

Click **Save** in the toolbar to open the save dialog:

1. Enter a view name
2. Click **Browse** to select a destination folder
3. Click **Save** to confirm

The created view works like any standard XProtect view - assign cameras to the slots in the normal view builder.

## Controls

| Action | How |
|---|---|
| **Create pane** | Click and drag on empty cells (min 2x2) |
| **Move pane** | Drag a pane's body |
| **Resize pane** | Drag the bottom-right corner handle |
| **Delete pane** | Right-click a pane, or select + Delete key |
| **New layout** | Click **New** to start fresh |

</div>
