# MSC Community Plugins for Milestone XProtect™

[![Build & Release](../../actions/workflows/build-release.yml/badge.svg)](../../actions/workflows/build-release.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

> [!IMPORTANT]
> This is an independent open source project and is **not affiliated with, endorsed by, or supported by Milestone Systems**. XProtect™ is a trademark of Milestone Systems A/S.

A collection of community-built plugins and drivers for Milestone XProtect™, maintained as a single repository with a unified build and installer.

## Plugins & Drivers

| Name | Category | Description |
|---|---|---|
| [Weather](Smart%20Client%20Plugins/Weather) | Smart Client | Live weather display in Smart Client view items (Open-Meteo) |
| [RDP](Smart%20Client%20Plugins/RDP) | Smart Client | Embedded RDP sessions in Smart Client view items |
| [Rtmp](Device%20Drivers/Rtmp) | Device Driver | Receive RTMP/RTMPS push streams (H.264) directly into XProtect™ |
| [RtmpStreamer](Admin%20Plugins/RtmpStreamer) | Admin Plugin | Stream XProtect™ cameras to RTMP destinations (YouTube, Twitch, etc.) |

## Installation

### Unified Installer (Recommended)

1. Download `MSCPlugins-vX.X-Setup.exe` from the [Releases](../../releases) page
2. Run as **Administrator**
3. Select the plugins and drivers you want to install
4. The installer handles stopping/starting the required Milestone services automatically

### Manual (ZIP)

Individual ZIPs for each plugin/driver are also available on the [Releases](../../releases) page. See each plugin's README for manual installation instructions:

| Plugin | Install Path |
|---|---|
| Weather | `C:\Program Files\Milestone\MIPPlugins\Weather\` |
| RDP | `C:\Program Files\Milestone\MIPPlugins\RDP\` |
| RTMPDriver | `C:\Program Files\Milestone\MIPDrivers\RTMPDriver\` |
| RtmpStreamer | `C:\Program Files\Milestone\MIPPlugins\RtmpStreamer\` |

> [!NOTE]
> Always **unblock** downloaded ZIP files before extracting (right-click -> Properties -> Unblock). Windows marks downloaded files as untrusted and will block the DLLs from loading if you skip this step.

## Requirements

- Milestone XProtect™ (Professional+, Expert, Corporate, or Essential+)
- Smart Client plugins require the **Smart Client**
- Device drivers require the **Recording Server**
- Admin plugins require the **Event Server** and **Management Client**

## Repository Structure

```
mscp/
├── Smart Client Plugins/
│   ├── Weather/                   Weather view item plugin
│   └── RDP/                       RDP view item plugin
├── Device Drivers/
│   └── Rtmp/                      RTMP push stream driver
├── Admin Plugins/
│   └── RtmpStreamer/              RTMP outbound streaming plugin
├── MSCPlugins.sln                 Visual Studio solution (all projects)
├── installer/
│   └── MSCPlugins.nsi             Unified NSIS installer script
├── .github/workflows/
│   └── build-release.yml          CI: matrix build + create release
├── build.ps1                      Local build script
└── README.md
```

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

This builds all plugins in Release configuration, creates per-plugin ZIPs, and optionally a unified NSIS installer (requires [NSIS](https://nsis.sourceforge.io/)). All output goes to the `build/` directory.

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

### Adding a New Plugin or Driver

To add a new plugin to the repository, follow these steps. Use an existing plugin as a reference (e.g., `Smart Client Plugins/Weather/` for a Smart Client plugin).

#### 1. Create the plugin folder

Place your project in the correct category folder:

| Category | Folder | Runs on |
|---|---|---|
| Smart Client Plugin | `Smart Client Plugins/YourPlugin/` | Smart Client |
| Device Driver | `Device Drivers/YourDriver/` | Recording Server |
| Admin Plugin | `Admin Plugins/YourPlugin/` | Event Server / Management Client |

Your folder must include:
- **`.csproj`** &mdash; project file targeting the appropriate .NET Framework version
- **`plugin.def`** &mdash; MIP SDK manifest declaring the DLL and load environment (see below)
- **`README.md`** &mdash; documentation for your plugin (features, configuration, troubleshooting)
- Your source files (`.cs`, `.xaml`, resources, etc.)

Example `plugin.def`:
```xml
<plugin>
   <file name="YourPlugin.dll"/>
   <load env="SmartClient"/>       <!-- or "Service" for drivers, "Service, Administration" for admin plugins -->
</plugin>
```

#### 2. Add the project to the solution

Open `MSCPlugins.sln` in Visual Studio, right-click the matching solution folder (Smart Client Plugins, Device Drivers, or Admin Plugins), and add your existing project. Alternatively, edit the `.sln` file directly:

```
Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "YourPlugin", "Smart Client Plugins\YourPlugin\YourPlugin.csproj", "{YOUR-GUID}"
EndProject
```

Make sure to add the project GUID to the `NestedProjects` section so it appears under the correct solution folder, and add platform configuration entries in `ProjectConfigurationPlatforms`.

#### 3. Update `build.ps1`

Add a staging block to copy your build output (after the existing plugins, in step 4):

```powershell
# YourPlugin
$stageYour = Join-Path $staging 'YourPlugin'
New-Item -ItemType Directory -Path $stageYour -Force | Out-Null
Copy-Item -Path (Join-Path $root 'Smart Client Plugins\YourPlugin\bin\Release\net48\*') -Destination $stageYour -Recurse
```

Add `'YourPlugin'` to the `$artifacts` array so a ZIP is created:

```powershell
$artifacts = @('Weather', 'RDP', 'RTMPDriver', 'RtmpStreamer', 'YourPlugin')
```

Update the NSIS variables section to pass your staging directory:

```powershell
$yourDir = (Resolve-Path (Join-Path $staging 'YourPlugin')).Path

& $makensis ... `
    "/DYOURPLUGIN_DIR=$yourDir" `
    ...
```

#### 4. Update `.github/workflows/build-release.yml`

Add a new matrix entry in the `build` job:

```yaml
- name: YourPlugin
  solution: MSCPlugins.sln
  platform: Any CPU
  msbuild_targets: YourPlugin
  stage_from: Smart Client Plugins\YourPlugin\bin\Release\net48
  stage_to: YourPlugin
  extra_flags: /p:CIBuild=true
```

In the `release` job, add your plugin name to the extract loop and the collect/release file lists:

```yaml
# Extract staging directories
foreach ($name in @('Weather', 'RDP', 'RTMPDriver', 'RtmpStreamer', 'YourPlugin')) {

# Collect release files
Copy-Item artifacts\YourPlugin\*.zip -Destination .

# Create GitHub Release → files
YourPlugin-${{ github.ref_name }}.zip
```

#### 5. Update `installer/MSCPlugins.nsi`

Add a staging directory define:

```nsi
!ifndef YOURPLUGIN_DIR
  !define YOURPLUGIN_DIR "..\build\staging\YourPlugin"
!endif
```

Add an install section inside the appropriate `SectionGroup`:

```nsi
Section "Your Plugin" SEC_YOURPLUGIN
  DetailPrint "Closing Smart Client..."
  nsExec::ExecToLog 'taskkill /F /IM "${SC_PROCESS}"'
  Pop $0
  Sleep 2000

  SetOutPath "$INSTDIR\MIPPlugins\YourPlugin"
  DetailPrint "Installing Your Plugin..."
  File /r "${YOURPLUGIN_DIR}\*.*"

  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\YourPlugin" \
    "DisplayName" "YourPlugin v${VERSION}"
  ; ... (registry entries, see existing sections for the pattern)
SectionEnd
```

Add a description, uninstall cleanup, and registry removal (see existing entries as a template).

#### 6. Update documentation

- **`README.md`** (this file) &mdash; add your plugin to the Plugins & Drivers table and the Manual install paths table
- **`docs/index.html`** &mdash; add a plugin card in the correct category section
- **Your plugin's `README.md`** &mdash; document features, configuration, and troubleshooting

#### Checklist

- [ ] Plugin folder with `.csproj`, `plugin.def`, `README.md`, and source files
- [ ] Project added to `MSCPlugins.sln` (correct solution folder + platform config)
- [ ] `build.ps1` &mdash; staging block, `$artifacts` array, NSIS variable
- [ ] `.github/workflows/build-release.yml` &mdash; matrix entry, extract/collect/release lists
- [ ] `installer/MSCPlugins.nsi` &mdash; directory define, install section, description, uninstall cleanup
- [ ] `README.md` &mdash; plugin table + install paths table
- [ ] `docs/index.html` &mdash; plugin card in correct category
- [ ] Build verified locally with `.\build.ps1`

## License

[MIT](LICENSE)

## Disclaimer

These plugins and drivers interact directly with your Milestone installation. Always test in a non-production environment first. Use at your own risk.
