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
| [Notepad](Smart%20Client%20Plugins/Notepad) | Smart Client | Simple text editor for operator notes in Smart Client view items |
| [Rtmp](Device%20Drivers/Rtmp) | Device Driver | Receive RTMP/RTMPS push streams (H.264) directly into XProtect™ |
| [RTMPStreamer](Admin%20Plugins/RTMPStreamer) | Admin Plugin | Stream XProtect™ cameras to RTMP destinations (YouTube, Twitch, etc.) |
| [SnapReport](Smart%20Client%20Plugins/SnapReport) | Smart Client | Camera snapshot PDF report generator for site surveys and compliance |
| [MonitorRTMPStreamer](Smart%20Client%20Plugins/MonitorRTMPStreamer) | Smart Client | Capture desktop monitors and stream via RTMP |
| [CertWatchdog](Admin%20Plugins/CertWatchdog) | Admin Plugin | Monitor SSL certificate expiry for all XProtect™ HTTPS endpoints |
| [Auditor](Admin%20Plugins/Auditor) | Admin Plugin | Audit user access to recorded video with per-user rules and reason prompts |

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
| Notepad | `C:\Program Files\Milestone\MIPPlugins\Notepad\` |
| RTMPDriver | `C:\Program Files\Milestone\MIPDrivers\RTMPDriver\` |
| RTMPStreamer | `C:\Program Files\Milestone\MIPPlugins\RTMPStreamer\` |
| SnapReport | `C:\Program Files\Milestone\MIPPlugins\SnapReport\` |
| MonitorRTMPStreamer | `C:\Program Files\Milestone\MIPPlugins\MonitorRTMPStreamer\` |
| CertWatchdog | `C:\Program Files\Milestone\MIPPlugins\CertWatchdog\` |
| Auditor | `C:\Program Files\Milestone\MIPPlugins\Auditor\` |

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
│   ├── RDP/                       RDP view item plugin
│   ├── Notepad/                   Notepad view item plugin
│   ├── SnapReport/                Camera snapshot PDF report generator
│   └── MonitorRTMPStreamer/        Desktop monitor RTMP streaming
├── Device Drivers/
│   └── Rtmp/                      RTMP push stream driver
├── Admin Plugins/
│   ├── RTMPStreamer/              RTMP outbound streaming plugin
│   ├── CertWatchdog/             SSL certificate expiry monitoring plugin
│   └── Auditor/                 User access audit plugin
├── MSCPlugins.sln                 Visual Studio solution (all projects)
├── plugins.json                   Central plugin manifest (drives CI, build, installer)
├── Directory.Build.props          Shared MSBuild properties (paths, deploy flags)
├── Directory.Build.targets        Shared build targets (stop/deploy/start cycle)
├── installer/
│   ├── MSCPlugins.nsi             Unified NSIS installer script
│   └── generate-nsi.ps1           Generates NSIS sections from plugins.json
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

Your `.csproj` should declare `PluginName` and the dev-deploy flags it needs. The shared `Directory.Build.props` and `Directory.Build.targets` handle the stop/copy/start cycle automatically:

```xml
<PropertyGroup>
  <PluginName>YourPlugin</PluginName>
  <StopSmartClient>true</StopSmartClient>     <!-- set flags matching your plugin's target environment -->
  <LaunchSmartClient>true</LaunchSmartClient>
</PropertyGroup>
```

For SDK-style projects, also add a `Properties/launchSettings.json` with a debug profile (see existing plugins for examples).

#### 2. Add the project to the solution

Open `MSCPlugins.sln` in Visual Studio, right-click the matching solution folder (Smart Client Plugins, Device Drivers, or Admin Plugins), and add your existing project. Alternatively, edit the `.sln` file directly:

```
Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "YourPlugin", "Smart Client Plugins\YourPlugin\YourPlugin.csproj", "{YOUR-GUID}"
EndProject
```

Make sure to add the project GUID to the `NestedProjects` section so it appears under the correct solution folder, and add platform configuration entries in `ProjectConfigurationPlatforms`.

#### 3. Add the plugin to `plugins.json`

All build infrastructure (CI workflow, `build.ps1`, NSIS installer) is driven by the central `plugins.json` manifest. Add one entry:

```json
{
  "name": "YourPlugin",
  "displayName": "Your Plugin",
  "path": "Smart Client Plugins/YourPlugin",
  "category": "SmartClient",
  "description": "Short description for the NSIS installer"
}
```

**Required fields:**

| Field | Description |
|---|---|
| `name` | Plugin name (used for assembly, staging dir, ZIP name, registry key) |
| `displayName` | Human-readable name shown in the NSIS installer |
| `path` | Relative path to the project folder from the repo root |
| `category` | `SmartClient`, `DeviceDriver`, or `AdminPlugin` |
| `description` | One-line description for the NSIS component selection page |

**Optional fields (with defaults):**

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
- `build.ps1` staging, zipping, and NSIS arguments
- NSIS installer sections, descriptions, ComponentsLeave logic, and uninstall cleanup

#### 4. Update documentation

- **`README.md`** (this file) &mdash; add your plugin to the Plugins & Drivers table and the Manual install paths table
- **`docs/index.html`** &mdash; add a plugin card in the correct category section
- **Your plugin's `README.md`** &mdash; document features, configuration, and troubleshooting

#### Checklist

- [ ] Plugin folder with `.csproj`, `plugin.def`, `README.md`, and source files
- [ ] Project added to `MSCPlugins.sln` (correct solution folder + platform config)
- [ ] Entry added to `plugins.json`
- [ ] `README.md` &mdash; plugin table + install paths table
- [ ] `docs/index.html` &mdash; plugin card in correct category
- [ ] Build verified locally with `.\build.ps1`

## License

[MIT](LICENSE)

## Disclaimer

These plugins and drivers interact directly with your Milestone installation. Always test in a non-production environment first. Use at your own risk.
