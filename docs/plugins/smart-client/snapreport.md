<div class="show-title" markdown>

# SnapReport

A Smart Client workspace plugin that lets users select cameras from a folder tree, grab live snapshots, and generate a PDF report with one camera per page.

## Quick Start

1. Open XProtect Smart Client
3. Navigate to the **SnapReport** workspace tab
4. Select cameras using the checkbox tree
5. Click **Generate PDF**
6. Choose a save location
7. Wait for snapshot capture and PDF generation
8. Optionally open the generated PDF

## Permissions

Read permission for the SnapReport plugin must be configured in Management Client under **Security > Roles**.

## Troubleshooting

| Problem | Fix |
|---|---|
| SnapReport tab not visible | Check role permissions in Management Client. Unblock ZIP if manual install. |
| Snapshots failing | Ensure cameras are online and the Smart Client has access to live video. |
| PDF not generating | Verify write access to the chosen save location. |
