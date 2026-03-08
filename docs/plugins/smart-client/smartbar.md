<div class="show-title" markdown>

# Smart Bar

A command palette for the Milestone XProtect Smart Client.
Press the invoke key (default **Space**) to open a searchable launcher for cameras, views, commands, and programs - with keyboard navigation, multi-select,
multi-window targeting, recent items, and full undo history.

## Quick Start

1. Press **Space** anywhere in the Smart Client to open Smart Bar
4. Start typing to filter cameras, views, commands, or programs
5. Use **Arrow keys** to navigate, **Enter** to select

## Features

### Command Palette

- **Recent** - Recently used cameras and views appear at the top for quick re-access. Configurable limit (5–20 items).
- **Cameras** - Browse all cameras with folder breadcrumb paths. Select one to place it in the current view slot.
- **Views** - Browse all views organized by folder hierarchy. Select one to navigate to that view.
- **Outputs** - Activate or deactivate hardware outputs (gates, sirens, door locks, etc.) directly from Smart Bar. Each output appears twice: `Output: Name Activate` and `Output: Name Deactivate`. Requires Corporate/Expert edition. Can be disabled in Settings.
- **Events** - Trigger user-defined events configured in XProtect Management Client. Each event appears as `Event: Name`. Can be disabled in Settings.
- **Commands** - Built-in application controls including fullscreen toggle, mode switching (Live/Playback/Setup), side panel, window management, configuration reload, and undo.
- **Programs** - Launch external applications directly from Smart Bar. Configured in Settings.

### Search & Filter

Type in the search box to filter across all categories. Matches against both item names and folder/group paths.

### Multi-Camera Selection

1. Navigate to a camera and press **Tab** to add it to the selection
2. Repeat for additional cameras
3. Press **Enter** to create a grid view containing all selected cameras (best-fit from 1x1, 1x2, 1x3, 2x2, 2x3, 2x4, 3x3, 3x4, 4x4, 4x5)
4. Press **Esc** to clear the selection

Smart Bar automatically creates grid layout views (1x1 through 4x5) in a "SmartBar" folder under Private Views.
The smallest layout that fits the selected cameras is chosen automatically.

### Multi-Window Support

When multiple Smart Client windows are open, window chips appear in the footer bar. Use **Ctrl+1** through **Ctrl+9** to target a specific window. All view navigations and camera placements are sent to the targeted window.

### Undo / Go Back

Smart Bar tracks view changes and camera swaps across all windows. Use the "Undo / Go Back" command or select a specific step from the **Undo History** list to jump back multiple steps at once.

- **Camera undo** - Restores the previous camera in the same slot when a camera is swapped.
- **View undo** - Restores the previous view on the same window, including any cameras that were placed in the destroyed view (view snapshot restore).

The history depth is configurable (5–30 entries) in Settings. History entries are labeled with their target window (e.g. `[W1]`, `[W2]`).

### Recent Items

The top section of Smart Bar shows recently used cameras and views, ordered by most recent first. This provides quick one-keystroke access to items you use frequently without needing to search.

The number of recent items shown is configurable (5–20) in Settings.

## Keyboard Shortcuts

| Key | Action |
|---|---|
| **Space** (default, configurable) | Open Smart Bar |
| **↑ / ↓** | Navigate results |
| **Enter** | Execute selected item |
| **Tab** | Toggle multi-select (cameras only) |
| **Ctrl+1–9** | Switch target window |
| **Esc** | Clear selection or close Smart Bar |

## Settings

Open Smart Client **Settings** and select **Smart Bar** to configure:

### General

**Invoke key** - The keyboard shortcut to open Smart Bar. Click the key recorder, then press the desired key or key combination (e.g. `Space`, `F2`, `Ctrl+F`). Supports modifier combinations with Ctrl, Alt, and Shift.

Reserved keys that cannot be used as the invoke key (without a modifier): letters, digits, Escape, Enter, arrows, Tab, Backspace, and Delete - these are used by the Smart Bar window itself for search and navigation. Adding a modifier (e.g. `Ctrl+F`) makes any key valid.

### History

- **Max undo history entries** (5–30) - Number of view and camera changes to remember for undo.
- **Max recent items** (5–20) - Number of recently used cameras and views shown at the top of Smart Bar.

### Categories

Toggle which item categories appear in the Smart Bar launcher:

- **Show hardware outputs** - List all hardware outputs with activate/deactivate commands. Enabled by default.
- **Show user-defined events** - List all user-defined events with trigger commands. Enabled by default.

### Programs

Manage external programs that appear in the Smart Bar launcher:

- Click **Add program** to create a new entry
- Enter a display **Name** and the **executable path**, or use the folder icon to browse
- Click the red **×** to remove an entry
- Paths are validated - must be a valid file path (e.g. `C:\Program Files\app.exe`) or a bare executable name (e.g. `notepad.exe`)
- The **Save** button is disabled until all entries are valid, with a hint shown below explaining the issue
- Default: Notepad

Programs appear in Smart Bar prefixed with "Program:" and can be searched like any other item.

## Configuration File

Settings are stored in XML at:

```
C:\ProgramData\Milestone\SmartBar\config.xml
```

| Element | Type | Default | Description |
|---|---|---|---|
| `MaxHistory` | int | 20 | Maximum undo history entries |
| `MaxRecent` | int | 10 | Maximum recent items shown |
| `InvokeKey` | Key enum | Space | Keyboard key to open Smart Bar |
| `InvokeModifiers` | ModifierKeys enum | None | Modifier keys (Ctrl, Alt, Shift) for invoke |
| `ShowOutputs` | bool | true | Show hardware output commands |
| `ShowEvents` | bool | true | Show user-defined event commands |
| `Programs` | list | Notepad | External programs to show in launcher |

</div>
