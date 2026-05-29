---
title: "Auto Exporter Plugin for Milestone XProtect"
description: "Auto Exporter plugin for Milestone XProtect — schedule recurring XProtect / AVI exports as a rule action with per-job ring-buffer storage, manual Run Now, and live progress."
---

<div class="show-title" markdown>

# Auto Exporter

XProtect has no native automated export. This plugin adds it: configure jobs (cameras, time-range, format, encryption, storage), trigger them from the Rules engine (or manually), and keep the results on disk with a per-job ring buffer.

## Quick Start

1. Open the **Management Client**
2. Navigate to the **Auto Exporter** node in the sidebar
3. Right-click **Jobs** → Create New… for each job. Configure name, cameras, format, time range, storage path, and MAX GB / MAX TIME. Click **Verify on ES** next to Browse to confirm the Event Server can reach the path.
4. Open **Executions** → click **Run Now ▼** to test, or build a Rule using **Execute Auto Export Job** as the action.
5. Open **Status** for a live per-job dashboard probed from the Event Server.

## Tree layout

```
Auto Exporter
├── Status      (singleton — per-job storage dashboard: health, usage, free disk)
├── Executions  (singleton — run history, Run Now, live progress)
└── Jobs        (category — right-click to Create New… each job)
      ├── Nightly Lobby
      └── Weekly Parking
```

## Job configuration

| Property | Description |
|---|---|
| **Format** | XProtect (database, optional AES-128 encryption, Smart Client Player) or AVI |
| **Encrypt + Password** | XProtect only. **⚠ Password is stored in plaintext** in the Milestone configuration database — see warning below |
| **Time range** | `Last N [Minutes / Hours / Days / Months]`, anchored at trigger time |
| **Cameras / Camera Groups** | Any mix — groups are resolved to their member cameras at trigger time |
| **Include Smart Client Player** | XProtect format only |
| **Include audio** | Microphones/Speakers related to each camera |
| **Storage path** | Per-job: local folder or UNC path |
| **MAX GB / MAX TIME** | Per-job ring-buffer limits (`0` = unlimited) |
| **Enabled** | Per-job enable/disable |

## Storage layout (per job)

```
<Job.StoragePath>\
  28.05.2026_0300\
    <CameraName>\
      ...exported files...
  28.05.2026_0400\
    ...
```

Each job owns its storage path. Two jobs that point at the same path share the same ring buffer (their runs prune together).

## Ring buffer

- Each job's MAX GB / MAX TIME apply to its own storage path only.
- Cleanup runs **before each export** and once an hour for every configured job.
- Oldest run folders are deleted first until under MAX GB; folders older than MAX TIME are deleted on every pass.
- `0` = unlimited.

## Execution log

The Executions view reads from one shared file (independent of any job's storage):

```
%ProgramData%\MSCPlugins\AutoExporter\executions.jsonl
```

Retention is enforced automatically on every append:

- Up to **100 entries per job** (oldest dropped first within each job)
- Up to **1000 entries total** across all jobs (newest globally kept)

The Executions view shows at most the most recent 500. No manual maintenance.

## Rule action

The plugin registers one action: **Execute Auto Export Job**. The Rules engine offers individual / folder / ALL targeting natively. Two events fire on completion:

- **Auto Export: Job Succeeded** — chain to Notification, HTTP Requests, etc.
- **Auto Export: Job Failed** — fires on disk-full, missing cameras, "job busy", etc.

## Concurrency

If a rule re-triggers a job while the previous run is still active, the new trigger is **skipped**, an entry is added to the Executions log, and the Failed event is fired. This prevents disk/IO pile-ups.

## Security warning — plaintext password

> ⚠ **Job export passwords are stored as PLAINTEXT in the Milestone configuration database.**
> The Management Client encrypts traffic to the server, but anyone with read access to the management DB can extract the passwords.
>
> Use export encryption only for non-sensitive exports or where database access is tightly restricted. If you need stronger protection, gate DB access via Milestone role permissions and disable encryption (the export will be a plain XProtect bundle).

## Troubleshooting

| Symptom | Likely cause |
|---|---|
| "Storage path not configured on this job" | Open the job and pick a path that the **Event Server service account** can write to. UNC paths must be reachable as the service account, not the interactive user. |
| Empty exports with `Success` result | Range didn't overlap any recorded footage; an empty range is logged as success with 0 bytes. |
| AVI + Encrypt rejected at save | AVI does not support encryption. Switch to XProtect or disable encryption. |
| Progress stuck at 0% | Large encrypted exports take a while to start streaming. Check the System Log for category **Auto Exporter**. |

</div>
