# Changelog

All notable changes to this project will be documented in this file.

## [2.2.1] - 2026-04-26
- Fix FlexView: Saving an opened view no longer clears camera assignments. Smart Client rejects `ViewAndLayoutItem.Layout` mutation on an existing view; FlexView now recreates the view and re-attaches each slot's built-in content (Camera, Hotspot, Carousel, Matrix, HTML) via `InsertBuiltinViewItem`.
- Fix FlexView: A `Save` with no edits is now a no-op (was destructively recreating the view).
- Add FlexView: **Save As** button (visible only when editing an existing view) — duplicates the current view to a new name/folder and carries over camera assignments.
- Add FlexView: Search field in the view picker to filter views by name as you type. Folders auto-expand to reveal matches.
- Add FlexView: Confirmation dialog before opening an existing view explains that saving recreates the view (and that the internal `ObjectId` changes), so external references pinned by ID can be updated.
- Improve FlexView: Save success and error notifications use a custom dark themed dialog matching the rest of the FlexView UI.
- Improve FlexView: Heavy diagnostic logging under the `FlexView` category in `MIPLog.txt` for load and save flows (per-slot snapshot/restore, recreate phases, success/failure summaries).

## [2.2.0] - 2026-04-25
- Improve Timelapse: Continuous and Event-based modes backed by MIP SequenceDataSource (RecordingSequence)
- Improve Timelapse: Preflight card shows sequences, recorded time, and coverage with per-camera breakdown and loading spinner
- Improve Timelapse: Live segment-aware output estimate (real frame counts, not naive window ÷ interval)
- Improve Timelapse: Multi-camera union timeline with mode-dependent fallbacks (dimmed last frame + badge for Continuous, black "no event" placeholder for Event-based)
- Improve Timelapse: UTC-normalized MIP queries with lookback to catch sequences that span the start of the window
- Improve Timelapse: Redesigned idle view with card-style layout, bold-Run tooltips, and close-preview button
- Improve Timelapse: Logging to MIPLog.txt under `Timelapse` and `Timelapse.SequenceQuery` categories

## [2.1.0] - 2026-04-24
- Add: Metadata Viewer Plugin

## [2.0.2] - 2026-04-23
- Add: QR Code Barcode Scanner Plugin
- Improve: Installer regarding to OEM installations
- Improve CertWatchdog: Discovery drivers that are using https but have not default fields from milestone drivers (custom drivers)
- Improve CertWatchdog: Discovery also failovers servers in failover groups or hotstandby
- 
## [1.9.1] - 2026-04-04
- Add: Remote Manager Smart Client Plugin (replaces RDP plugins)
- Fix: Flex View: Modification of existing views was not possible #58.

## [1.8.0] - 2026-03-31
- Add: Timelapse Smart Client Plugin

## [1.7.0] - 2026-03-27
- Add: Remote Control Smart Client Plugin

## [1.6.0] - 2026-03-25
- Add: View Carousel Smart Client Plugin

## [1.5.5] - 2026-03-21

- Add: Smart Bar Col Layout 
- Add: Smart Bar settings added "Restore defaults" button to reset all settings
- Add: Smart Bar item tooltips show full name on hover for truncated entries
- Improve: Smart Bar dimensions now can configured
- Improve: Smart Bar category ordering available in both standard and column layout modes
- Improve: Smart Bar settings removed redundant Save button (framework handles save)
- Fix: Smart Bar workspace commands did not work. Now uses ChangeWorkSpaceStateCommand for Normal/Setup and dynamically enumerates all workspaces via GetWorkSpaceItems()
- Fix: Smart Bar column layout mode no longer overrides configured dimensions with hardcoded screen percentages

## [1.5.3] - 2026-03-20

- Improve: Weather Smart Client Plugin now have unit selectors and forcast options.
- Improve: Auditor Plugin now have reasons, and more time logging for playback ops.

## [1.5.1] - 2026-03-15

- Add: RTSP Driver multi-stream and audio support (ADTS header)
- Add: ILRepack to merge CommunitySDK into plugin DLLs
- Fix: Mask RTMP stream keys in logs and improve RTMP URL regex
- Improve: Installer custom action and reduced installer verbosity

## [1.5.0] - 2026-03-13

- Add: Flex View - Dynamic View Builder

## [1.4.3] - 2026-03-12

- Improve: Auditor - Introduce optional camera filtering for audit rules.

## [1.4.2] - 2026-03-10

- Add: WiX v5 MSI installer (replaces NSIS)
- Add: Dev build CI workflow (`d*` tags for development releases)
- Add: Auto-incrementing build version for local MSI builds
- Fix: Icon rendering skipped in Service environment (eliminates STA thread errors)
- Fix: Duplicate event type registration in HTTP Requests plugin
- Fix: `LogMessage.CategoryName` compatibility with older XProtect versions (pre-2025R2)
- Improve: Reduced plugin output size by filtering Milestone SDK DLLs from build output
- Remove: Unnecessary `MilestoneSystems.VideoOS.Platform.SDK` references from CommunitySDK, SnapReport, Auditor, CertWatchdog

## [1.4.1] - 2026-03-09

- Add: Command line args for Smart Bar Programms
- Change: Use 6 args logclient ctor for broader compatiblity

## [1.4.0] - 2026-03-09

- Add: HTTP Requests Plugin

## [1.3.0] - 2026-03-08

- Add: RTSP Driver
- Add: Smart Bar Plugin

## [1.1.0] - 2026-03-07

- Add: Auditor Plugin
- Add: CommunitySDK shared library (CrossMessageHandler, SystemLogBase, PluginLog)
- Add: Help page to CertWatchdog
- Update: Migrate plugins to CommunitySDK (CrossMessageHandler, PluginLog)
- Update: Improve monitor capture via GDI capture
- Update: Change icons to FontAwesome
- Update: Clean up dead code and convert RTMPDriver to SDK-style project
- Update: Docs

## [1.0.1] - 2026-03-06

- Add: RDP Port (RDP Smart Client Plugin)


## [1.0.0] - 2026-03-05

- Add: Snaphshot Reporter Plugin
- Add: RTMP Desktop Streamer Plugin
- Update: Optimize Github Workflows
- Update: Docs

Previous Changes are on Github