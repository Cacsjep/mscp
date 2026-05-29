# Camera Status and Connected Users (MIP SDK)

Overview of the techniques for two related questions in an XProtect plugin:

1. Which enabled cameras are online / working right now?
2. Who is currently connected / logged in?

Both are MIP SDK topics, but they use completely different mechanisms. Neither
has a single "give me the list" call; each is a two-part job (enumerate, then
query / clean up).

---

## 1. Camera status for enabled devices

Two steps: enumerate the enabled cameras from configuration, then ask each
camera's recording server for live status.

### Step 1 - enumerate enabled cameras

Walk the configuration tree and keep only enabled cameras. Disabled devices
carry an `Enabled` flag you test explicitly:

```csharp
var cameras = Configuration.Instance
    .GetItemsByKind(Kind.Camera, ServerId.LocalManagementServer())
    .Where(c => c.Enabled)   // Item.Enabled - skip disabled devices
    .ToList();
```

`GetItemsByKind` hides most disabled items already, but filtering on
`Item.Enabled` is the reliable way to guarantee "enabled only."

### Step 2 - query live status per recording server

`RecorderStatusService2` lives on each recording server (port 7563), not on the
management server. Group cameras by their recording server, build the proxy per
server with the login token, and batch-query:

```csharp
foreach (var group in camerasByRecordingServer)
{
    var token  = LoginSettingsCache.GetLoginSettings(group.Key.ManagementUri).Token;
    var client = BuildRecorderStatusService2Client(group.Key); // http://<recorder>:7563/RecorderStatusService2/

    var deviceIds = group.Select(c => c.FQID.ObjectId).ToArray();
    var status    = client.GetCurrentDeviceStatus(token, deviceIds);

    foreach (var s in status.CameraDeviceStatusArray)
    {
        bool online = s.Started && !s.Error;   // "working"
        // also: s.Motion, s.Recording, s.DbMoving, s.DbRepairing
    }
}
```

Online / working = `Started == true && Error == false`. The same struct also
reports Recording, Motion and DB-moving/repairing if you want richer states.
`GetVideoDeviceStatistics` on the same service returns per-camera streaming
stats (requested live streams, FPS, resolution) if you need throughput data.

Reference samples: Status Demo Console, System Status Client Console.

### Caveats

- **Online is not the same as has-footage.** Probing `SequenceDataSource`
  (RecordingSequence metadata) tells you a camera has recordings, which is a
  different question from device status. A camera can be Started/online with no
  recordings, and vice versa. This is the check the AutoExporter helper uses to
  avoid the "Recorder offline -" export failure, and it should not be confused
  with status.
- **Permissions.** The querying identity needs status/view rights on the
  cameras, or the recorder returns nothing, which looks like offline but is not.

### Live updates instead of a snapshot

If you need to be pushed status changes continuously rather than polling, use
the event subscription path (`NewEventsIndication` with client-side filtering)
or the Event-and-State WebSocket API. For a one-shot "is it up right now,"
`RecorderStatusService2` is the direct tool.

---

## 2. Connected users

There is no real-time "list of logged-in users" API. XProtect does not expose a
live session table. There are two practical techniques, answering two different
questions.

### Primary (real-time): WhoAreOnline presence

A built-in presence query over the Event Server message channel. Any plugin can
ask "who is online?" and get back every MIP Environment currently connected.
This is the same mechanism the Chat sample uses to discover peers.

```csharp
// 1. Start message communication to the master / Event Server (once)
MessageCommunicationManager.Start(EnvironmentManager.Instance.MasterSite.ServerId);
var mc = MessageCommunicationManager.Get(EnvironmentManager.Instance.MasterSite.ServerId);

// 2. Register the response handler BEFORE sending
mc.RegisterCommunicationFilter(
    WhoAreOnlineResponseHandler,
    new CommunicationIdFilter(MessageCommunication.WhoAreOnlineResponse));

// 3. Ask
mc.TransmitMessage(new Message(MessageCommunication.WhoAreOnlineRequest), null, null, null);

// 4. Handler receives a list of EndPointIdentityData
private object WhoAreOnlineResponseHandler(Message message, FQID dest, FQID source)
{
    var online = message.Data as List<EndPointIdentityData>;
    // each entry: IdentityName ("Administrator (10.0.0.5)") + FQID (EndPointFQID)
    return null;
}
```

Each entry (`EndPointIdentityData`) gives only:

- `IdentityName` - format `"<username> (<ip>)"`
- `FQID` - the endpoint address, usable as a destination for other MIP messages

**Important: the raw list is noisy.** It is not a clean list of human users.
Milestone states it is "not meant as a perfect user session monitoring
solution," and the response includes:

- **Milestone services themselves** (Event Server, Log Server, and other
  MIP-connected service endpoints)
- **Duplicate entries** for the same user / endpoint

There is no service flag and no built-in filter, so to get real connected users
you post-process:

1. **Drop service endpoints** - match `IdentityName` against a known-service
   name list, and/or exclude server-local addresses (for example `(0.0.0.0)` or
   the management server's own IP that services report from).
2. **De-duplicate** - group by `IdentityName` (or user + IP) so one user counts
   once.
3. **Treat as best-effort** - a client that joins messaging shows up; a session
   that never joins MIP messaging (some Mobile / Web / API-gateway connections)
   may not.

### Secondary (historical / attributable): Audit log via LogClient

For a clean, attributable record of logins and logouts, read the audit log with
`VideoOS.Platform.Log.LogClient.Instance` (log types: System, Audit, Rules - use
Audit). Audit entries expose:

- Local time
- Message ("User logged in" / "User logged out")
- Permission (Granted / Denied)
- Category, Source type / name
- **User** (for example `[BASIC]\DEMO` or `DOMAIN\user`)
- **User location** (client IP)

To infer "who is connected now," read a recent window, pair each login with its
matching logout, and treat logins with no later logout as still connected.

Caveats:

- **Windowing.** LogClient gets exponentially slower over long spans; the
  samples page through small time windows. Do not request a month in one call.
- **Inference, not truth.** A client that drops without a clean logout leaves a
  dangling login that looks "still connected." Bound the window.
- **Permissions.** Reading the audit log requires an account with audit-read
  rights; a plain integration / basic user sees nothing.

### Which to use

| Question | Technique |
| --- | --- |
| Who is using a client right now? | WhoAreOnline (filter out services, dedupe) |
| Who is online and I also want to message them? | WhoAreOnline (use the returned FQID) |
| Who logged in / out over time, for audit or forensic? | Audit log via LogClient |

WhoAreOnline answers "who is online now" but needs cleanup; the audit log gives
clean attributable history but is not live. They answer different questions.

---

## Quick reference

| Goal | API / class | Notes |
| --- | --- | --- |
| Enabled cameras | `Configuration.Instance.GetItemsByKind(Kind.Camera)`, `Item.Enabled` | Filter on `Enabled` |
| Camera online now | `RecorderStatusService2.GetCurrentDeviceStatus` | `Started && !Error`; per recording server, port 7563 |
| Camera stream stats | `RecorderStatusService2.GetVideoDeviceStatistics` | Requested streams, FPS, resolution |
| Camera has footage | `SequenceDataSource` (RecordingSequence) | Different from online status |
| Connected users (live) | `MessageCommunication` WhoAreOnlineRequest / Response, `EndPointIdentityData` | Includes services + duplicates; filter and dedupe |
| Login / logout history | `VideoOS.Platform.Log.LogClient` (Audit) | Windowed reads; needs audit-read rights |

### Sources

- RecorderStatusService and camera status, forum: get-camera-status threads
  13367 and 13481
- MIP message communication and WhoAreOnline:
  https://doc.developer.milestonesys.com/mipsdk/gettingstarted/intro_mip_messaging.html
- WhoAreOnline services / duplicates caveat (MilestonePSTools Get-WhoIsOnline):
  https://www.milestonepstools.com/commands/en-US/Get-WhoIsOnline/
- Read Audit / System / Rule logs from the MIP SDK (LogClient), and the LogRead
  sample
