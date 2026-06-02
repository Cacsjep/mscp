---
title: "Auto Exporter for Milestone XProtect™"
description: "Auto Exporter is a standalone Milestone XProtect™ application that runs scheduled and rule-triggered video exports (AVI or XProtect database) on the machine that stores them, with size and age limits, managed from the Management Client."
---

<style>
  .download-card {
    background: #0c0c0c;
    border: 1px solid var(--md-default-fg-color--lightest);
    border-radius: 10px;
    padding: 28px;
    margin-bottom: 1.5rem;
  }
  .download-card p { font-size: 0.6rem; }
</style>

<div class="show-title" markdown>

# Auto Exporter

Automate Milestone XProtect™ video exports on a recurring rule or on demand, and keep them on disk within a bounded size and age. Exports are produced by a standalone agent service running on the machine that stores them, and managed from the Management Client.

Auto Exporter is a standalone application with its own installer and release feed, separate from the main MS Community Plugins installer.

## Download

<div class="download-card">
  <small>One MSI with selectable Agent and Management Client plugin features.</small>
  <a id="autoexp-download" href="https://github.com/Cacsjep/mscp-auto-exporter/releases/latest" class="md-button md-button--primary" style="width:100%;text-align:center;font-weight:400">Download MSCPAutoExport-Setup.msi</a>
  <small>Minimum Version: Milestone XProtect™ 2023 R3</small>
</div>

!!! info "Windows SmartScreen"
    The installer is not code-signed, so Windows SmartScreen may show a **"Windows protected your PC"** dialog when you run it. This is normal for unsigned open-source software. Click **More info** and then **Run anyway** to proceed.

## Architecture

Auto Exporter is split into parts so the heavy export work runs on the machine that stores the files, while the configuration and status live where the operator already works (the Management Client). The parts talk to each other over Milestone MessageCommunication on the management server, so they can be on different machines.

### Management Client plugin (the admin UI)

This is the part you see in the Management Client, under **Auto Exporter**. It is where you **define jobs** (which cameras, format, time range), assign each job to an agent, and watch status in the **Agents**, **Jobs**, and **Executions** sections. It also **registers the rule action and events** so jobs can be driven by the Rules engine. It stores jobs as Milestone configuration items but does **not** export anything itself.

### Event Server bridge (the part that runs in the Event Server)

The same plugin DLL also loads inside the **Event Server**, where it runs as a small background bridge. It is needed because the things below can only happen server-side, not in the agent process and not in the Management Client UI:

- **Forwarding rule triggers.** A rule's **Run Auto Export Job** action executes in the Event Server. The bridge turns that trigger into a `RunJob` message addressed to the agent that owns the job.
- **Raising the rule events.** When an agent reports a run starting, succeeding, or failing, the bridge raises the registered **Auto Export: Job Started / Succeeded / Failed** events. A Milestone event source must live in the Event Server, so the agent cannot raise these itself.
- **Server-side configuration writes.** Removing an offline agent (and its jobs) is a write the Management Client is not allowed to make for the agent item kind. The bridge performs it server-side on request.

It does no exporting and holds no heavy state. It is part of the plugin feature and loads automatically wherever the Event Server runs, with no separate install.

### Agent service (the part that exports)

A Windows service installed on **each machine that stores exports**. It is a separate process and usually a separate machine for two reasons: exports are written to a folder on that machine's local disk, and the export pipeline is long-running and CPU and IO heavy, so it must not run inside the shared Event Server. The agent:

- signs in to Milestone with its own credentials (as Local System) and registers itself in the Agents list,
- runs the actual export (AVI or XProtect™ database) when a `RunJob` arrives or on a manual Run now,
- enforces the per-agent size and age limits on its export folder,
- answers a live ping so its Online status and fields are current, and reports progress and the outcome of each run back to the Event Server bridge.

### Tray app (configures the agent)

A small app on the agent machine. It sets the Milestone connection, the export folder, the display name, and the size and age limits, and can start or stop the service. The tray never logs in itself: it saves the configuration, restarts the service, and shows back exactly what the service reports, so there is a single sign-in path owned by the service.

### Installer

The single installer has two selectable features: **Agent service and tray app** (install on each export machine) and **Management Client plugin** (install on the management machine, this also provides the Event Server bridge). You can install both on the same machine.

## Install

1. Download `MSCPAutoExport-vX.X-Setup.msi` from the [Releases](https://github.com/Cacsjep/mscp-auto-exporter/releases) page.
2. Run as **Administrator**.
3. On the feature page, pick **Agent service and tray app**, **Management Client plugin**, or both.

The plugin feature closes the Management Client and restarts the Event Server during install (the plugin DLL is locked while either runs). The agent feature installs the service plus the tray app, with Start menu and Desktop shortcuts and an autostart entry, and offers to launch the tray when the installer finishes.

!!! note "OEM Installations" 
    You need manually copy files to the correct destination after installation, and stop and start the services by yourself, you need todo this only for the managment client / event server plugin.

## Configure the agent (tray app)

The agent signs in to Milestone itself. The tray never logs in: it saves the configuration, restarts the service, and shows back what the service reports.

1. Open the **Auto Exporter Agent** tray app on the export machine.
2. On **Registration**, enter the Management Server address and credentials (Basic user, or a Windows user). The service runs as Local System and signs in with these credentials. Click **Connect**.
3. On **General**, set the **Display name** (optional, the friendly name shown in the Management Client), the **Export folder**, and the **Max size (GB)** and **Retention (days)** limits. A limit of `0` means unlimited.

Once connected, the agent appears under **Auto Exporter, Agents** in the Management Client within a few seconds.

!!! note "Recording server reachability"
    After sign-in the agent checks that it can reach every recording server. If one cannot be reached (often bad DNS, a missing hosts entry, or a firewall), the tray warns about it. Exports for cameras on an unreachable recorder fail until it can be reached.

## Set up a job

1. In the Management Client open **Auto Exporter** and the **Jobs** section (or the **Jobs** tree node).
2. Add a job. Give it a name, pick the **agent** that runs it, choose the format, set the time range, and add the **cameras or camera groups**.
3. Save. A job needs a name, an agent, and at least one camera or group.
4. Use **Run now** to test it, or build a Rule with the **Run Auto Export Job** action. Watch the **Executions** section for the result.

Audio is always included. Microphones and speakers related to the selected cameras are exported alongside the video.

### Export formats

| Format | Result |
|---|---|
| **AVI** (default) | A standard video file per camera that plays in any media player, no Milestone software needed. Does not support encryption. Tick **Burn in timestamp** to draw the recording time onto the frames. |
| **XProtect** | The Milestone database export (a Data folder plus a project file), supports encryption. Open it in a Smart Client to review it. |

!!! warning "The Smart Client Player is not bundled"
    The standalone Smart Client Player (the `SmartClient-Player.exe` you get from an export made inside the Smart Client) is **not** included with an XProtect-format export. Milestone only adds the player when the export runs from within the Smart Client itself, so a standalone service cannot produce it. Use **AVI** if the recipient has no Milestone software, or open the XProtect export in a Smart Client.

### Time range

Each job exports `Last N [Minutes, Hours, Days, or Months]`, anchored at the moment the job runs, so the same job works for a scheduled rule, a manual Run now, or an event-driven rule.

| Run time | Range | Exported window |
|---|---|---|
| 2026-05-28 03:00 | Last 2 Days | 2026-05-26 03:00 to 2026-05-28 03:00 |
| 2026-05-28 14:22 | Last 2 Hours | 2026-05-28 12:22 to 2026-05-28 14:22 |

Months counts as 30 days.

## Where exports go and how long they stay

The export folder, the maximum size, and the retention in days are **per agent**, not per job. They are set in the agent tray app. Each job writes into its own subfolder, and each run gets its own timestamped folder.

```text
<Export folder>\
  <Job name>\
    28.05.2026_0300\
      <Camera name>\
        ...exported files...
    28.05.2026_0400\
      ...
```

Old runs are removed oldest first to stay under the size limit, and runs older than the retention are removed as well.

## Agents section

An agent appears automatically once its service signs in (it registers itself by hostname). You do not add agents by hand. The section shows each agent's friendly name, hostname, status, version, size and age limits, current **Used GB** of the export folder, and last seen.

- Status is driven by a live ping, so it reflects reality without a manual refresh. A new or reconnecting agent shows **Checking...** briefly, then **Online**.
- The **Used GB** value turns orange once the export folder reaches 95% of the agent size limit.
- An offline agent can be removed with **Remove agent**, which also deletes the jobs assigned to it so none point at a missing agent. A live agent cannot be removed because it would just register itself again.

## Executions section

The **Executions** section shows recent runs from every agent (newest first) with the outcome, camera count, size, and detail. A running job shows its live progress (**Running 45%**).

Select a **Pending** or **Running** run and click **Stop run** to cancel it. A queued run is dropped before it starts, an in-progress export is cancelled, and the run is recorded as **Stopped** (it does not raise the Job Failed event).

Each agent keeps its own run history and log files under `%ProgramData%\MSCPlugins\AutoExporter\` on its machine. The tray app can open those logs.

## Rule action and events

The plugin exposes one action, **Run Auto Export Job**, which you point at a specific job. It raises three events you can react to in rules:

- **Auto Export: Job Started** fires when a run begins.
- **Auto Export: Job Succeeded** fires when a run finishes.
- **Auto Export: Job Failed** fires on a hard error.

## Troubleshooting

- **No agent to pick when adding a job.** Start the agent service on the export machine and connect it in the tray app. It appears in Agents within a few seconds.
- **A camera has no recordings in the range.** Cameras with no data in the window are skipped. The run is recorded as Partial when some cameras still exported, or Skipped when none did. This is informational, not an error.
- **AVI plus encryption was rejected when saving.** AVI does not support encryption. Switch to XProtect or turn encryption off.
- **A recording server is reported unreachable.** Check name resolution and the firewall from the agent machine to that recorder.

!!! note "Encryption password storage"
    An export encryption password is stored in the Milestone configuration. Use encryption only where that is acceptable, and restrict configuration database access with Milestone roles.

</div>

<script>
fetch("https://api.github.com/repos/Cacsjep/mscp-auto-exporter/releases/latest")
  .then(function(r) { return r.json(); })
  .then(function(data) {
    var btn = document.getElementById("autoexp-download");
    if (!btn || !data) return;
    var asset = (data.assets || []).find(function(a) {
      return /MSCPAutoExport.*\.msi$/i.test(a.name);
    });
    if (asset) {
      btn.href = asset.browser_download_url;
      btn.textContent = "Download " + asset.name;
    } else if (data.html_url) {
      btn.href = data.html_url;
      btn.textContent = data.tag_name ? ("Go to " + data.tag_name + " Release") : "Browse Releases";
    }
  })
  .catch(function() {});
</script>
