# Adding a New Plugin

Step-by-step guide for Smart Client Plugins and Admin Plugins.

## Prerequisites

- Visual Studio 2022+
- .NET Framework 4.8 SDK
- Milestone XProtect installed (for testing)
- Generate unique GUIDs before starting (`[guid]::NewGuid()` in PowerShell)

---

# Smart Client Plugin

Based on Weather, RDP, and Notepad plugin patterns.

## GUIDs Needed

| GUID | Purpose | Used In |
|------|---------|---------|
| GUID 1 | PluginId | `*Definition.cs` |
| GUID 2 | ViewItemKind | `*Definition.cs` |
| GUID 3 | BackgroundPluginId | `*Definition.cs` |
| GUID 4 | ViewItemPlugin.Id | `*ViewItemPlugin.cs` |
| GUID 5 | Project GUID | `MSCPlugins.sln` |

## Directory Structure

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

## plugin.def

```xml
<plugin>
   <file name="MyPlugin.dll"/>
   <load env="SmartClient"/>
</plugin>
```

## .csproj

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

## PluginDefinition

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

## ViewItemPlugin

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

## ViewItemManager

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

## ViewItemWpfUserControl

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

## PropertiesWpfUserControl

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

## BackgroundPlugin (Smart Client)

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

## launchSettings.json

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

---

# Admin Plugin (Management Client + Event Server)

Based on HttpRequests, CertWatchdog, and Auditor plugin patterns.

## GUIDs Needed

| GUID | Purpose | Used In |
|------|---------|---------|
| GUID 1 | PluginId | `*Definition.cs` |
| GUID 2 | FolderKindId (parent item) | `*Definition.cs`, `ItemNode` |
| GUID 3 | ItemKindId (child item) | `*Definition.cs`, `ItemNode` |
| GUID 4 | BackgroundPluginId | `*Definition.cs` |
| GUID 5 | Project GUID | `MSCPlugins.sln` |
| GUID 6+ | Event/State/Action GUIDs | If using Rules, events, or actions |

## Directory Structure

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

## plugin.def

```xml
<plugin>
   <file name="MyPlugin.dll"/>
   <load env="Service, Administration"/>
</plugin>
```

`Service` = Event Server background plugin, `Administration` = Management Client admin UI.

## .csproj

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

## PluginDefinition (Admin)

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

## ItemManager (Admin - Parent Folder)

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

**Critical: Event registration on the parent ItemManager.** `GetKnownEventGroups`, `GetKnownEventTypes`, `GetKnownStateGroups` must be on the top-level (folder) ItemManager for the Rules engine to discover them.

## ItemManager (Admin - Child Item)

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

## ActionManager (Rules Integration)

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

## BackgroundPlugin (Event Server)

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

## Admin UserControl (WinForms)

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

## Context Menu

The MIP SDK admin tree only supports three context menu commands:
- `ADD` - "Create New..."
- `DELETE` - "Delete..."
- `RENAME` - F2 inline rename

Override `IsContextMenuValid(string command)` in ItemManager to enable/disable these. Custom context menu items are NOT supported. Use buttons in the detail UserControl instead (e.g. Duplicate).

---

# Common Steps (Both Plugin Types)

## Modify MSCPlugins.sln

Three changes:

### Project Entry
```
Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "MyPlugin", "Admin Plugins\MyPlugin\MyPlugin.csproj", "{YOUR-PROJECT-GUID}"
EndProject
```

### Platform Configuration
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

### NestedProjects
```
{YOUR-PROJECT-GUID} = {SOLUTION-FOLDER-GUID}
```

Solution folder GUIDs:
- `{A0000001-...01}` = Smart Client Plugins
- `{A0000002-...02}` = Device Drivers
- `{A0000003-...03}` = Admin Plugins

## Add to plugins.json

```json
{
  "name": "MyPlugin",
  "displayName": "My Plugin",
  "path": "Admin Plugins/MyPlugin",
  "category": "AdminPlugin",
  "description": "Short description for the MSI installer"
}
```

| Field | Description |
|---|---|
| `name` | Plugin name (assembly, staging dir, ZIP, registry key) |
| `displayName` | Human-readable name in MSI installer |
| `path` | Relative path from repo root |
| `category` | `SmartClient`, `DeviceDriver`, or `AdminPlugin` |
| `description` | One-line description for MSI installer feature selection |

Optional: `project`, `platform`, `outputPath`, `extraProjects`, `extraStagingDirs`, `extraStagingFiles`.

## Update Documentation

- `README.md` - add to Plugins & Drivers table and Manual install paths
- `docs/plugins/index.md` - add plugin card
- `docs/plugins/admin/my-plugin.md` or `docs/plugins/smart-client/my-plugin.md` - plugin docs page

## Checklist

- [ ] Plugin folder with `.csproj`, `plugin.def`, source files
- [ ] Unique GUIDs generated and used correctly
- [ ] Project added to `MSCPlugins.sln` (project + platform config + NestedProjects)
- [ ] Entry added to `plugins.json`
- [ ] `README.md` updated
- [ ] `docs/` updated
- [ ] Build verified locally

### Admin Plugin Extra Checks

- [ ] ItemNodes are nested (child in parent's children list, not flat)
- [ ] Event registration (`GetKnownEventGroups/Types/StateGroups`) on the parent (folder) ItemManager
- [ ] ActionManager uses `BaseEvent` not `AnalyticsEvent` in `ExecuteAction`
- [ ] BackgroundPlugin has static `Instance` property for ActionManager access
- [ ] Config change listener registered for auto-reload
- [ ] `HelpPage.html` included as Content with CopyToOutputDirectory=Always
