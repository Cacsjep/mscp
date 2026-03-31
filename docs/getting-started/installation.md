---
title: "Download and Install Milestone XProtect Plugins"
description: "Download and install free community plugins for Milestone XProtect. System requirements, setup instructions, and MSI installer."
hide:
  - navigation
---

<style>
  .download-grid {
    display: grid;
    grid-template-columns: 1fr 1fr;
    gap: 24px;
    margin-bottom: 1.5rem;
  }
  @media (max-width: 700px) {
    .download-grid { grid-template-columns: 1fr; }
  }
  .download-card {
    background: #0c0c0c;
    border: 1px solid var(--md-default-fg-color--lightest);
    border-radius: 10px;
    padding: 28px;
  }
  .download-card h3 { margin-top: 0; }
  .download-card .rec {
    display: inline-block;
    background: rgba(63, 185, 80, 0.15);
    color: #3fb950;
    font-size: 0.6rem;
    font-weight: 600;
    text-transform: uppercase;
    padding: 2px 8px;
    border-radius: 4px;
    margin-left: 8px;
    vertical-align: middle;
    margin-top: -3px;
  }
  .download-card p {
    font-size: 0.6rem;
  }
</style>

<div class="download-grid">
  <div class="download-card">
    <h3>Unified Installer <span class="rec">Recommended</span></h3>
    <p>Single installer with component selection.</p>
    <a id="installer-download" href="https://github.com/Cacsjep/mscp/releases/latest" class="md-button md-button--primary" style="width:100%;text-align:center;font-weight:400">Download MSCPlugins-Setup.msi</a>
    <small>Choose exactly which plugins and drivers to install.</small>
  </div>
  <div class="download-card">
    <h3>Individual ZIPs</h3>
    <p>Download only the plugin you need.</p>
    <a href="https://github.com/Cacsjep/mscp/releases/latest" class="md-button" style="width:100%;text-align:center;font-weight:400">Browse Releases</a>
    <small>Extract to the correct Milestone directory and restart the relevant service.</small>
  </div>
</div>

!!! warning "Minimum Version"
    All plugins and drivers require **Milestone XProtect 2023 R3** or newer.

!!! info "Windows SmartScreen"
    The installer is not code-signed, so Windows SmartScreen may show a **"Windows protected your PC"** dialog when you run it. This is normal for unsigned open-source software. Click **More info** and then **Run anyway** to proceed with the installation.

---

## Unified Installer

1. Download `MSCPlugins-vX.X-Setup.msi` from the [Releases](https://github.com/Cacsjep/mscp/releases) page
2. Run as **Administrator**
3. Select the plugins and drivers you want to install

!!! note
    The installer handles stopping/starting the required Milestone services automatically
---

## Manual Installation (ZIP)

Individual ZIPs for each plugin/driver are available on the [Releases](https://github.com/Cacsjep/mscp/releases) page.

!!! warning "Unblock ZIPs first"
    Always **unblock** downloaded ZIP files before extracting: right-click the `.zip` → Properties → Unblock → OK. Windows marks downloaded files as untrusted and will prevent the DLLs from loading.


### Install Paths

| Plugin | Install Path |
|---|---|
| Weather | `C:\Program Files\Milestone\MIPPlugins\Weather\` |
| RDP | `C:\Program Files\Milestone\MIPPlugins\RDP\` |
| Notepad | `C:\Program Files\Milestone\MIPPlugins\Notepad\` |
| SnapReport | `C:\Program Files\Milestone\MIPPlugins\SnapReport\` |
| MonitorRTMPStreamer | `C:\Program Files\Milestone\MIPPlugins\MonitorRTMPStreamer\` |
| RTMPDriver | `C:\Program Files\Milestone\MIPDrivers\RTMPDriver\` |
| RTMPStreamer | `C:\Program Files\Milestone\MIPPlugins\RTMPStreamer\` |
| CertWatchdog | `C:\Program Files\Milestone\MIPPlugins\CertWatchdog\` |
| Auditor | `C:\Program Files\Milestone\MIPPlugins\Auditor\` |
| SmartBar | `C:\Program Files\Milestone\MIPPlugins\SmartBar\` |

### Services 

After manual installation, restart the relevant service, if you update, you need to stop the services before.

| Plugin Type | Restart |
|---|---|
| Smart Client Plugins | Restart the **Smart Client** |
| Device Drivers | Restart the **Recording Server** service |
| Admin Plugins | Restart the **Event Server** service, then the **Management Client** and **Smart Client** |

<script>
fetch("https://api.github.com/repos/Cacsjep/mscp/releases/latest")
  .then(function(r) { return r.json(); })
  .then(function(data) {
    var btn = document.getElementById("installer-download");
    if (!btn) return;
    var asset = data.assets.find(function(a) {
      return a.name.match(/MSCPlugins.*\.msi$/i);
    });
    if (asset) {
      btn.href = asset.browser_download_url;
      btn.textContent = "Download " + asset.name;
    } else {
      btn.href = data.html_url;
      btn.textContent = "Go to " + data.tag_name + " Release";
    }
  });
</script>
