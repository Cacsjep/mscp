# MSC Community Plugins for Milestone XProtect™

[![Build & Release](../../actions/workflows/build-release.yml/badge.svg)](../../actions/workflows/build-release.yml)
[![OpenSSF Scorecard](https://api.scorecard.dev/projects/github.com/Cacsjep/mscp/badge)](https://scorecard.dev/viewer/?uri=github.com/Cacsjep/mscp)
[![Latest release](https://img.shields.io/github/v/release/Cacsjep/mscp?label=release)](../../releases/latest)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![Security Policy](https://img.shields.io/badge/security-policy-blue.svg)](.github/SECURITY.md)

> [!IMPORTANT]
> This is an independent open source project and is **not affiliated with, endorsed by, or supported by Milestone Systems**. XProtect™ is a trademark of Milestone Systems A/S.

A collection of community-built plugins and drivers for Milestone XProtect™, maintained as a single repository with a unified build and installer.

## Installation

### Unified Installer (Recommended)

1. Download `MSCPlugins-vX.X-Setup.msi` from the [Releases](../../releases) page
2. Run as **Administrator**
3. Select the plugins and drivers you want to install
4. The installer handles stopping/starting the required Milestone services automatically

### Manual (ZIP)

Individual ZIPs for each plugin/driver are also available on the [Releases](../../releases) page. See each plugin's README for manual installation instructions:

#### Install Path

`C:\Program Files\Milestone\MIPPlugins\`

> [!NOTE]
> Always **unblock** downloaded ZIP files before extracting (right-click -> Properties -> Unblock). Windows marks downloaded files as untrusted and will block the DLLs from loading if you skip this step.

## Building from Source

**Prerequisites:** Visual Studio 2022+, .NET Framework 4.7/4.8, NuGet

1. Open `MSCPlugins.sln` in Visual Studio
2. Restore NuGet packages
3. Build the solution

The solution contains all projects organized in solution folders matching the repository categories.

### Local Release Build

```powershell
.\build.ps1
```

This builds all plugins in Release configuration, creates per-plugin ZIPs and a WiX MSI installer (requires [WiX Toolset](https://wixtoolset.org/): `dotnet tool install --global wix`). All output goes to the `build/` directory.

## Development Workflow

### Shared Build Infrastructure

All projects share a centralized stop/deploy/start cycle via `Directory.Build.props` and `Directory.Build.targets` at the solution root. This eliminates duplicated build event boilerplate, each project just declares what it needs:

```xml
<PropertyGroup>
  <PluginName>MyPlugin</PluginName>         <!-- Auto-deploys to MIPPlugins\MyPlugin\ -->
  <StopSmartClient>true</StopSmartClient>   <!-- Kill Client.exe before build -->
  <LaunchSmartClient>true</LaunchSmartClient> <!-- Launch Smart Client after deploy -->
</PropertyGroup>
```

**Available flags:**

| Flag | Effect |
|---|---|
| `StopSmartClient` | Kill Smart Client (`Client.exe`) before build |
| `StopAdminClient` | Kill Management Client (`VideoOS.Administration.exe`) before build |
| `StopEventServer` | Stop the Event Server service before build |
| `StopRecordingServer` | Stop the Recording Server service before build |
| `StartEventServer` | Restart the Event Server service after deploy |
| `StartRecordingServer` | Restart the Recording Server service after deploy |
| `LaunchSmartClient` | Launch Smart Client after deploy |
| `LaunchAdminClient` | Launch Management Client after deploy |

All flags default to `false`. The entire stop/deploy/start cycle is skipped during CI builds (`CIBuild=true`).

Since all environments load from the same `MIPPlugins\` folder, multi-environment plugins (like CertWatchdog) must stop every process that holds the DLL before deploying. The build handles this automatically based on the project's flags.

### Launch Profiles

Each SDK-style project includes `Properties/launchSettings.json` with debug profiles for its target environments. Select the profile from the Visual Studio debug dropdown:

| Plugin Type | Profiles |
|---|---|
| Smart Client plugins | Smart Client |
| Admin plugins (Service + Admin) | Smart Client, Management Client, Event Server (console) |
| Device drivers | *(use VS Debug tab, old-style project)* |

The **Event Server (console)** profile launches the Event Server with the `-x` flag, which runs it as a regular console process instead of a Windows service, allowing Visual Studio to attach the debugger from startup. This is the [SDK-recommended approach](https://doc.developer.milestonesys.com/mipsdk/) for debugging Event Server plugins.

> **Note:** Run Visual Studio as **Administrator** writing to `C:\Program Files\Milestone\MIPPlugins\` requires elevated privileges.

## Releasing

1. Update version strings in each plugin's definition/assembly files
2. Commit, tag, and push:
   ```bash
   git tag v1.1.0
   git push origin main --tags
   ```
3. GitHub Actions builds each plugin in parallel (matrix strategy) and publishes per-plugin ZIPs + the unified installer to [Releases](../../releases)

## Contributing

Contributions are welcome! Feel free to open issues or submit pull requests.

### Pull Request Guidelines

`main` is a protected branch. Every pull request needs a green `PR Build / ci-success` check and a review before it can be merged. Direct pushes to `main` are not allowed.

- Branch off `main` and keep each PR focused on a single change. Smaller PRs are faster to review.
- Name your branch `type/short-description`, for example `feat/rtsp-driver`, `fix/pki-crash`, or `docs/readme-update`.

**PR title format** (Conventional Commits):

```
type(scope): short summary
```

- `type` is one of: `feat`, `fix`, `docs`, `refactor`, `perf`, `test`, `build`, `ci`, `chore`
- `scope` is optional and names the affected plugin or area, e.g. `RTSP Driver`, `PKI`, `Remote Manager`
- `summary` is imperative and lower case, with no trailing period

Examples:

- `feat(RTSP Driver): add something cool`
- `fix(PKI): handle missing xyz`
- `docs: clarify Smart Client install steps`

**Docs-only changes skip the build.** If every changed file in a PR is under `docs/`, ends in `.md`, or is `mkdocs.yml`, the `PR Build` workflow skips the Windows build matrix and the installer. The required `PR Build / ci-success` check still reports green within seconds, so documentation PRs do not wait on a full compile. Adding any code file to the PR re-enables the full build. Use a `docs/...` branch name and a `docs:` PR title to keep these easy to spot.

### Adding a New Plugin or Driver

Step-by-step guide for Smart Client Plugins and Admin Plugins. Device drivers use old-style projects; follow an existing driver under `Device Drivers/` as a reference, and use the same solution and `plugins.json` wiring described in [Common steps](#common-steps-both-plugin-types). Use an existing plugin as a reference (e.g. `Smart Client Plugins/Weather/` for a Smart Client plugin).

Place your project in the correct category folder:

| Category | Folder | Runs on |
|---|---|---|
| Smart Client Plugin | `Smart Client Plugins/YourPlugin/` | Smart Client |
| Device Driver | `Device Drivers/YourDriver/` | Recording Server |
| Admin Plugin | `Admin Plugins/YourPlugin/` | Event Server / Management Client |

#### Prerequisites

- Visual Studio 2022+
- .NET Framework 4.8 SDK
- Milestone XProtect installed (for testing)
- Generate unique GUIDs before starting (`[guid]::NewGuid()` in PowerShell)

#### Smart Client Plugin

Based on the Weather, RDP, and Notepad plugin patterns.

**GUIDs needed**

| GUID | Purpose | Used In |
|------|---------|---------|
| GUID 1 | PluginId | `*Definition.cs` |
| GUID 2 | ViewItemKind | `*Definition.cs` |
| GUID 3 | BackgroundPluginId | `*Definition.cs` |
| GUID 4 | ViewItemPlugin.Id | `*ViewItemPlugin.cs` |
| GUID 5 | Project GUID | `MSCPlugins.sln` |

**Directory structure**

```
Smart Client Plugins/
  MyPlugin/
    MyPlugin.csproj
    plugin.def
    MyPluginDefinition.cs
    Resources/
      PluginIcon.png
    Properties/
      launchSettings.json
    Client/
      MyPluginViewItemPlugin.cs
      MyPluginViewItemManager.cs
      MyPluginViewItemWpfUserControl.xaml
      MyPluginViewItemWpfUserControl.xaml.cs
      MyPluginPropertiesWpfUserControl.xaml
      MyPluginPropertiesWpfUserControl.xaml.cs
    Background/
      MyPluginBackgroundPlugin.cs
```

**plugin.def**

```xml
<plugin>
   <file name="MyPlugin.dll"/>
   <load env="SmartClient"/>
</plugin>
```

**.csproj**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net48</TargetFramework>
    <UseWPF>true</UseWPF>
    <OutputType>Library</OutputType>
    <RootNamespace>MyPlugin</RootNamespace>
    <AssemblyName>MyPlugin</AssemblyName>
    <LangVersion>latest</LangVersion>
    <PluginName>MyPlugin</PluginName>
    <StopSmartClient>true</StopSmartClient>
    <LaunchSmartClient>true</LaunchSmartClient>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="MilestoneSystems.VideoOS.Platform" Version="*-*" />
  </ItemGroup>

  <ItemGroup>
    <Reference Include="System.Windows.Forms" />
    <Reference Include="System.Drawing" />
  </ItemGroup>

  <ItemGroup>
    <Content Include="plugin.def">
      <CopyToOutputDirectory>Always</CopyToOutputDirectory>
    </Content>
    <Resource Include="Resources\PluginIcon.png" />
  </ItemGroup>
</Project>
```

Key points:
- Always `net48` with `UseWPF=true`
- MIP SDK NuGet uses wildcard prerelease: `Version="*-*"`
- `plugin.def` must be `CopyToOutputDirectory=Always`
- Plugin icon is `<Resource>` (not `Content`)
- `PluginName` + deploy flags are used by `Directory.Build.props`/`Directory.Build.targets`
- Launch profile name should be `"Smart Client"` (not the plugin name) in `launchSettings.json`

**Common gotcha: FontAwesome5 is NOT available as a XAML StaticResource**

`FontAwesome5FreeSolid` is **not** a WPF resource you can use in XAML like `FontFamily="{StaticResource FontAwesome5FreeSolid}"`. It will throw:
```
Cannot find resource named 'FontAwesome5FreeSolid'. Resource names are case sensitive.
```

FontAwesome icons are only available **via code** through the CommunitySDK `PluginIcon.RenderIconSource()` / `PluginIcon.Render()` methods (used in `PluginDefinition` for the plugin icon). For XAML UI, use Unicode characters (e.g. `&#x21BB;` for a rotation arrow) or WPF Path/Geometry instead.

**PluginDefinition**

```csharp
using System;
using System.Collections.Generic;
using System.Reflection;
using MyPlugin.Background;
using MyPlugin.Client;
using VideoOS.Platform;
using VideoOS.Platform.Background;
using VideoOS.Platform.Client;
using VideoOS.Platform.UI.Controls;

namespace MyPlugin
{
    public class MyPluginDefinition : PluginDefinition
    {
        private static readonly VideoOSIconSourceBase _pluginIcon;

        internal static Guid MyPluginPluginId = new Guid("GUID-1-HERE");
        internal static Guid MyPluginViewItemKind = new Guid("GUID-2-HERE");
        internal static Guid MyPluginBackgroundPluginId = new Guid("GUID-3-HERE");

        static MyPluginDefinition()
        {
            var packString = $"pack://application:,,,/{Assembly.GetExecutingAssembly().GetName().Name};component/Resources/PluginIcon.png";
            _pluginIcon = new VideoOSIconUriSource { Uri = new Uri(packString) };
        }

        internal static VideoOSIconSourceBase PluginIcon => _pluginIcon;

        public override Guid Id => MyPluginPluginId;
        public override string Name => "MyPlugin Plugin";
        public override string Manufacturer => "MSC Community Plugins";

        public override System.Drawing.Image Icon
            => VideoOS.Platform.UI.Util.ImageList.Images[VideoOS.Platform.UI.Util.PluginIx];

        public override List<ViewItemPlugin> ViewItemPlugins
            => new List<ViewItemPlugin> { new MyPluginViewItemPlugin() };

        public override List<BackgroundPlugin> BackgroundPlugins
            => new List<BackgroundPlugin> { new MyPluginBackgroundPlugin() };
    }
}
```

Three internal static GUIDs: `PluginId`, `ViewItemKind`, `BackgroundPluginId`. Icon loaded via WPF pack URI from embedded resource.

**ViewItemPlugin**

```csharp
using System;
using VideoOS.Platform.Client;
using VideoOS.Platform.UI.Controls;

namespace MyPlugin.Client
{
    public class MyPluginViewItemPlugin : ViewItemPlugin
    {
        public override Guid Id => new Guid("GUID-4-HERE");
        public override string Name => "MyPlugin";

        public override VideoOSIconSourceBase IconSource
        {
            get => MyPluginDefinition.PluginIcon;
            protected set => base.IconSource = value;
        }

        public override ViewItemManager GenerateViewItemManager()
            => new MyPluginViewItemManager();

        public override void Init() { }
        public override void Close() { }
    }
}
```

**ViewItemManager**

```csharp
using VideoOS.Platform.Client;

namespace MyPlugin.Client
{
    public class MyPluginViewItemManager : ViewItemManager
    {
        private const string SomePropertyKey = "SomeProperty";

        public MyPluginViewItemManager() : base("MyPluginViewItemManager") { }

        public string SomeProperty
        {
            get => GetProperty(SomePropertyKey) ?? string.Empty;
            set => SetProperty(SomePropertyKey, value);
        }

        public void Save() => SaveProperties();
        public override void PropertiesLoaded() { }

        public override ViewItemWpfUserControl GenerateViewItemWpfUserControl()
            => new MyPluginViewItemWpfUserControl(this);

        public override PropertiesWpfUserControl GeneratePropertiesWpfUserControl()
            => new MyPluginPropertiesWpfUserControl(this);
    }
}
```

Property storage: `GetProperty(key) ?? defaultValue` / `SetProperty(key, value)` / `SaveProperties()`.

**ViewItemWpfUserControl**

```xml
<external:ViewItemWpfUserControl
    xmlns:external="clr-namespace:VideoOS.Platform.Client;assembly=VideoOS.Platform"
    x:Class="MyPlugin.Client.MyPluginViewItemWpfUserControl"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    PreviewMouseLeftButtonUp="OnMouseLeftUp"
    PreviewMouseDoubleClick="OnMouseDoubleClick">
    <Grid Background="#FF1C2326">
        <!-- Live mode UI -->
        <Grid x:Name="setupOverlay" Visibility="Collapsed">
            <!-- Setup mode overlay -->
        </Grid>
    </Grid>
</external:ViewItemWpfUserControl>
```

```csharp
using System;
using System.Windows.Input;
using VideoOS.Platform;
using VideoOS.Platform.Client;
using VideoOS.Platform.Messaging;

namespace MyPlugin.Client
{
    public partial class MyPluginViewItemWpfUserControl : ViewItemWpfUserControl
    {
        private readonly MyPluginViewItemManager _viewItemManager;
        private object _modeChangedReceiver;

        public MyPluginViewItemWpfUserControl(MyPluginViewItemManager viewItemManager)
        {
            _viewItemManager = viewItemManager;
            InitializeComponent();
        }

        public override void Init()
        {
            _modeChangedReceiver = EnvironmentManager.Instance.RegisterReceiver(
                new MessageReceiver(OnModeChanged),
                new MessageIdFilter(MessageId.System.ModeChangedIndication));
            ApplyMode(EnvironmentManager.Instance.Mode);
        }

        public override void Close()
        {
            if (_modeChangedReceiver != null)
            {
                EnvironmentManager.Instance.UnRegisterReceiver(_modeChangedReceiver);
                _modeChangedReceiver = null;
            }
        }

        private void ApplyMode(Mode mode)
        {
            // Switch between Setup and Live UI
        }

        private object OnModeChanged(Message message, FQID destination, FQID sender)
        {
            Dispatcher.BeginInvoke(new Action(() => ApplyMode((Mode)message.Data)));
            return null;
        }

        private void OnMouseLeftUp(object sender, MouseButtonEventArgs e) => FireClickEvent();
        private void OnMouseDoubleClick(object sender, MouseButtonEventArgs e) => FireDoubleClickEvent();

        public override bool Maximizable => true;
        public override bool Selectable => true;
        public override bool ShowToolbar => false;
    }
}
```

Key patterns:
- Register `ModeChangedIndication` in `Init()`, unregister in `Close()`
- Use `Dispatcher.BeginInvoke` for mode change handler (non-UI thread)
- `FireClickEvent()`/`FireDoubleClickEvent()` for Smart Client selection

**PropertiesWpfUserControl**

```xml
<external:PropertiesWpfUserControl
    xmlns:external="clr-namespace:VideoOS.Platform.Client;assembly=VideoOS.Platform"
    x:Class="MyPlugin.Client.MyPluginPropertiesWpfUserControl"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <StackPanel Margin="10">
        <GroupBox Header="Settings" Foreground="White">
            <StackPanel Margin="8">
                <TextBlock Text="Some Property:" Foreground="White" Margin="0,0,0,4" />
                <TextBox x:Name="somePropertyBox" />
            </StackPanel>
        </GroupBox>
    </StackPanel>
</external:PropertiesWpfUserControl>
```

```csharp
using VideoOS.Platform.Client;

namespace MyPlugin.Client
{
    public partial class MyPluginPropertiesWpfUserControl : PropertiesWpfUserControl
    {
        private readonly MyPluginViewItemManager _viewItemManager;

        public MyPluginPropertiesWpfUserControl(MyPluginViewItemManager viewItemManager)
        {
            _viewItemManager = viewItemManager;
            InitializeComponent();
        }

        public override void Init()
        {
            somePropertyBox.Text = _viewItemManager.SomeProperty;
        }

        public override void Close()
        {
            _viewItemManager.SomeProperty = somePropertyBox.Text;
            _viewItemManager.Save();
        }
    }
}
```

`Init()` loads from manager, `Close()` saves back.

**BackgroundPlugin (Smart Client)**

```csharp
using System;
using System.Collections.Generic;
using VideoOS.Platform;
using VideoOS.Platform.Background;

namespace MyPlugin.Background
{
    public class MyPluginBackgroundPlugin : BackgroundPlugin
    {
        public override Guid Id => MyPluginDefinition.MyPluginBackgroundPluginId;
        public override string Name => "MyPlugin BackgroundPlugin";

        public override List<EnvironmentType> TargetEnvironments
            => new List<EnvironmentType> { EnvironmentType.SmartClient };

        public override void Init() { }
        public override void Close() { }
    }
}
```

**launchSettings.json**

```json
{
  "profiles": {
    "MyPlugin": {
      "commandName": "Executable",
      "executablePath": "C:\\Program Files\\Milestone\\XProtect Smart Client\\Client.exe"
    }
  }
}
```

#### Admin Plugin (Management Client + Event Server)

Based on the HttpRequests, CertWatchdog, and Auditor plugin patterns.

**GUIDs needed**

| GUID | Purpose | Used In |
|------|---------|---------|
| GUID 1 | PluginId | `*Definition.cs` |
| GUID 2 | FolderKindId (parent item) | `*Definition.cs`, `ItemNode` |
| GUID 3 | ItemKindId (child item) | `*Definition.cs`, `ItemNode` |
| GUID 4 | BackgroundPluginId | `*Definition.cs` |
| GUID 5 | Project GUID | `MSCPlugins.sln` |
| GUID 6+ | Event/State/Action GUIDs | If using Rules, events, or actions |

**Directory structure**

```
Admin Plugins/
  MyPlugin/
    MyPlugin.csproj
    plugin.def
    MyPluginDefinition.cs
    SystemLog.cs            (optional - CommunitySDK SystemLogBase)
    Admin/
      HelpPage.html         (in-app help, loaded by GenerateUserControl)
      MyFolderItemManager.cs
      MyFolderUserControl.cs / .Designer.cs
      MyItemManager.cs
      MyItemUserControl.cs / .Designer.cs
    Background/
      MyBackgroundPlugin.cs
      MyActionManager.cs    (optional - for Rules integration)
    Messaging/              (optional - for cross-environment communication)
      MessageIds.cs
      CrossMessageHandler.cs
```

**plugin.def**

```xml
<plugin>
   <file name="MyPlugin.dll"/>
   <load env="Service, Administration"/>
</plugin>
```

`Service` = Event Server background plugin, `Administration` = Management Client admin UI.

**.csproj**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net48</TargetFramework>
    <UseWPF>true</UseWPF>
    <OutputType>Library</OutputType>
    <RootNamespace>MyPlugin</RootNamespace>
    <AssemblyName>MyPlugin</AssemblyName>
    <LangVersion>latest</LangVersion>
    <GenerateAssemblyInfo>false</GenerateAssemblyInfo>
    <PluginName>MyPlugin</PluginName>
    <StopAdminClient>true</StopAdminClient>
    <StopEventServer>true</StopEventServer>
    <StartEventServer>true</StartEventServer>
    <LaunchAdminClient>false</LaunchAdminClient>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="MilestoneSystems.VideoOS.Platform" Version="*-*" />
  </ItemGroup>

  <ItemGroup>
    <Reference Include="System.Windows.Forms" />
    <Reference Include="System.Drawing" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\CommunitySDK\CommunitySDK.csproj" />
  </ItemGroup>

  <ItemGroup>
    <Content Include="Admin\HelpPage.html">
      <CopyToOutputDirectory>Always</CopyToOutputDirectory>
    </Content>
    <Content Include="plugin.def">
      <CopyToOutputDirectory>Always</CopyToOutputDirectory>
    </Content>
  </ItemGroup>
</Project>
```

Key differences from Smart Client:
- Deploy flags: `StopAdminClient`, `StopEventServer`, `StartEventServer` instead of `StopSmartClient`/`LaunchSmartClient`
- References CommunitySDK for `PluginLog`, `PluginIcon`, `SystemLogBase`, `CrossMessageHandler`
- Admin UI is WinForms (not WPF), so `System.Windows.Forms` is required
- `HelpPage.html` is `Content` with `CopyToOutputDirectory=Always`

**PluginDefinition (Admin)**

```csharp
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using CommunitySDK;
using FontAwesome5;
using MyPlugin.Admin;
using MyPlugin.Background;
using VideoOS.Platform;
using VideoOS.Platform.Admin;
using VideoOS.Platform.Background;
using VideoOS.Platform.RuleAction;

namespace MyPlugin
{
    public class MyPluginDefinition : PluginDefinition
    {
        internal static readonly Guid PluginId = new Guid("GUID-1-HERE");
        internal static readonly Guid FolderKindId = new Guid("GUID-2-HERE");
        internal static readonly Guid ItemKindId = new Guid("GUID-3-HERE");
        internal static readonly Guid BackgroundPluginId = new Guid("GUID-4-HERE");

        // Optional: Event/State GUIDs for Rules integration
        internal static readonly Guid EventGroupId = new Guid("...");
        internal static readonly Guid EvtSuccessId = new Guid("...");
        internal static readonly Guid EvtFailedId = new Guid("...");
        internal static readonly Guid StateGroupId = new Guid("...");

        private List<BackgroundPlugin> _backgroundPlugins = new List<BackgroundPlugin>();
        private List<ItemNode> _itemNodes;
        private MyActionManager _actionManager;    // optional
        private Image _pluginIcon, _folderIcon, _itemIcon;

        public override Guid Id => PluginId;
        public override string Name => "My Plugin";
        public override string Manufacturer => "https://github.com/Cacsjep";
        public override Image Icon => _pluginIcon;

        public override void Init()
        {
            // Icons via FontAwesome (CommunitySDK)
            try
            {
                _pluginIcon = PluginIcon.Render(EFontAwesomeIcon.Solid_Cog);
                _folderIcon = PluginIcon.Render(EFontAwesomeIcon.Solid_FolderOpen);
                _itemIcon = PluginIcon.Render(EFontAwesomeIcon.Solid_File);
            }
            catch
            {
                var images = VideoOS.Platform.UI.Util.ImageList.Images;
                _pluginIcon = images[VideoOS.Platform.UI.Util.PluginIx];
                _folderIcon = images[VideoOS.Platform.UI.Util.FolderIconIx];
                _itemIcon = images[VideoOS.Platform.UI.Util.PluginIx];
            }

            // Only register background plugin on Event Server
            if (EnvironmentManager.Instance.EnvironmentType == EnvironmentType.Service)
                _backgroundPlugins.Add(new MyBackgroundPlugin());

            // Optional: ActionManager for Rules integration
            _actionManager = new MyActionManager();
        }

        public override void Close()
        {
            _itemNodes = null;
            _backgroundPlugins.Clear();
        }

        // IMPORTANT: Nested ItemNode structure (children inside parent)
        public override List<ItemNode> ItemNodes
        {
            get
            {
                var env = EnvironmentManager.Instance.EnvironmentType;
                if (env == EnvironmentType.Administration || env == EnvironmentType.Service)
                {
                    if (_itemNodes == null)
                    {
                        _itemNodes = new List<ItemNode>
                        {
                            new ItemNode(
                                FolderKindId, Guid.Empty,
                                "Folder", _folderIcon,
                                "Folders", _folderIcon,
                                Category.Text, true, ItemsAllowed.Many,
                                new MyFolderItemManager(FolderKindId),
                                new List<ItemNode>    // <-- child items nested here
                                {
                                    new ItemNode(
                                        ItemKindId, FolderKindId,
                                        "Item", _itemIcon,
                                        "Items", _itemIcon,
                                        Category.Text, true, ItemsAllowed.Many,
                                        new MyItemManager(ItemKindId),
                                        null)
                                })
                        };
                    }
                    return _itemNodes;
                }
                return null;
            }
        }

        // In-app help page (shown when clicking the plugin root node)
        public override UserControl GenerateUserControl()
        {
            return new HtmlHelpUserControl(
                System.Reflection.Assembly.GetExecutingAssembly(), "Admin", "HelpPage.html");
        }

        public override List<BackgroundPlugin> BackgroundPlugins => _backgroundPlugins;

        // Optional: expose ActionManager for Rules
        public override ActionManager ActionManager => _actionManager;
    }
}
```

**Critical: ItemNode nesting** - child ItemNodes go in the parent's children list parameter, NOT as separate entries in the flat `_itemNodes` list. This is required for the Rules engine to offer folder/individual/all targeting.

**ItemManager (Admin - parent folder)**

```csharp
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Forms;
using VideoOS.Platform;
using VideoOS.Platform.Admin;

namespace MyPlugin.Admin
{
    public class MyFolderItemManager : ItemManager
    {
        private MyFolderUserControl _userControl;
        private readonly Guid _kind;

        public MyFolderItemManager(Guid kind) { _kind = kind; }

        public override void Init() { }
        public override void Close() { ReleaseUserControl(); }

        // ---- Event Registration (MUST be on parent ItemManager) ----
        // The Rules engine discovers events from the top-level ItemManager

        public override Collection<VideoOS.Platform.Data.EventGroup> GetKnownEventGroups(CultureInfo culture)
        {
            return new Collection<VideoOS.Platform.Data.EventGroup>
            {
                new VideoOS.Platform.Data.EventGroup
                {
                    ID = MyPluginDefinition.EventGroupId,
                    Name = "My Plugin"
                }
            };
        }

        public override Collection<VideoOS.Platform.Data.EventType> GetKnownEventTypes(CultureInfo culture)
        {
            var sourceKinds = new List<Guid>
            {
                MyPluginDefinition.ItemKindId,
                MyPluginDefinition.FolderKindId
            };

            return new Collection<VideoOS.Platform.Data.EventType>
            {
                new VideoOS.Platform.Data.EventType
                {
                    ID = MyPluginDefinition.EvtSuccessId,
                    Message = "Action Succeeded",
                    GroupID = MyPluginDefinition.EventGroupId,
                    StateGroupID = MyPluginDefinition.StateGroupId,
                    State = "Success",
                    DefaultSourceKind = MyPluginDefinition.FolderKindId,
                    SourceKinds = sourceKinds
                },
                new VideoOS.Platform.Data.EventType
                {
                    ID = MyPluginDefinition.EvtFailedId,
                    Message = "Action Failed",
                    GroupID = MyPluginDefinition.EventGroupId,
                    StateGroupID = MyPluginDefinition.StateGroupId,
                    State = "Failed",
                    DefaultSourceKind = MyPluginDefinition.FolderKindId,
                    SourceKinds = sourceKinds
                }
            };
        }

        public override Collection<VideoOS.Platform.Data.StateGroup> GetKnownStateGroups(CultureInfo culture)
        {
            return new Collection<VideoOS.Platform.Data.StateGroup>
            {
                new VideoOS.Platform.Data.StateGroup
                {
                    ID = MyPluginDefinition.StateGroupId,
                    Name = "My Plugin Status",
                    States = new[] { "Success", "Failed" }
                }
            };
        }

        // ---- User Control ----

        public override UserControl GenerateDetailUserControl()
        {
            _userControl = new MyFolderUserControl();
            _userControl.ConfigurationChangedByUser += ConfigurationChangedByUserHandler;
            return _userControl;
        }

        public override void ReleaseUserControl()
        {
            if (_userControl != null)
                _userControl.ConfigurationChangedByUser -= ConfigurationChangedByUserHandler;
            _userControl = null;
        }

        public override void FillUserControl(Item item)
        {
            CurrentItem = item;
            _userControl?.FillContent(item);
        }

        public override void ClearUserControl()
        {
            CurrentItem = null;
            _userControl?.ClearContent();
        }

        public override bool ValidateAndSaveUserControl()
        {
            if (CurrentItem != null && _userControl != null)
            {
                _userControl.UpdateItem(CurrentItem);
                Configuration.Instance.SaveItemConfiguration(MyPluginDefinition.PluginId, CurrentItem);
            }
            return true;
        }

        // ---- Item CRUD ----

        public override string GetItemName() => _userControl?.DisplayName ?? "";
        public override void SetItemName(string name) { if (CurrentItem != null) CurrentItem.Name = name; }

        public override List<Item> GetItems()
            => Configuration.Instance.GetItemConfigurations(MyPluginDefinition.PluginId, null, _kind);

        public override List<Item> GetItems(Item parentItem)
            => Configuration.Instance.GetItemConfigurations(MyPluginDefinition.PluginId, parentItem, _kind);

        public override Item GetItem(FQID fqid)
            => Configuration.Instance.GetItemConfiguration(MyPluginDefinition.PluginId, _kind, fqid.ObjectId);

        public override Item CreateItem(Item parentItem, FQID suggestedFQID)
        {
            CurrentItem = new Item(suggestedFQID, "New Folder");
            _userControl?.FillContent(CurrentItem);
            Configuration.Instance.SaveItemConfiguration(MyPluginDefinition.PluginId, CurrentItem);
            return CurrentItem;
        }

        public override void DeleteItem(Item item)
        {
            if (item != null)
                Configuration.Instance.DeleteItemConfiguration(MyPluginDefinition.PluginId, item);
        }

        public override OperationalState GetOperationalState(Item item) => OperationalState.Ok;
    }
}
```

**Critical: event registration on the parent ItemManager.** `GetKnownEventGroups`, `GetKnownEventTypes`, `GetKnownStateGroups` must be on the top-level (folder) ItemManager for the Rules engine to discover them.

**ItemManager (Admin - child item)**

Same pattern as parent but without event registration overrides. Add validation, duplicate support:

```csharp
public override UserControl GenerateDetailUserControl()
{
    _userControl = new MyItemUserControl();
    _userControl.ConfigurationChangedByUser += ConfigurationChangedByUserHandler;
    _userControl.DuplicateRequested += OnDuplicateRequested;
    return _userControl;
}

private void OnDuplicateRequested(object sender, EventArgs e)
{
    if (CurrentItem == null) return;

    var src = CurrentItem;
    var newFqid = new FQID(
        src.FQID.ServerId,
        src.FQID.ParentId,    // same parent folder
        Guid.NewGuid(),        // new unique ID
        FolderType.No,
        _kind);

    var newItem = new Item(newFqid, "Copy of " + src.Name);

    foreach (var kvp in src.Properties)
        newItem.Properties[kvp.Key] = kvp.Value;

    Configuration.Instance.SaveItemConfiguration(MyPluginDefinition.PluginId, newItem);

    MessageBox.Show(
        $"Created \"{newItem.Name}\".\n\nCollapse/expand the folder to see it.",
        "Duplicated", MessageBoxButtons.OK, MessageBoxIcon.Information);
}
```

FQID constructor: `new FQID(ServerId, ParentId, ObjectId, FolderType, Kind)` - 5 parameters.

**ActionManager (Rules integration)**

```csharp
using System;
using System.Collections.ObjectModel;
using VideoOS.Platform;
using VideoOS.Platform.Data;
using VideoOS.Platform.RuleAction;

namespace MyPlugin.Background
{
    public class MyActionManager : ActionManager
    {
        internal static readonly Guid ExecuteActionId = new Guid("...");

        public override Collection<ActionDefinition> GetActionDefinitions()
        {
            return new Collection<ActionDefinition>
            {
                new ActionDefinition
                {
                    Id = ExecuteActionId,
                    Name = "Execute My Action",
                    SelectionText = "Execute <My Item>",
                    DescriptionText = "Execute {0}",
                    ActionItemKind = new ActionElement
                    {
                        DefaultText = "My Item",
                        ItemKinds = new Collection<Guid> { MyPluginDefinition.ItemKindId }
                    }
                }
            };
        }

        public override void ExecuteAction(Guid actionId, Collection<FQID> actionItems, BaseEvent sourceEvent)
        {
            if (actionId != ExecuteActionId) return;

            // IMPORTANT: sourceEvent is BaseEvent, NOT AnalyticsEvent
            // Cast to AnalyticsEvent will return null for most rule triggers
            // Use sourceEvent.EventHeader directly

            foreach (var fqid in actionItems)
            {
                // The Rules engine resolves targeting:
                // - Individual item: one FQID
                // - All in folder: all item FQIDs in that folder
                // - ALL: every item FQID
                MyBackgroundPlugin.Instance.HandleAction(fqid, sourceEvent);
            }
        }
    }
}
```

**Critical: `sourceEvent` is `BaseEvent`, not `AnalyticsEvent`.** The Rules engine passes `BaseEvent` which has `EventHeader`. Casting to `AnalyticsEvent` returns `null` and you lose event data.

One action definition is sufficient - the Rules engine automatically provides individual / folder / all targeting in the UI.

**BackgroundPlugin (Event Server)**

```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using VideoOS.Platform;
using VideoOS.Platform.Background;
using VideoOS.Platform.Data;
using VideoOS.Platform.Messaging;

namespace MyPlugin.Background
{
    public class MyBackgroundPlugin : BackgroundPlugin
    {
        internal static MyBackgroundPlugin Instance { get; private set; }

        private List<Item> _items = new List<Item>();
        private readonly object _configLock = new object();
        private object _configMessageObj;
        private volatile bool _closing;

        public override Guid Id => MyPluginDefinition.BackgroundPluginId;
        public override string Name => "My Plugin Background";
        public override List<EnvironmentType> TargetEnvironments
            => new List<EnvironmentType> { EnvironmentType.Service };

        public override void Init()
        {
            Instance = this;
            LoadConfig();

            // Listen for config changes
            _configMessageObj = EnvironmentManager.Instance.RegisterReceiver(
                OnConfigurationChanged,
                new MessageIdAndRelatedKindFilter(
                    MessageId.Server.ConfigurationChangedIndication,
                    MyPluginDefinition.ItemKindId));
        }

        public override void Close()
        {
            _closing = true;
            if (_configMessageObj != null)
            {
                EnvironmentManager.Instance.UnRegisterReceiver(_configMessageObj);
                _configMessageObj = null;
            }
        }

        private void LoadConfig()
        {
            // Load items from Configuration.Instance
        }

        private object OnConfigurationChanged(Message message, FQID dest, FQID sender)
        {
            if (!_closing) LoadConfig();
            return null;
        }

        public void HandleAction(FQID targetFqid, BaseEvent triggeringEvent)
        {
            if (_closing) return;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                // Find item by targetFqid.ObjectId, execute action
                // Use triggeringEvent.EventHeader for event data
            });
        }
    }
}
```

Key patterns:
- `Instance` static property for ActionManager to call into
- `volatile bool _closing` for clean shutdown
- `_configLock` for thread-safe config access
- `ThreadPool.QueueUserWorkItem` for async action execution
- Register `ConfigurationChangedIndication` to reload config on changes

**Admin UserControl (WinForms)**

Admin plugins use WinForms UserControls (not WPF). Use the Designer for layout.

```csharp
public partial class MyItemUserControl : UserControl
{
    internal event EventHandler ConfigurationChangedByUser;
    internal event EventHandler DuplicateRequested;

    public string DisplayName => _txtName.Text;

    public void FillContent(Item item) { /* load item.Properties into controls */ }
    public void ClearContent() { /* reset all controls */ }
    public void UpdateItem(Item item) { /* save controls back to item.Properties */ }
    public string ValidateInput() { /* return error string or null if valid */ }
}
```

Property storage uses `item.Properties["Key"] = "value"` (string dictionary).

**Context menu**

The MIP SDK admin tree only supports three context menu commands:
- `ADD` - "Create New..."
- `DELETE` - "Delete..."
- `RENAME` - F2 inline rename

Override `IsContextMenuValid(string command)` in ItemManager to enable/disable these. Custom context menu items are NOT supported. Use buttons in the detail UserControl instead (e.g. Duplicate).

#### Common steps (both plugin types)

**Modify MSCPlugins.sln**

Three changes. Project entry:

```
Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "MyPlugin", "Admin Plugins\MyPlugin\MyPlugin.csproj", "{YOUR-PROJECT-GUID}"
EndProject
```

Platform configuration:

```
{YOUR-PROJECT-GUID}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
{YOUR-PROJECT-GUID}.Debug|Any CPU.Build.0 = Debug|Any CPU
{YOUR-PROJECT-GUID}.Debug|x64.ActiveCfg = Debug|Any CPU
{YOUR-PROJECT-GUID}.Debug|x64.Build.0 = Debug|Any CPU
{YOUR-PROJECT-GUID}.Release|Any CPU.ActiveCfg = Release|Any CPU
{YOUR-PROJECT-GUID}.Release|Any CPU.Build.0 = Release|Any CPU
{YOUR-PROJECT-GUID}.Release|x64.ActiveCfg = Release|Any CPU
{YOUR-PROJECT-GUID}.Release|x64.Build.0 = Release|Any CPU
```

NestedProjects:

```
{YOUR-PROJECT-GUID} = {SOLUTION-FOLDER-GUID}
```

Solution folder GUIDs:
- `{A0000001-...01}` = Smart Client Plugins
- `{A0000002-...02}` = Device Drivers
- `{A0000003-...03}` = Admin Plugins

**Add to `plugins.json`**

All build infrastructure (CI workflow, `build.ps1`, MSI installer) is driven by the central `plugins.json` manifest. Add one entry:

```json
{
  "name": "MyPlugin",
  "displayName": "My Plugin",
  "path": "Admin Plugins/MyPlugin",
  "category": "AdminPlugin",
  "description": "Short description for the MSI installer"
}
```

Required fields:

| Field | Description |
|---|---|
| `name` | Plugin name (used for assembly, staging dir, ZIP name, registry key) |
| `displayName` | Human-readable name shown in the MSI installer |
| `path` | Relative path to the project folder from the repo root |
| `category` | `SmartClient`, `DeviceDriver`, or `AdminPlugin` |
| `description` | One-line description for the MSI installer feature selection |

Optional fields (with defaults):

| Field | Default | Description |
|---|---|---|
| `project` | `{name}.csproj` | Project file name if different from the plugin name |
| `platform` | `AnyCPU` | MSBuild platform (`AnyCPU` or `x64`) |
| `outputPath` | `bin/Release/net48` | Build output path (auto-adjusts to `bin/x64/Release/net48` for x64) |
| `extraProjects` | `[]` | Additional .csproj files to build (e.g. helper projects) |
| `extraStagingDirs` | `[]` | Extra directories to copy into the staging folder |
| `extraStagingFiles` | `[]` | Extra individual files to copy into the staging folder |

This single entry automatically configures:
- GitHub Actions build matrix and release job
- `build.ps1` staging, zipping, and MSI build
- WiX MSI installer features, components, and uninstall cleanup

**Update documentation**

- `README.md` - add your plugin to the Plugins & Drivers table and the Manual install paths table
- `docs/plugins/index.md` - add a plugin card in the correct category section
- `docs/plugins/admin/my-plugin.md` or `docs/plugins/smart-client/my-plugin.md` (or `docs/plugins/device-drivers/my-driver.md`) - the plugin docs page
- Your plugin's `README.md` - document features, configuration, and troubleshooting

**Checklist**

- [ ] Plugin folder with `.csproj`, `plugin.def`, `README.md`, and source files
- [ ] Unique GUIDs generated and used correctly
- [ ] Project added to `MSCPlugins.sln` (project entry + platform config + NestedProjects)
- [ ] Entry added to `plugins.json`
- [ ] `README.md` updated (plugin table + install paths table)
- [ ] `docs/` updated (plugin card + plugin page)
- [ ] Build verified locally with `.\build.ps1`

Admin plugin extra checks:

- [ ] ItemNodes are nested (child in parent's children list, not flat)
- [ ] Event registration (`GetKnownEventGroups/Types/StateGroups`) on the parent (folder) ItemManager
- [ ] ActionManager uses `BaseEvent` not `AnalyticsEvent` in `ExecuteAction`
- [ ] BackgroundPlugin has static `Instance` property for ActionManager access
- [ ] Config change listener registered for auto-reload
- [ ] `HelpPage.html` included as Content with CopyToOutputDirectory=Always

## License

[MIT](LICENSE)

## Disclaimer

These plugins and drivers interact directly with your Milestone installation. Always test in a non-production environment first. Use at your own risk.
