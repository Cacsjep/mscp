<div class="show-title" markdown>

# Smart Bar

A command palette for the Milestone XProtect Smart Client. 
Press **Space** to open a searchable launcher for cameras, views, commands, and programs - with keyboard navigation, multi-select, 
multi-window targeting, and full undo history.

## Quick Start

1. Install the plugin (via installer or manual ZIP)
2. Restart the Smart Client
3. Press **Space** anywhere in the Smart Client to open Smart Bar
4. Start typing to filter cameras, views, commands, or programs
5. Use **Arrow keys** to navigate, **Enter** to select

## Features

### Command Palette

- **Cameras** - Browse all cameras with folder breadcrumb paths. Select one to place it in the current view slot.
- **Views** - Browse all views organized by folder hierarchy. Select one to navigate to that view.
- **Commands** - Built-in application controls including fullscreen toggle, mode switching (Live/Playback/Setup), side panel, window management, configuration reload, and undo.
- **Programs** - Launch external applications directly from Smart Bar. Configured in Settings.

### Search & Filter

Type in the search box to filter across all categories. Matches against both item names and folder/group paths.

### Multi-Camera Selection

1. Navigate to a camera and press **Tab** to add it to the selection
2. Repeat for additional cameras
3. Press **Enter** to create a grid view containing all selected cameras (best-fit from 1x1, 1x2, 1x3, 2x2, 2x3, 2x4, 3x3, 3x4, 4x4, 4x5)
4. Press **Esc** to clear the selection

Smart Bar automatically creates grid layout views (1x1, 1x2, 1x3, 2x2, 2x3, 2x4, 3x3, 3x4, 4x4, 4x5) in a "SmartBar" folder under Private Views. 
The smallest layout that fits the selected cameras is chosen automatically.

### Multi-Window Support

When multiple Smart Client windows are open, window chips appear in the footer bar. Use **Ctrl+1** through **Ctrl+9** to target a specific window. All view navigations and camera placements are sent to the targeted window.

### Undo / Go Back

Smart Bar tracks view changes and camera swaps. Press the **Undo** button in the toolbar or use the "Undo / Go Back" command to step backwards through your navigation history.

The history depth is configurable (5–30 entries) in Settings.

## Keyboard Shortcuts

| Key | Action |
|---|---|
| **Space** | Open Smart Bar (from anywhere in the Smart Client) |
| **↑ / ↓** | Navigate results |
| **Enter** | Execute selected item |
| **Tab** | Toggle multi-select (cameras only) |
| **Ctrl+1–9** | Switch target window |
| **Esc** | Clear selection or close Smart Bar |

## Settings

Open Smart Client **Settings** and select **Smart Bar** to configure:

### History

Set the maximum number of undo entries (5 to 30). Changes take effect immediately.

### Programs

Manage external programs that appear in the Smart Bar launcher:

- Click **Add program** to create a new entry
- Enter a display **Name** and the **executable path**, or use the folder icon to browse
- Click the red **×** to remove an entry
- Programs with empty name or path are ignored on save
- Default: Notepad

Programs appear in Smart Bar prefixed with "Program:" and can be searched like any other item.
</div>
