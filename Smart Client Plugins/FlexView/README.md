# FlexView

A freeform grid-based view layout designer for the Milestone XProtect Smart Client.

Design custom camera view layouts beyond the standard row/column grids. FlexView provides a 60x60 grid canvas where you can freely create, resize, move, and arrange panes, then save the result as a standard XProtect view.

## Features

- **60x60 Grid Canvas** - Design layouts on a high-resolution 16:9 canvas with snap-to-cell precision. The 60-cell resolution divides evenly by 2, 3, 4, 5, and 6, ensuring pixel-perfect import of all standard Milestone view layouts.
- **Drag to Create** - Click and drag on empty cells to create new panes (minimum 2x2 cells)
- **Move & Resize** - Drag panes to reposition, drag the bottom-right corner handle to resize
- **Edit Existing Views** - Open any existing view, modify its layout, and save back with camera assignments preserved
- **Save Anywhere** - Save designed views to any Private or Shared view folder via a save dialog
- **Camera Preservation** - When editing existing views, camera assignments are maintained for unchanged slots
- **Overlap Prevention** - Visual feedback prevents overlapping panes

## Quick Start

1. Open the **FlexView** workspace tab in the Smart Client
2. Click and drag on the grid to create panes (minimum 2x2 cells)
3. Arrange and resize panes to your desired layout
4. Click **Save** to open the save dialog, enter a name, select a folder, and confirm

## Controls

| Action | How |
|---|---|
| **Create pane** | Click and drag on empty cells (min 2x2) |
| **Move pane** | Drag a pane's body |
| **Resize pane** | Drag the bottom-right corner handle |
| **Delete pane** | Right-click a pane, or select + Delete key |
| **New layout** | Click **New** to start fresh |

## Editing Existing Views

1. Click **Open View**
2. Browse and select an existing view from the tree
3. The view's layout loads onto the grid with camera names displayed in blue
4. Modify the layout (move, resize, add, or remove panes)
5. Click **Save** to update the view in place

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
