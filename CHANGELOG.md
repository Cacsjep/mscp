# Changelog

All notable changes to this project will be documented in this file.

## [2.7.1] - 2026-05-04
- Fix Metadata Display: `TypeLoadException` for `LiveChartsCore.CoreAxis`2` `LiveChartsCore`, `LiveChartsCore.SkiaSharpView` and `LiveChartsCore.SkiaSharpView.WPF` are now ILRepacked /internalize'd into `MetadataDisplay.dll` so the chart code resolves against its own private copy regardless of what other plugins ship. SkiaSharp / HarfBuzz remain external (large + native libs).

## [2.7.0] - 2026-05-04
- Add Web Viewer: New Smart Client view item plugin that embeds a single web page in a view item using Microsoft Edge WebView2. One URL per view item with optional title, HTTP Basic credentials (DPAPI-encrypted under the current Windows user), auto-accept of invalid TLS certificates (default on, for in-house dashboards with self-signed certs), and one-shot auto-fill of the basic-auth prompt (default on). For multi-tab / folder-tree usage with both web pages and RDP, use the existing Remote Manager plugin.

## [2.6.6] - 2026-05-04
- Add Metadata Display: **Line Chart** render type. Time-series view of any numeric data key with selectable window (60 seconds up to 24 hours), Mean / Min / Max aggregation into time buckets, optional min/max envelope band, Straight / Smooth / Step line types, configurable color, thickness, fill area and markers, optional dashed warn / critical threshold lines, and zoom and pan (mouse wheel and drag).
- Add Metadata Display: **Live archive backfill** for Line Chart. When the chart appears with a window longer than 60 seconds it is seeded from recorded metadata so the user sees real history immediately instead of waiting for the window to fill from live samples. Switching between Live and Playback or changing the window no longer wipes the visible history.
- Add Metadata Display: **Playback support** for Line Chart. The chart seeds itself at the current playback time on entry (no scrub needed), the cursor line tracks the timeline position, and large jumps trigger a fresh range scan around the new cursor. Zoom and pan are always enabled in playback and the live "Paused" badge is suppressed.
- Add Metadata Display: **In-pane time window picker** for Line Chart (top-right of the chart). Lets viewers temporarily switch the window (60 seconds, 5 / 10 / 30 minutes, 1 / 6 / 24 hours) without entering Setup mode. A **Default** entry reverts to the saved value; an asterisk on the badge label flags an active session override. Override is session-only and never modifies the saved configuration.
- Add Metadata Display: **Loading indicators** while archive backfill runs (cold-start in both Live and Playback, plus after window picker switches).
- Add Metadata Display: **Threshold enable toggle** for Number, Gauge and Line Chart, mirrored across all three render panels. When off, gauges show a single neutral track and the chart hides its threshold lines.
- Improve Metadata Display: **Number widget** value and unit now share a true text baseline so readouts like `43.99 km/h` look like a single piece of typography.
- Improve Metadata Display: **Min / Max chips** redesigned with warn-triangle (lower bound) and critical-circle (upper bound) icons, color-coded to the threshold direction.
- Improve Metadata Display: **Bar gauge** layout - value, unit, and Min/Max scale labels share one row under the bar; ticks moved above; thinner value indicator that does not hide the scale.
- Improve Metadata Display: Default **track thickness** of 6 for arc gauges and 2 for bar gauges (max 20) for a cleaner default look.
- Improve Metadata Display: Setup mode panel scales properly on small panes.
- Improve Metadata Display: Live preview now uses the same "Waiting for data..." indicator as the runtime widget instead of a key=value status line; Line Chart preview is sized 16:9 so the configured shape matches the runtime pane.
- Improve Metadata Display: Configuration window capped at 1280px wide.
- Improve Metadata Display: Diagnostic logging under the `MetadataDisplay` category for chart backfill (start, sample counts, cancellations, faults), playback seeding, and window-picker overrides.

## [2.6.0] - 2026-05-02
- Add Metadata Display: A new Smart Client plugin (View Item Plugin) to display metadata contained data in live and also recording mode.

## [2.5.0] - 2026-05-01
- Add Timeline Jump: New Smart Client toolbar plugin (partner request) for jumping the playback timeline backward or forward by a chosen increment without dragging or scrubbing. 

## [2.4.2] - 2026-04-30
- Fix RTMP Driver: Stuttering / dropped recording when the publisher's RTMP timestamps drift ahead of wall-clock.
- Add Timelapse: **Apply time window per day** option restricts frames to a daily time-of-day window across the full date range. Use for daylight-only timelapses (e.g. 2 weeks, 08:00 to 17:00 each day) or night-only timelapses with wrap-around windows (e.g. 22:00 to 06:00). Segments are clipped in memory after the server query, so the Recording Sequences card and Output Estimate update without extra round-trips.

## [2.3.1] - 2026-04-29
- Add Colored Timeline: New per-camera selectable-event ribbon plugin (successor to EdgeMotionTimeline). Includes icon picker, marker support, display-name aware rule UI with reduced table footprint, and demo video in the docs.
- Fix BarcodeReader (#84): Structured diagnostics for helper failures. Last-chance `UnhandledException` and `UnobservedTaskException` handlers, chatty `OnAssemblyResolve` (logs hits, load failures, and our-dep misses plus the full search-dir list), per-channel on-disk helper log mirrored to `C:\ProgramData\Milestone\BarcodeReader\helper-{itemId}.log` with 5 MB rotation, and typed exit codes (`BackgroundPlugin.MapExitCode` decodes 255 as `NativeCrash`, dumps the last stderr lines and the on-disk log path on death).
- Fix Installer: Management Client process kill targets the actual EXE name. Old image name `VideoOS.Platform.Administration.exe` does not match modern Milestone builds where Management Client runs as `VideoOS.Administration.exe`, so the kill silently no-opped and left the client holding plugin DLLs. Both names are now killed.
- Fix Installer (CodeQL #3, `cs/zipslip`, CWE-22 high): `ExtractZipToFolder` now resolves each entry path via `Path.GetFullPath` and validates it is contained within the resolved destination directory (with trailing separator) before extracting. Entries that escape are skipped and logged.
- Improve Dependabot: Monthly cadence with major-version bumps ignored, reducing churn from breaking upgrades.
- Bump ZXing.Net 0.16.9 to 0.16.11 (#93).
- Bump GitHub Actions: `softprops/action-gh-release` 2.6.1 to 2.6.2 (#95), `actions/upload-artifact` 7.0.0 to 7.0.1 (#66), `NuGet/setup-nuget` 3.0.0 to 3.1.0 (#65).

## [2.2.3] - 2026-04-26
- Fix FlexView: Saving an opened view no longer clears camera assignments. Smart Client rejects `ViewAndLayoutItem.Layout` mutation on an existing view; FlexView now recreates the view and re-attaches each slot's built-in content (Camera, Hotspot, Carousel, Matrix, HTML) via `InsertBuiltinViewItem`.
- Fix FlexView: A `Save` with no edits is now a no-op (was destructively recreating the view).
- Add FlexView: **Save As** button (visible only when editing an existing view) — duplicates the current view to a new name/folder and carries over camera assignments.
- Add FlexView: Search field in the view picker to filter views by name as you type. Folders auto-expand to reveal matches.
- Add FlexView: Confirmation dialog before opening an existing view explains that saving recreates the view..
- Improve FlexView: Save success and error notifications use a custom dark themed dialog matching the rest of the FlexView UI.
- Improve FlexView: Heavy diagnostic logging under the `FlexView` category in `MIPLog.txt` for load and save flows (per-slot snapshot/restore, recreate phases, success/failure summaries).
- Add RTMP Driver: Per-stream statistics block emitted to the driver log every 30 seconds.
- Improve RTMP Driver: Parse AMF `@setDataFrame onMetaData` messages so source-declared metadata (width, height, framerate, video / audio bitrate, audio codec, sample rate) ends up in the stats block. Previously the driver silently ignored these messages.
- Improve RTMP Driver: Replaced the per-150-frame and per-20-second ad-hoc log lines with the new periodic stats block.
- Add Installer: Optional **Local download page** feature for the management server.
- Improve Installer: Service stop/start is now gated on the feature action state. Smart Client-only installs no longer stop the Recording Server; admin-plugin-only installs no longer stop the Event Server; driver-only installs no longer stop the Event Server. Process kills (Smart Client, Management Client, Driver Framework) still run unconditionally because they're cheap.

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