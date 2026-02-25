# MIP SDK Knowledge Base

Reference for all Milestone XProtect™ MIP SDK patterns, APIs, quirks, and workarounds learned during development.

## Plugin Architecture

### PluginDefinition (entry point)

Every MIP plugin has exactly one class inheriting `PluginDefinition`. Milestone discovers it via reflection.

```csharp
public class MyPluginDefinition : PluginDefinition
{
    public override Guid Id => new Guid("...");
    public override string Name => "My Plugin";

    public override void Init()
    {
        // Check environment type:
        if (EnvironmentManager.Instance.EnvironmentType == EnvironmentType.Service)
        {
            // Running in Event Server
        }
        // EnvironmentType.Administration = Management Client
    }

    public override void Close() { }
    public override List<ItemNode> ItemNodes { get; }           // Admin tree nodes
    public override List<BackgroundPlugin> BackgroundPlugins { get; }  // Event Server only
    public override UserControl GenerateUserControl() { ... }   // Help panel for plugin node
}
```

### ItemNode Configuration

Defines how items appear in the Management Client tree:

```csharp
var node = new ItemNode(
    kindId,                    // Guid for this item type
    Guid.Empty,                // Parent kind (Guid.Empty = root)
    "RTMP Stream",             // Singular name
    "RTMP Streams",            // Plural name (tree node label)
    Category.Text,             // Category enum from VideoOS.Platform.Admin
    new MyItemManager(kindId), // Handles CRUD
    includeInExport: true      // Include in MIP config backups
);
node.ItemsAllowed = ItemsAllowed.Many;  // Multiple items allowed
```

**`Category` enum** (`VideoOS.Platform.Admin.Category`):

| Value | Int | Use for |
|-------|-----|---------|
| Server | 0 | General server items |
| VideoIn | 1 | Camera/video input |
| VideoOut | 2 | Video output/streaming |
| AudioIn | 3 | Audio input |
| AudioOut | 4 | Audio output |
| TriggerIn | 5 | Trigger input |
| TriggerOut | 6 | Trigger output |
| Text | 7 | Text/metadata |
| Unknown | 8 | Unclassified |
| Layout | 9 | Layout items |

### Plugin Icon

Generated programmatically (no resource files needed):

```csharp
var bmp = new Bitmap(16, 16);
using (var g = Graphics.FromImage(bmp))
{
    g.Clear(Color.White);
    using (var brush = new SolidBrush(Color.FromArgb(40, 100, 200)))
        g.FillPolygon(brush, new[] {
            new PointF(5, 3), new PointF(5, 13), new PointF(13, 8)
        });
}
```

Note: Avoid red backgrounds -- they clash with the Error operational state overlay icon in the MC tree.

### ItemsAllowed

- `ItemsAllowed.Many` - Multiple items under the tree node, user right-clicks "Create New"
- `ItemsAllowed.One` - Single item, auto-created. Requires FQID construction which is complex. Prefer `Many`.

## Item Management (Admin UI)

### ItemManager Lifecycle

1. `Init()` - Called once when environment initializes
2. `GenerateOverviewUserControl()` - Returns `ItemNodeUserControl` shown when parent node selected
3. `GenerateDetailUserControl()` - Called once when detail panel first shown. Create UserControl, subscribe events.
4. `FillUserControl(Item item)` - Called each time user selects an item in tree. Set `CurrentItem = item`, call `_userControl.FillContent(item)`.
5. `ClearUserControl()` - Called when item deselected. Set `CurrentItem = null`, clear UI.
6. `ValidateAndSaveUserControl()` - Called by MC when user clicks Save. Validate + `UpdateItem()` + `SaveItemConfiguration()`. Return `true` on success.
7. `ReleaseUserControl()` - Called when control no longer needed. Unsubscribe events.
8. `Close()` - Called on shutdown.

### Full ItemManager interface

```csharp
public class MyItemManager : ItemManager
{
    // --- User Control ---
    GenerateOverviewUserControl(): ItemNodeUserControl  // Info panel for parent node
    GenerateDetailUserControl(): UserControl            // Config form for selected item
    FillUserControl(Item item): void                    // Load item into form
    ClearUserControl(): void                            // Reset form
    ValidateAndSaveUserControl(): bool                  // Validate + save
    ReleaseUserControl(): void                          // Cleanup

    // --- CRUD ---
    GetItems(): List<Item>
    GetItems(Item parentItem): List<Item>
    GetItem(FQID fqid): Item
    CreateItem(Item parentItem, FQID suggestedFQID): Item
    DeleteItem(Item item): void

    // --- Status ---
    GetOperationalState(Item item): OperationalState
    GetItemStatusDetails(Item item, string language): string  // NON-OBSOLETE overload!
}
```

**IMPORTANT**: `GetItemStatusDetails(Item)` (single parameter) is **obsolete** and causes CS0672 warning. Always use `GetItemStatusDetails(Item item, string language)`.

### ConfigurationChangedByUser Event Pattern

```csharp
// In UserControl - must be internal
internal event EventHandler ConfigurationChangedByUser;

internal void OnUserChange(object sender, EventArgs e)
{
    if (ConfigurationChangedByUser != null)
        ConfigurationChangedByUser(this, new EventArgs());
}
```

- Wire to `TextChanged`, `CheckedChanged`, etc. in Designer
- Also call manually from button click handlers (camera select, etc.)
- Use the INHERITED `ConfigurationChangedByUserHandler` from `ItemManager` base class
- Purpose: Marks item as dirty in MC. Does NOT save. Save happens in `ValidateAndSaveUserControl()`.

### OperationalState

Drives tree node overlay icons in Management Client:

| Value | Icon | Use for |
|-------|------|---------|
| `OperationalState.OkActive` | Green | Active/streaming |
| `OperationalState.Ok` | Normal | Idle/ready |
| `OperationalState.Error` | Red | Error state |
| `OperationalState.Disabled` | Grey | Disabled items |

```csharp
public override OperationalState GetOperationalState(Item item)
{
    if (item == null) return OperationalState.Disabled;
    var enabled = !item.Properties.ContainsKey("Enabled") || item.Properties["Enabled"] != "No";
    if (!enabled) return OperationalState.Disabled;
    var status = item.Properties.ContainsKey("Status") ? item.Properties["Status"] : "";
    if (status.StartsWith("Streaming")) return OperationalState.OkActive;
    if (status.StartsWith("Error")) return OperationalState.Error;
    return OperationalState.Ok;
}
```

Note: The `item` parameter comes from the MC's cached copy. Status properties must be written by BackgroundPlugin via `Configuration.Instance.SaveItemConfiguration()` to propagate.

### Configuration API

```csharp
// Read items
Configuration.Instance.GetItemConfigurations(pluginId, parentItem, kindId): List<Item>
Configuration.Instance.GetItemConfiguration(pluginId, kindId, objectId): Item
Configuration.Instance.GetItem(objectId, Kind.Camera): Item
Configuration.Instance.GetItemsByKind(Kind.Camera): List<Item>

// Write items
Configuration.Instance.SaveItemConfiguration(pluginId, item): void
Configuration.Instance.DeleteItemConfiguration(pluginId, item): void
```

### Item Properties

All custom data stored as `string` key-value pairs:

```csharp
item.Name                           // Display name
item.FQID.ObjectId                  // Unique Guid
item.Properties["CameraId"]         // Camera Guid as string
item.Properties["Enabled"]          // "Yes" / "No" convention for booleans
```

### Camera Picker Dialog

```csharp
var form = new ItemPickerWpfWindow
{
    Items = Configuration.Instance.GetItemsByKind(Kind.Camera),
    KindsFilter = new List<Guid> { Kind.Camera },
    SelectionMode = SelectionModeOptions.AutoCloseOnSelect
};

if (form.ShowDialog() == true && form.SelectedItems?.Any() == true)
{
    var camera = form.SelectedItems.First();
    // camera.FQID.ObjectId, camera.Name
}
```

Requires WPF references: `PresentationCore`, `PresentationFramework`, `WindowsBase`, `System.Xaml`.

### CreateItem Pattern

```csharp
public override Item CreateItem(Item parentItem, FQID suggestedFQID)
{
    CurrentItem = new Item(suggestedFQID, "Default Name");
    CurrentItem.Properties["Enabled"] = "Yes";
    if (_userControl != null) _userControl.FillContent(CurrentItem);
    Configuration.Instance.SaveItemConfiguration(PluginId, CurrentItem);
    return CurrentItem;
}
```

## BackgroundPlugin (Event Server)

Runs in Event Server service context:

```csharp
public class MyBackgroundPlugin : BackgroundPlugin
{
    public override Guid Id => ...;
    public override string Name => "My Background";
    public override List<EnvironmentType> TargetEnvironments =>
        new List<EnvironmentType> { EnvironmentType.Service };
    public override void Init() { /* Start work */ }
    public override void Close() { /* Stop work */ }
}
```

### Configuration Change Detection

```csharp
// Register
_receiver = EnvironmentManager.Instance.RegisterReceiver(
    OnConfigurationChanged,
    new MessageIdAndRelatedKindFilter(
        MessageId.Server.ConfigurationChangedIndication,
        myKindId));  // Filter to only your item kind

// Handler
private object OnConfigurationChanged(Message message, FQID dest, FQID sender)
{
    // Reload configuration
    return null;
}

// Unregister on Close()
EnvironmentManager.Instance.UnRegisterReceiver(_receiver);
```

**QUIRK**: Saving item properties (e.g., status updates) triggers `ConfigurationChangedIndication` for your own kind. Use a config snapshot comparison to avoid infinite reload loops. Only include user-editable properties in the snapshot (CameraId, RtmpUrl, Enabled), NOT status properties that you write yourself.

### Status Property Update Optimization

Writing item properties triggers config change notifications to all clients. To avoid constant save spam:

1. Only persist **significant** state changes (Streaming, Error, Stopped) -- skip transient states (Connecting, Reconnecting, Initializing)
2. Track `LastWrittenStatus` and only call `SaveItemConfiguration()` when the value actually changed
3. This prevents the monitor timer from spamming `ConfigurationChangedIndication` every 10 seconds

### Management Server URI

```csharp
var serverId = EnvironmentManager.Instance.MasterSite.ServerId;
var uri = $"{serverId.ServerScheme}://{serverId.ServerHostname}:{serverId.Serverport}";
```

## Logging

### Plugin Log (Event Server log files)

```csharp
// Appears in: C:\ProgramData\Milestone\XProtect™ Event Server\Logs\
EnvironmentManager.Instance.Log(false, "MyPlugin", "Info message");           // isError=false
EnvironmentManager.Instance.Log(true, "MyPlugin", "Error message");           // isError=true
EnvironmentManager.Instance.Log(true, "MyPlugin", "Error", new[] { ex });     // With exception
```

### System Log (LogClient API -- visible in Management Client)

Writes to **Management Client > Logs > System Log**.

#### Step 1: Define message templates

```csharp
var messages = new Dictionary<string, LogMessage>
{
    ["MyMessage"] = new LogMessage
    {
        Id = "MyMessage",
        Group = Group.System,           // System, Audit, Event, Rules
        Severity = Severity.Info,       // Info, Warning, Error, Debug
        Status = Status.Success,        // Success, Failure, StatusQuo
        RelatedObjectKind = Kind.Server,
        Category = Category.VideoOut.ToString(),   // Free-form string; Admin.Category enum works
        CategoryName = "RTMP Streaming",           // Human-readable display name
        Message = "Stream '{p1}' connected to {p2}"  // {p1}, {p2} are placeholders
    }
};
```

#### Step 2: Register dictionary

```csharp
var dict = new LogMessageDictionary(
    culture: "en-US",
    version: "1.0",
    application: "MyPlugin",
    component: "MyComponent",
    logMessages: messages,
    resourceType: "text");

LogClient.Instance.RegisterDictionary(dict);
LogClient.Instance.SetCulture("en-US");
```

#### Step 3: Write entries

```csharp
var siteItem = EnvironmentManager.Instance.GetSiteItem(
    EnvironmentManager.Instance.MasterSite);

LogClient.Instance.NewEntry(
    "MyPlugin", "MyComponent", "MyMessage",
    siteItem,
    new Dictionary<string, string> { ["p1"] = name, ["p2"] = url });
```

#### Available constants

| Class | Values |
|-------|--------|
| `Group` | `System`, `Audit`, `Event`, `Rules` |
| `Severity` | `Info`, `Warning`, `Error`, `Debug` |
| `Status` | `Success`, `Failure`, `StatusQuo` |

`Category` and `CategoryName` on `LogMessage` are free-form strings. Convention: use `VideoOS.Platform.Admin.Category` enum `.ToString()` for Category, and a readable string for CategoryName.

**QUIRK**: `LogClient.Instance.RegisterDictionary()` must be called before any `NewEntry()`. Call in `BackgroundPlugin.Init()`.

## Helper Process Architecture

### Why a separate process?

`VideoOS.Platform.SDK` and `VideoOS.Platform.SDK.Media` (needed for `RawLiveSource` camera access) are only available in the Recording Server directory, not Event Server. A BackgroundPlugin runs in Event Server. Solution: spawn a standalone helper EXE.

### SDK Initialization in standalone mode

```csharp
// MUST set up assembly resolution BEFORE any SDK type usage
AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
{
    var name = new AssemblyName(args.Name).Name + ".dll";
    foreach (var dir in searchDirs)
    {
        var path = Path.Combine(dir, name);
        if (File.Exists(path)) return Assembly.LoadFrom(path);
    }
    return null;
};

// Initialize
VideoOS.Platform.SDK.Environment.Initialize();
VideoOS.Platform.SDK.Environment.AddServer(uri, CredentialCache.DefaultNetworkCredentials);
VideoOS.Platform.SDK.Environment.Login(uri);
VideoOS.Platform.SDK.Media.Environment.Initialize();

// Cleanup
VideoOS.Platform.SDK.Environment.Logout();
VideoOS.Platform.SDK.Environment.RemoveAllServers();
VideoOS.Platform.SDK.Environment.UnInitialize();
```

### Stderr protocol (helper to parent)

```
STATUS <message>                              # State change
STATS frames=N fps=X.X bytes=N keyframes=N   # Telemetry
<anything else>                               # Forwarded as log line
```

### Authentication

Helper inherits Event Server service account via `CredentialCache.DefaultNetworkCredentials`.

### DLL resolution

References `VideoOS.Platform.dll`, `VideoOS.Platform.SDK.dll`, `VideoOS.Platform.SDK.Media.dll` -- all `<Private>false</Private>`. Resolved at runtime via `AssemblyResolve` handler searching Milestone install dirs.

### Shared source files

Use linked compilation to share code between plugin DLL and helper EXE:

```xml
<!-- In helper .csproj -->
<Compile Include="..\Rtmp\RtmpPublisher.cs" Link="Rtmp\RtmpPublisher.cs" />
```

Both projects compile the same source. `PluginLog` has different implementations per project.

## Camera Frame Source

### RawLiveSource API

```csharp
var rawSource = new RawLiveSource(cameraItem);
rawSource.LiveContentEvent += (sender, e) =>
{
    var args = e as LiveContentRawEventArgs;
    byte[] content = args?.LiveContent?.Content;  // GenericByteData payload
};
rawSource.Init();
rawSource.LiveModeStart = true;

// Stop
rawSource.LiveModeStart = false;
rawSource.Close();
```

### GenericByteData format

Milestone internal video frame format (32-byte big-endian header):

```
Offset  Size  Field
0       2     DataType (0x0010 = video stream)
2       4     TotalLength
6       2     CodecType (0x000A = H.264, 0x000E = H.265)
8       2     SequenceNumber
10      2     Flags (bit 0 = SYNC/keyframe)
12      8     SyncTimestamp (ms since epoch, int64)
20      8     PictureTimestamp (ms since epoch, int64)
28      4     Reserved
32+     ...   Raw codec payload (Annex B for H.264/H.265)
```

### Codec support

Only **H.264** works for RTMP. H.265 requires Enhanced RTMP (not widely supported). MJPEG cannot be muxed into FLV. Detect unsupported codecs early and report to user.

## Build and Deployment

### plugin.def (required manifest)

```xml
<plugin>
   <file name="RtmpStreamer.dll"/>
   <load env="Service, Administration"/>
</plugin>
```

### Deployment directory

```
C:\Program Files\Milestone\MIPPlugins\YourPlugin\
  ├── YourPlugin.dll
  ├── YourHelper.exe
  └── plugin.def
```

Milestone SDK DLLs should NOT be copied here -- loaded from Milestone install dirs.

### Project references

```xml
<Reference Include="VideoOS.Platform">
    <HintPath>$(MilestoneInstallDir)VideoOS.Platform.dll</HintPath>
    <Private>false</Private>  <!-- Don't copy; use installed version -->
</Reference>
```

### Build events

- Pre-build: Stop Event Server, kill MC and helper processes
- Post-build: Copy to MIPPlugins, start Event Server, launch MC
- `timeout` command does NOT work in MSBuild (stdin redirected). Use `ping -n 4 127.0.0.1 >nul` for delays.

## SDK Quirks and Gotchas

1. **ConfigurationChangedIndication on your own saves**: Saving item properties triggers config change messages for your kind. Use snapshot comparison to avoid infinite loops.

2. **GetItemStatusDetails obsolete overload**: `GetItemStatusDetails(Item)` causes CS0672. Use `GetItemStatusDetails(Item item, string language)`.

3. **OperationalState uses cached item**: The MC's cached `Item` may have stale properties. Status must be persisted by BackgroundPlugin via `SaveItemConfiguration()` first.

4. **RawLiveSource not available in Event Server**: Must use standalone SDK in helper process.

5. **Assembly resolution in helper**: Needs custom `AppDomain.AssemblyResolve` to find Milestone DLLs.

6. **LogClient must be registered before use**: Call `RegisterDictionary()` in `Init()` before any `NewEntry()`.

7. **Item properties are strings only**: Booleans use `"Yes"`/`"No"`. Numbers use `.ToString()`.

8. **WPF references for ItemPickerWpfWindow**: Add `PresentationCore`, `PresentationFramework`, `WindowsBase`, `System.Xaml`.

9. **Avoid saving transient states**: Only save stable states (Streaming, Error, Stopped) to item properties. Transient states (Connecting, Reconnecting) cause config change notification spam.

10. **Locale issues with number formatting**: Always use `CultureInfo.InvariantCulture` for parsing/formatting doubles. German locale uses comma as decimal separator.

11. **SDK method deprecations**: `Environment.AddServer(Uri, NetworkCredential)` and `Environment.Login(Uri)` are deprecated but work. Warnings are harmless.

12. **LogMessage Category/CategoryName**: Both are free-form strings, not enums. Use `VideoOS.Platform.Admin.Category` enum `.ToString()` for the Category value.
