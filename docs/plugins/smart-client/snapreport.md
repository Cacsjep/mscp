---
title: "Snapshot Report PDF Plugin for Milestone XProtect"
description: "Snapshot Report plugin for Milestone XProtect Smart Client — generate PDF reports with live camera snapshots, one camera per page."
---

<div class="show-title" markdown>

# Snapshot Report

A Smart Client workspace plugin that lets users select cameras from a folder tree, grab live snapshots, and generate a PDF report with one camera per page.

## Quick Start

1. Open XProtect Smart Client
3. Navigate to the **Snap Report** workspace tab
4. Select cameras using the checkbox tree
5. Click **Generate PDF**
6. Choose a save location
7. Wait for snapshot capture and PDF generation
8. Optionally open the generated PDF

<video controls width="100%">
  <source src="../vids/snap_usage.mp4" type="video/mp4">
</video>

## Split large reports

When a report covers many cameras the resulting PDF can grow very large and become awkward to share or open. Tick **Split PDF** in the header and set a maximum size in **MB** (default `100`) to break the report into numbered parts instead of one big file.

| Setting | Behavior |
|---|---|
| **Split PDF** off (default) | Single PDF saved exactly to the path you choose in the Save dialog. |
| **Split PDF** on | Multiple PDFs saved beside the chosen path, numbered `<name>.001.pdf`, `<name>.002.pdf`, ... Each part stays at or under the chosen size (a single camera page can slightly exceed the limit if its snapshot alone is larger). |

When splitting produces more than one file, the status bar reports how many parts were written and the **Open** prompt opens the **output folder** in Explorer so you can pick which part to view. With splitting off, or when only a single part is produced, you get the usual *open the PDF now?* prompt.

The size limit is checked per page, using the snapshot's compressed size plus a small per-page overhead, so the actual file sizes are close to the limit without going significantly over.

## Permissions

Read permission for the SnapReport plugin must be configured in Management Client under **Security > Roles**.

## Troubleshooting

| Problem | Fix |
|---|---|
| SnapReport tab not visible | Check role permissions in Management Client. Unblock ZIP if manual install. |
| Snapshots failing | Ensure cameras are online and the Smart Client has access to live video. |
| PDF not generating | Verify write access to the chosen save location. |
| "Split size must be a whole number..." | The **MB** box must contain a positive integer (e.g. `100`). Decimals and text are rejected. |
| Only one `.001.pdf` file produced even with splitting on | The whole report fits under the limit. Lower the MB value if you wanted multiple parts. |
