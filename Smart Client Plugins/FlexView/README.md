# FlexView

A freeform grid-based view layout designer for the Milestone XProtect Smart Client.

Create custom camera view layouts beyond the standard row/column grids. FlexView provides a 12x12 grid canvas where you can draw, resize, move, and arrange panes freely, then save the result as a standard XProtect view that works with any camera.

## Features

- **12x12 Grid Canvas** - Design layouts on a 12x12 grid with snap-to-cell precision
- **Drag to Create** - Click and drag on empty cells to create new panes of any size
- **Move & Resize** - Drag panes to reposition, drag edges/corners to resize
- **Edit Existing Views** - Open any existing view, modify its layout, and save back with camera assignments preserved
- **Save Anywhere** - Save designed views to any Private or Shared view folder
- **Camera Preservation** - When editing existing views, camera assignments are maintained for unchanged slots
- **Overlap Prevention** - Visual feedback prevents overlapping panes

## Quick Start

1. Open the **FlexView** workspace tab in the Smart Client
2. Click and drag on the grid to create panes
3. Resize and arrange panes as needed
4. Enter a view name and select a destination folder
5. Click **Save View**
6. Switch to Live/Playback mode and navigate to your new view to assign cameras

## Controls

| Action | How |
|---|---|
| Create pane | Click and drag on empty grid cells |
| Select pane | Click on an existing pane |
| Move pane | Drag a selected pane's body |
| Resize pane | Drag a pane's edge or corner |
| Delete pane | Right-click a pane, or select and press Delete |
| Clear all | Click the Clear button |

## Editing Existing Views

1. Click **Open View**
2. Browse and select an existing view from the tree
3. The view's layout loads onto the grid with camera names displayed
4. Modify the layout (move, resize, add, or remove panes)
5. Click **Save View** to update the view in place

Camera assignments are preserved for slots that existed before editing. New slots are created as empty camera placeholders. If slots are removed, those cameras are no longer in the view.

## Installation

### MSI Installer

Select **FlexView** during MSI installer feature selection.

### Manual

Copy the `FlexView` folder to:

```
C:\Program Files\Milestone\MIPPlugins\FlexView\
```

Restart the Smart Client.
