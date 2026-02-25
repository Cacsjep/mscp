# Adding a New Smart Client Plugin

Step-by-step guide based on the Weather, RDP, and Notepad plugin patterns.

## Prerequisites

- Visual Studio 2022+
- .NET Framework 4.8 SDK
- Milestone XProtect Smart Client installed (for testing)
- Generate **4 unique GUIDs** before starting (use `[guid]::NewGuid()` in PowerShell)

| GUID | Purpose | Used In |
|------|---------|---------|
| GUID 1 | PluginId | `*Definition.cs` |
| GUID 2 | ViewItemKind | `*Definition.cs` |
| GUID 3 | BackgroundPluginId | `*Definition.cs` |
| GUID 4 | ViewItemPlugin.Id | `*ViewItemPlugin.cs` |
| GUID 5 | Project GUID | `MSCPlugins.sln` |

---

## 1. Create the Plugin Directory

```
Smart Client Plugins/
  MyPlugin/
    MyPlugin.csproj
    plugin.def
    MyPluginDefinition.cs
    README.md
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

For Device Drivers, use `Device Drivers/MyDriver/`. For Admin Plugins, use `Admin Plugins/MyPlugin/`.

---

## 2. Source Files

### plugin.def

```xml
<plugin>
   <file name="MyPlugin.dll"/>
   <load env="SmartClient"/>
</plugin>
```

The `env` value depends on category:
- Smart Client Plugin: `SmartClient`
- Device Driver: `Service`
- Admin Plugin: `Service, Administration`

### MyPlugin.csproj

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net48</TargetFramework>
    <UseWPF>true</UseWPF>
    <OutputType>Library</OutputType>
    <RootNamespace>MyPlugin</RootNamespace>
    <AssemblyName>MyPlugin</AssemblyName>
    <LangVersion>latest</LangVersion>
    <MIPPluginDir>C:\Program Files\Milestone\MIPPlugins\MyPlugin\</MIPPluginDir>
    <SmartClientExe>C:\Program Files\Milestone\XProtect Smart Client\Client.exe</SmartClientExe>
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

  <!-- Pre-build: kill Smart Client so DLL files are not locked (skipped in CI) -->
  <Target Name="PreBuild" BeforeTargets="PreBuildEvent" Condition="'$(CIBuild)' != 'true'">
    <Exec Command="taskkill /F /IM Client.exe 2&gt;nul &amp; ping -n 3 127.0.0.1 &gt;nul" IgnoreExitCode="true" />
  </Target>

  <!-- Post-build: copy output to MIPPlugins and launch Smart Client (skipped in CI) -->
  <Target Name="PostBuild" AfterTargets="PostBuildEvent" Condition="'$(CIBuild)' != 'true'">
    <Exec Command="if not exist &quot;$(MIPPluginDir)&quot; mkdir &quot;$(MIPPluginDir)&quot;" />
    <Exec Command="xcopy /Y /E &quot;$(TargetDir)*.*&quot; &quot;$(MIPPluginDir)&quot;" />
    <Exec Command="start &quot;&quot; &quot;$(SmartClientExe)&quot;" />
  </Target>

</Project>
```

Key points:
- Always `net48` with `UseWPF=true`
- MIP SDK NuGet uses wildcard prerelease: `Version="*-*"`
- `plugin.def` must be `CopyToOutputDirectory=Always`
- Plugin icon is `<Resource>` (not `Content`)
- Pre/Post build targets use `$(CIBuild)` condition to skip in CI

### MyPluginDefinition.cs

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
        public override string VersionString => "1.0.0.0";

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

### Client/MyPluginViewItemPlugin.cs

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
        {
            return new MyPluginViewItemManager();
        }

        public override void Init() { }
        public override void Close() { }
    }
}
```

Gets its own unique GUID (4th one). `Name` is the short display name without "Plugin" suffix.

### Client/MyPluginViewItemManager.cs

```csharp
using VideoOS.Platform.Client;

namespace MyPlugin.Client
{
    public class MyPluginViewItemManager : ViewItemManager
    {
        // Property keys
        private const string SomePropertyKey = "SomeProperty";

        public MyPluginViewItemManager()
            : base("MyPluginViewItemManager")
        {
        }

        // Properties use GetProperty/SetProperty for XProtect storage
        public string SomeProperty
        {
            get => GetProperty(SomePropertyKey) ?? string.Empty;
            set => SetProperty(SomePropertyKey, value);
        }

        public void Save()
        {
            SaveProperties();
        }

        public override void PropertiesLoaded() { }

        public override ViewItemWpfUserControl GenerateViewItemWpfUserControl()
        {
            return new MyPluginViewItemWpfUserControl(this);
        }

        public override PropertiesWpfUserControl GeneratePropertiesWpfUserControl()
        {
            return new MyPluginPropertiesWpfUserControl(this);
        }
    }
}
```

**Property storage pattern:**
- `private const string` key for each property
- Getter: `GetProperty(key) ?? defaultValue`
- Setter: `SetProperty(key, value)`
- String defaults: `string.Empty` for text, `"14"` for numbers
- `Save()` wraps `SaveProperties()`
- Constructor base call passes class name: `base("MyPluginViewItemManager")`

### Client/MyPluginViewItemWpfUserControl.xaml

```xml
<external:ViewItemWpfUserControl
    xmlns:external="clr-namespace:VideoOS.Platform.Client;assembly=VideoOS.Platform"
    x:Class="MyPlugin.Client.MyPluginViewItemWpfUserControl"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
    xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
    mc:Ignorable="d"
    d:DesignHeight="400"
    d:DesignWidth="600"
    PreviewMouseLeftButtonUp="OnMouseLeftUp"
    PreviewMouseDoubleClick="OnMouseDoubleClick">
    <Grid Background="#FF1C2326">
        <!-- Your Live mode UI here -->

        <!-- Setup mode overlay (shown in ClientSetup mode) -->
        <Grid x:Name="setupOverlay" Visibility="Collapsed">
            <!-- Setup info -->
        </Grid>
    </Grid>
</external:ViewItemWpfUserControl>
```

### Client/MyPluginViewItemWpfUserControl.xaml.cs

```csharp
using System;
using System.Windows;
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
            // Register for mode changes (Setup <-> Live)
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
            if (mode == Mode.ClientSetup)
            {
                // Show setup overlay, hide live UI
            }
            else
            {
                // Show live UI, hide setup overlay
                // Load properties from manager
            }
        }

        private object OnModeChanged(Message message, FQID destination, FQID sender)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                ApplyMode((Mode)message.Data);
            }));
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
- Use `Dispatcher.BeginInvoke` for mode change handler (comes from non-UI thread)
- `ApplyMode()` switches between Setup and Live UI
- `FireClickEvent()` and `FireDoubleClickEvent()` for Smart Client selection

### Client/MyPluginPropertiesWpfUserControl.xaml

```xml
<external:PropertiesWpfUserControl
    xmlns:external="clr-namespace:VideoOS.Platform.Client;assembly=VideoOS.Platform"
    x:Class="MyPlugin.Client.MyPluginPropertiesWpfUserControl"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <StackPanel Margin="10">
        <GroupBox Header="MyPlugin Settings" Foreground="White" Margin="0,0,0,10">
            <StackPanel Margin="8">
                <TextBlock Text="Some Property:" Foreground="White" Margin="0,0,0,4" />
                <TextBox x:Name="somePropertyBox" Margin="0,0,0,8" />
            </StackPanel>
        </GroupBox>
    </StackPanel>
</external:PropertiesWpfUserControl>
```

### Client/MyPluginPropertiesWpfUserControl.xaml.cs

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
            // Load current values from manager
            somePropertyBox.Text = _viewItemManager.SomeProperty;
        }

        public override void Close()
        {
            // Save values back to manager
            _viewItemManager.SomeProperty = somePropertyBox.Text;
            _viewItemManager.Save();
        }
    }
}
```

`Init()` loads from manager, `Close()` saves back. Validation goes in `Close()`.

### Background/MyPluginBackgroundPlugin.cs

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

        public override void Init()
        {
            EnvironmentManager.Instance.Log(false, nameof(MyPluginBackgroundPlugin),
                "MyPlugin plugin started.");
        }

        public override void Close()
        {
            EnvironmentManager.Instance.Log(false, nameof(MyPluginBackgroundPlugin),
                "MyPlugin plugin stopped.");
        }
    }
}
```

### Properties/launchSettings.json

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

## 3. Modify MSCPlugins.sln

Three changes needed:

### Project Entry

Add after the last `EndProject` before `Global`:

```
Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "MyPlugin", "Smart Client Plugins\MyPlugin\MyPlugin.csproj", "{YOUR-PROJECT-GUID}"
EndProject
```

### Platform Configuration

Add inside `ProjectConfigurationPlatforms`:

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

Note: For SDK-style Any CPU projects, the `x64` solution platform maps to `Any CPU` project platform.

### NestedProjects

Add inside `NestedProjects`:

```
{YOUR-PROJECT-GUID} = {A0000001-0000-0000-0000-000000000001}
```

Solution folder GUIDs:
- `{A0000001-...01}` = Smart Client Plugins
- `{A0000002-...02}` = Device Drivers
- `{A0000003-...03}` = Admin Plugins

---

## 4. Modify build.ps1

Three changes:

### Staging Block

Add after the last staging block (before the ZIP creation loop):

```powershell
# MyPlugin
$stageMyPlugin = Join-Path $staging 'MyPlugin'
New-Item -ItemType Directory -Path $stageMyPlugin -Force | Out-Null
Copy-Item -Path (Join-Path $root 'Smart Client Plugins\MyPlugin\bin\Release\net48\*') -Destination $stageMyPlugin -Recurse
```

### Artifacts Array

Add `'MyPlugin'` to the `$artifacts` array:

```powershell
$artifacts = @('Weather', 'RDP', 'Notepad', 'RTMPDriver', 'RTMPStreamer', 'MyPlugin')
```

### NSIS Variable

Add resolve + `/D` flag to the makensis call:

```powershell
$mypluginDir = (Resolve-Path (Join-Path $staging 'MyPlugin')).Path

& $makensis ... `
    "/DMYPLUGIN_DIR=$mypluginDir" `
    ...
```

---

## 5. Modify .github/workflows/build-release.yml

Five changes:

### Matrix Entry

```yaml
- name: MyPlugin
  projects: Smart Client Plugins\MyPlugin\MyPlugin.csproj
  platform: AnyCPU
  stage_from: Smart Client Plugins\MyPlugin\bin\Release\net48
  stage_to: MyPlugin
  extra_flags: /p:CIBuild=true
```

Note: `platform` is `AnyCPU` (no space) in the workflow, but `Any CPU` (with space) in the solution file.

### Extract Loop

Add `'MyPlugin'` to the `foreach` array:

```powershell
foreach ($name in @('Weather', 'RDP', 'Notepad', 'RTMPDriver', 'RTMPStreamer', 'MyPlugin')) {
```

### NSIS Build Step

Add resolve + `/D` flag:

```powershell
$mypluginDir = (Resolve-Path 'staging\MyPlugin').Path
# Add to makensis call:
"/DMYPLUGIN_DIR=$mypluginDir" `
```

### Collect Release Files

```yaml
Copy-Item artifacts\MyPlugin\*.zip -Destination .
```

### Release Files List

```yaml
MyPlugin-${{ github.ref_name }}.zip
```

---

## 6. Modify installer/MSCPlugins.nsi

Six changes:

### Staging Directory Define

```nsi
!ifndef MYPLUGIN_DIR
  !define MYPLUGIN_DIR "..\build\staging\MyPlugin"
!endif
```

### Install Section

Add inside the appropriate `SectionGroup` (e.g., Smart Client Plugins):

```nsi
Section "MyPlugin Plugin" SEC_MYPLUGIN
  SetOutPath "$INSTDIR\MIPPlugins\MyPlugin"
  !insertmacro _LogMsg "Installing MyPlugin Plugin..."
  File /r "${MYPLUGIN_DIR}\*.*"

  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\MyPlugin" \
    "DisplayName" "MyPlugin Plugin v${VERSION}"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\MyPlugin" \
    "UninstallString" "$\"$INSTDIR\MIPPlugins\MyPlugin\Uninstall.exe$\""
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\MyPlugin" \
    "DisplayVersion" "${VERSION}"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\MyPlugin" \
    "Publisher" "MSC Community Plugins"
  WriteRegDWORD HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\MyPlugin" \
    "NoModify" 1
  WriteRegDWORD HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\MyPlugin" \
    "NoRepair" 1
SectionEnd
```

Install path convention:
- Smart Client plugins: `$INSTDIR\MIPPlugins\{Name}`
- Device drivers: `$INSTDIR\MIPDrivers\{Name}`

### ComponentsLeave

Add to the Smart Client check block:

```nsi
${OrIf} ${SectionIsSelected} ${SEC_MYPLUGIN}
```

### Uninstall - Remove Directory

```nsi
RMDir /r "$INSTDIR\MIPPlugins\MyPlugin"
```

### Uninstall - Remove Registry

```nsi
DeleteRegKey HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\MyPlugin"
```

### Description Text

```nsi
!insertmacro MUI_DESCRIPTION_TEXT ${SEC_MYPLUGIN} "Description for the installer UI"
```

---

## 7. Update Documentation

### README.md

Add to the Plugins & Drivers table:

```markdown
| [MyPlugin](Smart%20Client%20Plugins/MyPlugin) | Smart Client | Description |
```

Add to the Manual install paths table:

```markdown
| MyPlugin | `C:\Program Files\Milestone\MIPPlugins\MyPlugin\` |
```

### docs/index.html

- Add a plugin card row in the Smart Client Plugins section

### Plugin README.md

Create `Smart Client Plugins/MyPlugin/README.md` following the Weather/RDP/Notepad pattern (Quick Start, Installation, Configuration table, Features).

---

## 8. Checklist

- [ ] Plugin folder with `.csproj`, `plugin.def`, source files, `README.md`
- [ ] 4 unique GUIDs generated and used correctly
- [ ] Project added to `MSCPlugins.sln` (project entry + platform config + NestedProjects)
- [ ] `build.ps1` updated (staging block + `$artifacts` array + NSIS variable)
- [ ] `build-release.yml` updated (matrix entry + extract loop + NSIS build + collect + release files)
- [ ] `installer/MSCPlugins.nsi` updated (define + section + ComponentsLeave + uninstall + description)
- [ ] `README.md` updated (plugin table + install paths)
- [ ] `docs/index.html` updated (plugin card + zipMap)
- [ ] Build verified locally with `.\build.ps1`
