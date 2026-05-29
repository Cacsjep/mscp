# System Status toolbar plugin (design)

A Smart Client toolbar plugin, modeled on `TimelineJump`, that shows live system
health at a glance in the workspace toolbar:

```
[ 3/19 Cameras   4 Users ]
```

- `3/19 Cameras` - 3 of 19 enabled cameras are online / working
- `4 Users` - 4 users are currently connected

Clicking the button opens a flyout window listing each camera with its status
and each connected user.

---

## 1. Architecture: one background plugin owns the state

The data, the subscriptions and the refresh logic live in a single Smart Client
`BackgroundPlugin`. The toolbar button and the flyout are thin consumers that
subscribe to it and render. This mirrors
`Admin Plugins/ColoredTimeline/Background/ColoredTimelineSmartClientBackgroundPlugin.cs`.

Why the background plugin, not the toolbar instance:

- **Lifecycle.** A `BackgroundPlugin` gets one `Init`/`Close` for the whole
  Smart Client session. `WorkSpaceToolbarPluginInstance`s are created and
  destroyed per workspace and per window (TimelineJump's `Init`/`Close` fire on
  every Live/Playback switch). State and subscriptions must not churn with the
  button.
- **Single owner.** One `MessageCommunicationManager` channel, one WhoAreOnline
  subscription, one camera-state subscription, one poll timer, one cached
  snapshot. The Live and Playback toolbars each spawn an instance; if they
  owned the channel you would register it twice.
- **Reactive.** The plugin subscribes to camera state changes and updates its
  snapshot as events arrive, and re-issues WhoAreOnline on a timer. The UI never
  queries anything.
- **Warm UI.** The flyout opens against an already-populated snapshot; the
  label is correct the moment the toolbar instance appears.

```
SystemStatusBackgroundPlugin (SmartClient, session-lived)
    owns: camera-state subscription + WhoAreOnline channel + timer + snapshot
    exposes: static Instance, StatusChanged event, current Snapshot
        |  StatusChanged { onlineCount, enabledCount, userCount, cameras[], users[] }
        v
   Toolbar instance (per workspace)        Flyout window (while open)
   updates Title                           renders camera list + user list
```

The UI talks to the plugin in-process via a static singleton plus an event
(the same way TimelineJump uses its static `ImageViewerHelper`). The toolbar
instance subscribes to `StatusChanged` in `Init`, reads the current snapshot
once for the initial label, and unsubscribes in `Close`. No queries in the UI.

---

## 2. Toolbar behavior (mirrors TimelineJump)

The toolbar piece reuses the pattern from
`Smart Client Plugins/TimelineJump/Client/TimelineJumpToolbarPlugin.cs`, but does
no data work of its own:

- A `WorkSpaceToolbarPlugin` registered for the Live and Playback workspaces in
  `Init()` (`ClientControl.LiveBuildInWorkSpaceId`,
  `ClientControl.PlaybackBuildInWorkSpaceId`), `ToolbarPluginType.Action`.
- A `WorkSpaceToolbarPluginInstance` whose:
  - `Init(Item window)` shows only on the main window
    (`window.FQID.ObjectId != Kind.Window` -> `Visible = false`, as SmartBar
    does), reads the background plugin's current snapshot for the initial
    `Title`, and subscribes to `StatusChanged`.
  - `Activate()` toggles the flyout open/closed (a second click closes it),
    exactly like `TimelineJump`'s open/close of `JumpFlyoutWindow`.
  - `Close()` unsubscribes from `StatusChanged`. It does not stop the plugin.

The button **text is dynamic**. On each `StatusChanged` the instance marshals to
the UI thread and updates `Title`:

```csharp
Title = $"{e.OnlineCount}/{e.EnabledCount} Cameras   {e.UserCount} Users";
```

(`Title` updating live on the toolbar is the one host behavior to confirm early;
if the host does not repaint on `Title` change, fall back to a custom rendered
control. `Enabled`/`Visible` are known to update live, per TimelineJump.)

---

## 3. Data sources (owned by the background plugin)

Two independent services, both owned by the background plugin, feed the cached
snapshot (see section 5 for how they are refreshed).

### 3a. Camera status (the `3/19`)

Two steps: enumerate enabled cameras, then ask each camera's recording server
for live status.

**Enumerate enabled cameras:**

```csharp
var cameras = Configuration.Instance
    .GetItemsByKind(Kind.Camera, ServerId.LocalManagementServer())
    .Where(c => c.Enabled)   // Item.Enabled - skip disabled devices
    .ToList();               // count -> the "19"
```

`GetItemsByKind` hides most disabled items already, but filtering on
`Item.Enabled` is the reliable way to guarantee "enabled only."

**Query live status per recording server** (`RecorderStatusService2`, port 7563,
grouped per recorder):

```csharp
var token  = LoginSettingsCache.GetLoginSettings(recorder.ManagementUri).Token;
var client = BuildRecorderStatusService2Client(recorder); // http://<recorder>:7563/RecorderStatusService2/
var status = client.GetCurrentDeviceStatus(token, deviceIds);

foreach (var s in status.CameraDeviceStatusArray)
{
    bool online = s.Started && !s.Error;   // counts toward the "3"
    // also available: s.Motion, s.Recording, s.DbMoving, s.DbRepairing
}
```

Online / working = `Started == true && Error == false`. Per camera we keep
`{ name, online, recording, motion }` for the flyout list.

Notes:
- "Online" is not "has footage". `SequenceDataSource` (RecordingSequence) tells
  you a camera has recordings, which is a different question and is the check
  the AutoExporter helper uses. Do not use it for status here.
- The querying identity needs status/view rights or the recorder returns
  nothing (reads as offline).
- Reference samples: Status Demo Console, System Status Client Console.

### 3b. Connected users (the `4 Users`)

Use the built-in **WhoAreOnline** presence query over the Event Server message
channel (the same mechanism the Chat sample uses), then clean the result up.

```csharp
MessageCommunicationManager.Start(EnvironmentManager.Instance.MasterSite.ServerId);
var mc = MessageCommunicationManager.Get(EnvironmentManager.Instance.MasterSite.ServerId);

mc.RegisterCommunicationFilter(
    WhoAreOnlineResponseHandler,
    new CommunicationIdFilter(MessageCommunication.WhoAreOnlineResponse));

mc.TransmitMessage(new Message(MessageCommunication.WhoAreOnlineRequest), null, null, null);

private object WhoAreOnlineResponseHandler(Message message, FQID dest, FQID source)
{
    var raw = message.Data as List<EndPointIdentityData>;
    // each entry: IdentityName ("Administrator (10.0.0.5)") + FQID
    return null;
}
```

The raw list is **noisy** and must be post-processed before it becomes the
"4 Users" count. Milestone states it is "not meant as a perfect user session
monitoring solution," and the list includes:

- **Milestone services** (Event Server, Log Server, other service endpoints)
- **Duplicate entries** for the same user / endpoint

So the service does, in order:

1. **Drop service endpoints** - filter `IdentityName` against a known-service
   name list and/or exclude server-local addresses (`(0.0.0.0)`, the management
   server's own IP).
2. **De-duplicate** - group by `IdentityName` (or user + IP) so a user counts
   once. That count is the "4"; the deduped entries are the flyout user list.

Each user row keeps `{ displayName, ip }` parsed from `IdentityName`.

Fallback / complement: for an attributable login/logout history (not live), the
audit log via `VideoOS.Platform.Log.LogClient` (type Audit) gives clean
`User` + `User location` + time, with windowed reads and audit-read rights. Not
needed for the live counter, but useful if the flyout later wants "last login".

---

## 4. The flyout window

A WPF window like `JumpFlyoutWindow`, opened from `Activate()` and closed on a
second click or when the workspace mode changes (subscribe to
`MessageId.System.ModeChangedIndication`, as TimelineJump does). Two sections:

- **Cameras** - one row per enabled camera: name, status dot (green online /
  red offline), optional recording + motion indicators. Header shows
  `Online 3 / 19`.
- **Users** - one row per connected user (after filter + dedupe): display name
  and IP. Header shows `Connected 4`.

On open it renders the background plugin's current snapshot, then subscribes to
`StatusChanged` for live updates while it is open and unsubscribes on close. It
runs no queries of its own.

---

## 5. Refresh strategy (inside the background plugin)

The background plugin owns the refresh logic. Prefer subscription over polling
where the SDK allows it:

- **Camera status** - subscribe to camera state changes (event/state) so the
  snapshot updates as cameras go up or down, with a periodic full
  `RecorderStatusService2` sweep as a reconcile/backstop.
- **Connected users** - keep the `MessageCommunicationManager` channel started
  once and re-issue `WhoAreOnlineRequest` on a timer (there is no clean push for
  presence). The response handler updates the user snapshot.
- A single timer (suggest 15 to 30 s; configurable via a settings panel like
  `TimelineJumpSettingsPanel`) drives the periodic work. Network I/O runs off
  the UI thread.
- After each change the plugin updates its cached `Snapshot` and raises
  `StatusChanged { onlineCount, enabledCount, userCount, cameras[], users[] }`.
  Consumers marshal to the Dispatcher before touching `Title` or the flyout.
- Channel and subscriptions start in the plugin's `Init()` and stop in `Close()`
  (session-lived). Never start/stop `MessageCommunicationManager` per refresh,
  and never tie it to the toolbar instance lifecycle.

---

## 6. Suggested file layout

Background plugin owns the data; the Client folder is presentation only. The
scaffolding files follow `new_plugin.md` (Smart Client section); the plugin
surface follows TimelineJump (toolbar + flyout), not the ViewItem template.

```
Smart Client Plugins/SystemStatus/
  SystemStatus.csproj                    net48, UseWPF, MilestoneSystems.VideoOS.Platform *-*, deploy flags
  plugin.def                             <load env="SmartClient"/>
  SystemStatusDefinition.cs              PluginDefinition; SmartClient-only; registers BG plugin + toolbar
  Resources/
    PluginIcon.png                       <Resource> (toolbar/flyout glyphs can use FontAwesome via code)
  Properties/
    launchSettings.json                  profile name "Smart Client"
  Background/
    SystemStatusBackgroundPlugin.cs      BackgroundPlugin; owns channel + subscriptions + timer + snapshot;
                                         static Instance, StatusChanged event (mirrors ColoredTimeline BG)
  Services/
    CameraStatusService.cs               enumerate enabled cameras + RecorderStatusService2 -> online/total + rows
    ConnectedUsersService.cs             WhoAreOnline request/response + filter services + dedupe -> rows
    RecorderStatusClientFactory.cs       build the per-recorder RecorderStatusService2 proxy with token
  Client/
    SystemStatusToolbarPlugin.cs         WorkSpaceToolbarPlugin + Instance; subscribes to StatusChanged, sets Title
    StatusFlyoutWindow.xaml(.cs)         camera list + user list; renders snapshot, subscribes while open
    SystemStatusSettingsPanel.cs         refresh interval, service-name filter (optional)
```

`SystemStatusDefinition` registers both the `BackgroundPlugin` (via
`BackgroundPlugins`) and the `WorkSpaceToolbarPlugin` (via
`WorkSpaceToolbarPlugins`), SmartClient-only, as TimelineJump and ColoredTimeline
do for their respective collections. Settings use `SettingsPanelPlugins` (like
TimelineJump), not a ViewItem `PropertiesWpfUserControl`.

---

## 7. Project conventions (from new_plugin.md)

This is a Smart Client plugin, but a toolbar one, so it diverges from the guide's
ViewItem template in these ways and matches it everywhere else.

**GUIDs needed** (generate with `[guid]::NewGuid()`):

| GUID | Purpose | Used in |
| --- | --- | --- |
| 1 | `PluginId` | `SystemStatusDefinition` |
| 2 | `BackgroundPluginId` | `SystemStatusDefinition`, BG plugin `Id` |
| 3 | `ToolbarPluginId` | `WorkSpaceToolbarPlugin.Id` (as TimelineJump) |
| 4 | Project GUID | `MSCPlugins.sln` |

No `ViewItemKind` / `ViewItemPlugin.Id` GUIDs - there is no view item.

**Scaffolding to match the guide:**

- `.csproj`: `net48`, `UseWPF=true`, `OutputType=Library`,
  `PackageReference MilestoneSystems.VideoOS.Platform Version="*-*"`, plus
  `PluginName`, `StopSmartClient`, `LaunchSmartClient` deploy flags. Reference
  `CommunitySDK` for `PluginLog` and `PluginIcon` (TimelineJump uses both).
- `plugin.def` with `<load env="SmartClient"/>`, `CopyToOutputDirectory=Always`.
- `launchSettings.json` profile named `"Smart Client"` (not the plugin name),
  `executablePath` to `Client.exe`.
- `SystemStatusDefinition.Init()` guards
  `EnvironmentType == EnvironmentType.SmartClient` (as TimelineJump does) before
  registering anything.
- Icon via FontAwesome `PluginIcon.RenderIconSource(...)` in the definition
  (e.g. a monitor/heartbeat glyph), with the built-in fallback TimelineJump uses.
  Gotcha from the guide: FontAwesome is **not** a XAML `StaticResource`. In the
  flyout XAML use Unicode glyphs or WPF `Path`/`Geometry`, not a FontAwesome
  font family.
- The BackgroundPlugin follows the guide's BG conventions: static `Instance`,
  `volatile bool _closing`, a lock around the snapshot, and a
  `ConfigurationChangedIndication` receiver if a settings change should reload
  (e.g. refresh interval or service-name filter).

**Registration steps (the guide's "Common Steps"):**

- Add the project to `MSCPlugins.sln` (project entry + platform config +
  `NestedProjects` under the Smart Client Plugins solution folder
  `{A0000001-...01}`).
- Add an entry to `plugins.json` with `"category": "SmartClient"`.
- Update `README.md` (plugins table + manual-install paths) and
  `docs/plugins/smart-client/system-status.md`.

---

## Quick reference

| Goal | API / class | Notes |
| --- | --- | --- |
| Enabled cameras (the total) | `Configuration.Instance.GetItemsByKind(Kind.Camera)`, `Item.Enabled` | Filter on `Enabled` |
| Camera online now (the online count) | `RecorderStatusService2.GetCurrentDeviceStatus` | `Started && !Error`; per recorder, port 7563 |
| Camera has footage | `SequenceDataSource` (RecordingSequence) | Different from status; do not use here |
| Connected users (live) | `MessageCommunication` WhoAreOnlineRequest / Response, `EndPointIdentityData` | Includes services + duplicates; filter and dedupe |
| Login / logout history | `VideoOS.Platform.Log.LogClient` (Audit) | Optional; windowed reads; needs audit-read rights |
| State + subscriptions owner | `BackgroundPlugin` (SmartClient) | Session-lived; pattern from `ColoredTimeline` BG |
| Toolbar button + flyout | `WorkSpaceToolbarPlugin(Instance)`, flyout `Window` | Thin consumers of `StatusChanged`; pattern from `TimelineJump` |

### Sources

- RecorderStatusService and camera status, forum get-camera-status threads 13367
  and 13481
- MIP message communication and WhoAreOnline:
  https://doc.developer.milestonesys.com/mipsdk/gettingstarted/intro_mip_messaging.html
- WhoAreOnline services / duplicates caveat (MilestonePSTools Get-WhoIsOnline):
  https://www.milestonepstools.com/commands/en-US/Get-WhoIsOnline/
- Read Audit / System / Rule logs from the MIP SDK (LogClient) and the LogRead
  sample
