<div class="show-title" markdown>

# FlexView

A freeform grid-based view layout designer for the Milestone XProtect Smart Client.

Design custom camera view layouts beyond the standard row/column grids. FlexView provides a 12x12 grid canvas where you can freely create, resize, move, and arrange panes, then save the result as a standard XProtect view.

## Quick Start

1. Open the **FlexView** workspace tab in the Smart Client
2. Click and drag on the grid to create panes
3. Arrange and resize panes to your desired layout
4. Enter a name, select a folder, and click **Save View**

## Features

### Grid Canvas

The 12x12 grid provides 144 possible cell positions. Panes snap to grid cells and can span any number of cells in both directions. Major gridlines every 3 cells help with alignment.

### Creating Panes

Click and drag on empty cells to create a new pane. A preview outline shows the pane dimensions while dragging. Release to place. The pane turns red if it would overlap an existing pane.

### Moving & Resizing

- **Move** - Click and drag a pane's body to reposition it
- **Resize** - Drag any edge or corner of a pane to resize it
- Cursor changes to indicate the available action (move, resize direction)
- Panes cannot overlap - invalid placements revert automatically

### Editing Existing Views

Click **Open View** to browse all Private and Shared views. Select a view to load its layout onto the grid. Camera names are displayed on each pane when available. After editing:

- Existing camera assignments are preserved for unchanged slots
- New panes become empty camera placeholders
- Removed panes are cleanly dropped from the layout

### Saving Views

Views can be saved to any Private or Shared view folder:

1. Click **Select Folder** to choose a destination
2. Enter a view name
3. Click **Save View**

The created view works like any standard XProtect view - switch to Setup mode to assign cameras to the slots.

## Controls

| Action | How |
|---|---|
| **Create pane** | Click and drag on empty cells |
| **Select pane** | Click on a pane |
| **Move pane** | Drag a pane's body |
| **Resize pane** | Drag a pane's edge or corner |
| **Delete pane** | Right-click, or select + Delete key |
| **New layout** | Click **New** to start fresh |
| **Clear** | Click **Clear** to remove all panes |

## Technical Details

FlexView maps its 12x12 grid to the Milestone SDK's 1000x1000 coordinate system. Each grid cell corresponds to approximately 83x83 SDK units. Created views are standard `ViewAndLayoutItem` objects stored in the XProtect view configuration, fully compatible with all Smart Client features.

</div>
