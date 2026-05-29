---
title: "Auto Exporter Plugin for Milestone XProtect"
description: "Auto Exporter plugin for Milestone XProtect. Schedule recurring XProtect or AVI exports as a rule action, with per-job ring-buffer storage, manual Run Now, distinct run outcomes, and live progress in the Event Server context."
---

<div class="show-title" markdown>

# Auto Exporter

XProtect has no native automated export. This plugin adds it. You configure jobs (cameras, time range, format, encryption, storage), trigger them from the Rules engine or manually, and keep the results on disk with a per-job ring buffer.

## Where jobs run

Exports run on the **Event Server**, not on the Management Client you configure from. Everything a job touches is resolved on the Event Server:

- The **storage path** is read on the Event Server, so it must be a folder the **Event Server service account** can write to (a local path on the Event Server machine, or a UNC share that account can reach).
- Type the path as the Event Server sees it, then use **Verify on ES** to confirm it is reachable and writable there. A path that exists on your client machine is meaningless unless the Event Server can also reach it. User-profile folders (Desktop, Documents) usually fail because the service account cannot access them.

## Quick Start

1. Open the **Management Client**.
2. Navigate to the **Auto Exporter** node in the sidebar.
3. Right-click **Jobs** and pick Create New for each job. Configure name, cameras, format, time range, storage path, and MAX GB / MAX TIME. Use **Verify on ES** to confirm the Event Server can reach the path.
4. Open **Status and Executions** and click **Run Now** to test, or build a Rule using **Execute Auto Export Job** as the action.

## Tree layout

```
Auto Exporter
├── Status and Executions  (one page, two tables: jobs storage status on the left, run history on the right)
└── Jobs                   (category, right-click to Create New for each job)
      ├── Nightly Lobby
      └── Weekly Parking
```

## Job configuration

| Property | Description |
|---|---|
| **Format** | XProtect (database, optional AES-128 encryption, Smart Client Player) or AVI |
| **Encrypt and Password** | XProtect only. The password is stored in plaintext in the Milestone configuration database (see the warning below) |
| **Time range** | Last N Minutes, Hours, Days, or Months, anchored at trigger time |
| **Cameras and camera groups** | Any mix. Groups are resolved to their member cameras at trigger time |
| **Include Smart Client Player** | XProtect format only |
| **Include audio** | Microphones and speakers related to each camera |
| **Storage path** | Per job. A local folder on the Event Server, or a UNC path the service account can reach. Must be unique per job |
| **MAX GB / MAX TIME** | Per-job ring-buffer limits (0 means unlimited) |
| **Enabled** | Per-job enable or disable |

Each storage path must belong to one job. Saving a job whose path is already used by another job is rejected, so jobs never mix their exports or fight over cleanup.

## Storage layout (per job)

```
<Job.StoragePath>\
  28.05.2026_0300\
    <CameraName>\
      ...exported files...
  28.05.2026_0400\
    ...
```

For AVI, each camera gets its own file. Milestone splits AVI output into segments (name.avi, name_0001.avi, and so on) at 512 MB, so a large single-camera export produces several numbered files that play in sequence. This is normal AVI behavior.

## Ring buffer

- Each job's MAX GB and MAX TIME apply to its own storage path only.
- Cleanup runs before each export and once an hour for every configured job.
- Oldest run folders are deleted first until the path is under MAX GB. Folders older than MAX TIME are deleted on every pass.
- A value of 0 means unlimited.

## Run history and outcomes

The run history lives in one file on the Event Server and is shown in the **Status and Executions** page (fetched over messaging, so it also works from a remote Management Client):

```
%ProgramData%\MSCPlugins\AutoExporter\executions.jsonl
```

Each run is recorded with a distinct outcome:

- **Success**. Everything requested was exported.
- **Partial**. Some cameras exported, but one or more had no recordings in the range and were left out. The skipped camera names are shown in the detail column.
- **Skipped**. Nothing was exported, either because no camera had recordings in the range, or because a trigger fired while the previous run was still active.
- **Failed**. The export errored (for example disk full or no storage path).

A camera with no recordings in the range is not an error. Milestone's exporter would otherwise abort the whole run for such a camera, so the plugin probes each camera first and quietly drops the empty ones, recording the result as Partial or Skipped and listing the affected cameras.

Retention is enforced automatically on every append: up to 100 entries per job and up to 1000 entries total. The view shows at most the most recent 500. Use **Clear list** to wipe the history.

## Live progress

A running export appears at the top of the history as a **Running** row that updates in place, then flips to its final outcome when it finishes. There is no separate progress bar.

- **XProtect** runs show the overall export percent.
- **AVI** runs show the current camera and its percent (for example "camera 3 of 44, 47 percent"), because AVI is exported one camera at a time.

While a run is in progress, **Run Now** is disabled so a second run cannot be started on top of the first.

## Rule events and action

The plugin registers one action, **Execute Auto Export Job**. The Rules engine offers individual, folder, or ALL targeting natively. Three events fire so you can build rules on them:

- **Auto Export: Job Started**. Fires when a run begins.
- **Auto Export: Job Succeeded**. Fires when a run finishes successfully.
- **Auto Export: Job Failed**. Fires on a hard error such as disk full or missing cameras.

A run that is skipped because the previous run is still active is recorded in the history as **Skipped**. It does not fire the Failed event, because it is normal back-pressure rather than a failure.

## Permissions

The Jobs node and the Status and Executions page expose **Read** and **Edit** permissions under Security, Roles, the role, MIP, Auto Exporter. Read controls who can see jobs and history. Edit controls who can create, modify, or delete jobs.

## Security warning, plaintext password

!!! warning "Job export passwords are stored as plaintext"
    Export passwords are stored as plaintext in the Milestone configuration database. The Management Client encrypts traffic to the server, but anyone with read access to the management database can extract the passwords.

    Use export encryption only for non-sensitive exports, or where database access is tightly restricted. If you need stronger protection, gate database access through Milestone role permissions and disable encryption (the export is then a plain XProtect bundle).

## Troubleshooting

| Symptom | Likely cause |
|---|---|
| Verify on ES reports "not found" for a path that exists | The path is interpreted by the **Event Server service account**, which cannot see user-profile folders such as Desktop. Use a folder the service account can write to, or a UNC share it can reach. |
| Saving a job is rejected as a duplicate storage path | Another job already uses that folder. Give each job its own path. |
| A run shows Partial or Skipped | One or more cameras had no recordings in the range. The detail column lists them. This is informational, not an error. |
| AVI export produced several numbered files | AVI auto-splits at 512 MB into name.avi, name_0001.avi, and so on. The segments play in sequence. |
| AVI + Encrypt rejected at save | AVI does not support encryption. Switch to XProtect or disable encryption. |
</div>
