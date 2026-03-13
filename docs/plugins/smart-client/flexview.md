<div class="show-title" markdown>

# FlexView

A freeform grid-based view layout designer for the Milestone XProtect Smart Client.

Design custom camera view layouts beyond the standard row/column grids. FlexView provides a 60x60 grid canvas where you can freely create, resize, move, and arrange panes, then save the result as a standard XProtect view.

## Quick Start

1. Open the **FlexView** workspace tab in the Smart Client
2. Click and drag on the grid to create panes (minimum 2x2 cells)
3. Arrange and resize panes to your desired layout
4. Click **Save** to open the save dialog, enter a name, select a folder, and confirm

## Features

### Grid Canvas

The 60x60 grid provides 3600 possible cell positions on a 16:9 canvas. Panes snap to grid cells and can span any number of cells in both directions. The 60x60 resolution ensures that all standard Milestone view layouts (2x2, 3x3, 4x4, 5x5, 6x6) import with perfectly equal pane sizes.

### Creating Panes

Click and drag on empty cells to create a new pane. A preview outline shows the pane dimensions while dragging. Release to place. The pane turns red if it would overlap an existing pane. Minimum pane size is 2x2 cells.

### Moving & Resizing

- **Move** - Click and drag a pane's body to reposition it
- **Resize** - Drag the bottom-right corner handle of a pane to resize it
- **Hover** - Panes highlight when hovered, showing the resize handle
- Cursor changes to indicate the available action (move, resize direction)
- Panes cannot overlap - invalid placements revert automatically

### Editing Existing Views

Click **Open View** to browse all Private and Shared views. Select a view to load its layout onto the grid. The view name is displayed in the toolbar. Camera names are shown in blue on each pane when available. After editing:

- Existing camera assignments are preserved for unchanged slots
- New panes become empty slots
- Removed panes are cleanly dropped from the layout

### Saving Views

Click **Save** in the toolbar to open the save dialog:

1. Enter a view name
2. Click **Browse** to select a destination folder
3. Click **Save** to confirm

When editing an existing view, saving updates it in place without a dialog.

The created view works like any standard XProtect view - assign cameras to the slots in the normal view builder.

## Controls

| Action | How |
|---|---|
| **Create pane** | Click and drag on empty cells (min 2x2) |
| **Move pane** | Drag a pane's body |
| **Resize pane** | Drag the bottom-right corner handle |
| **Delete pane** | Right-click a pane, or select + Delete key |
| **New layout** | Click **New** to start fresh |

## Technical Details

FlexView maps its 60x60 grid to the Milestone SDK's 1000x1000 coordinate system. The 60-cell resolution divides evenly by 2, 3, 4, 5, and 6, ensuring pixel-perfect import of all standard Milestone view layouts. Edge-based coordinate conversion eliminates sub-pixel gaps between adjacent panes. Created views are standard `ViewAndLayoutItem` objects stored in the XProtect view configuration, fully compatible with all Smart Client features.

The ViewItemPlugin uses `HideSetupItem = true` to prevent it from appearing in the normal View Builder while remaining available for the workspace.

</div>
