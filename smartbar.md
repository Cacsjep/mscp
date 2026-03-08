# SmartBar Plugin - Knowledge Base

## Overview
SmartBar is a command palette plugin for Milestone XProtect Smart Client, opened via Space bar. It provides quick access to views, cameras, commands, programs, and undo history.

## Project Structure
- `Smart Client Plugins/SmartBar/` — plugin root
- `Client/SmartBarWindow.xaml(.cs)` — Launcher UI (command palette popup)
- `Client/SmartBarHistory.cs` — Undo history tracking (views + cameras)
- `Client/SmartBarSettingsPanelControl.xaml(.cs)` — Settings panel (max history, programs list)
- `Client/SmartBarDefinition.cs` — Plugin definition, logging via `PluginLog`
- Build target: .NET Framework 4.8

## Build & Deploy
- Build command: `dotnet build "Smart Client Plugins/SmartBar/SmartBar.csproj"`
- Output: `Smart Client Plugins/SmartBar/bin/Debug/net48/`
- Files are copied to Smart Client plugin folder automatically (323 files)
- Log file: `c:\ProgramData\Milestone\XProtect Smart Client\MIPLog.txt`
- Clear log after each build: `> "c:/ProgramData/Milestone/XProtect Smart Client/MIPLog.txt"`

## Launcher (SmartBarWindow)
- `ItemCategory` enum: `Camera, View, Command, Program, Undo` — order determines display order
- Undo items appear at the bottom of the list
- Each undo entry shows "Undo N: [W1] description" format
- Selecting undo entry N calls `SmartBarHistory.GoBackN(N)` to undo N items at once
- `LoadUndoHistory()` populates undo items from `SmartBarHistory.GetHistoryDescriptions()`

## History Tracking (SmartBarHistory)

### Architecture
- Static class, installed/uninstalled with plugin lifecycle
- Tracks two types: `HistoryType.View` and `HistoryType.Camera`
- Uses `LinkedList<HistoryEntry>` with configurable max size (default 20)
- Per-window tracking: each viewer gets a `ViewerInfo` with `SlotIndex` + `WindowId`
- Window numbers assigned via `Configuration.Instance.GetItemsByKind(Kind.Window)`

### HistoryEntry Fields
- `Type` — View or Camera
- `ViewFQID` — the view's FQID (for View entries)
- `CameraFQID` — previous camera in slot (to restore on undo, null = was empty)
- `NewCameraFQID` — new camera placed (to re-apply after view restore)
- `WindowId` — which window (Guid)
- `SlotIndex` — viewer slot index within the window
- `Description` — human-readable text for launcher display

### Camera Swap Detection
- Detects camera changes via close/create cycle within 500ms (`CloseCreateWindowMs`)
- `OnViewerClose` records `_pendingClosedCamera`, `_pendingClosedSlot`, `_pendingClosedWindowId`
- `OnNewImageViewer` checks if a create follows a close within the window — if so, it's a camera swap
- `_consecutiveCloses` tracks batch closes (view switch = many closes, swap = exactly 1)
- Empty-to-camera placements ARE tracked (CameraFQID = null means slot was empty)

### View Change Detection
- Listens to `MessageId.SmartClient.SelectedViewChangedIndication`
- `_viewBatchOccurred` flag prevents window focus events from polluting history
- Only records a view change when a viewer batch (close/create cycle) preceded it
- Deduplicates: won't add same view on same window consecutively

### GoBack (Single Undo)
- Pops last entry from history
- Sets `_goBackTime` for 1000ms suppression window (`GoBackSuppressMs`)
- **View undo**: Finds previous view for the SAME window, sends `SetViewInWindow`
  - After view restore, scans history for camera entries between current position and previous view boundary
  - Re-applies most recent `NewCameraFQID` per slot via `SetCameraInViewCommand`
  - **Removes those camera entries from history** so GoBackN doesn't undo them again
- **Camera undo**: Sends `SetCameraInViewCommand` with the old `CameraFQID` (null = clear slot)
- `_suppressNextViewChange` prevents the SDK's view change notification from being recorded as new history

### GoBackN (Multi-Undo)
- First GoBack is immediate, remaining are paced via `DispatcherTimer` at 20ms intervals
- Timer stops when count reached or history empty
- Camera entries consumed by view restore are removed from history, so timer skips them correctly

## SDK Quirks & Known Issues

### SetCameraInViewCommand Index Mismatch
- On views where cell count != viewer count (e.g., 2x2 view = 4 cells but only 3 viewers), `SetCameraInViewCommand.Index` targets the wrong slot
- Index=2 hits viewer at slot 1 (off by 1)
- Works correctly when cell count == viewer count (e.g., 1x3 view = 3 cells, 3 viewers)
- Root cause unknown — possibly SDK cell-to-viewer mapping mismatch on partially-filled views

### View Switch Behavior
- XProtect does NOT persist temporary camera assignments when switching views
- Cameras placed by user are lost on view switch — must be re-applied manually after undo

### GoBack Suppression
- GoBack sets `_goBackTime` to prevent close/create events during undo from being recorded as new history
- 1000ms window covers all cascading close/create cycles
- Both camera swaps and view batch detection check `goBackActive`

### Window Detection
- New windows detected by comparing `Configuration.Instance.GetItemsByKind(Kind.Window).Count` against `_knownWindowCount`
- `_currentBatchWindowId` tracks which window is currently receiving new viewers

## Settings
- Max undo history: configurable via ComboBox in settings panel
- Max recent items: configurable (5–20)
- Category toggles: ShowOutputs, ShowEvents, ShowCommands, ShowRecent (all default true)
- ShowRecent=false also prevents collection of recent items in SmartBarHistory
- Programs: list of name + executable path, launchable from SmartBar
- Settings stored in XML at `%ProgramData%\Milestone\SmartBar\config.xml`

## Dependencies
- `CommunitySDK` — community wrapper for MIP SDK
- `VideoOS.Platform` — Milestone MIP SDK
- `FontAwesome5` — icons (fa5 namespace in XAML)
