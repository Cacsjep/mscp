# SnapReport - Camera Snapshot PDF Report Generator

A Smart Client workspace plugin that lets users select cameras from a folder tree, grab live snapshots, and generate a PDF report with one camera per page.

## Features

- Camera folder tree with checkboxes for selection
- Live JPEG snapshot capture from selected cameras
- PDF report generation with one camera per page (PdfSharp)
- Progress tracking during snapshot capture
- Dark theme matching XProtect Smart Client

## Use Cases

- Site surveys and compliance documentation
- Status reports for camera installations
- Quick visual verification of camera views
- Incident documentation

## Installation

### Unified Installer
Use the MSCPlugins installer and select "SnapReport Plugin".

### Manual
1. Download `SnapReport-vX.X.zip` from [Releases](../../releases)
2. Unblock the ZIP (right-click → Properties → Unblock)
3. Extract to `C:\Program Files\Milestone\MIPPlugins\SnapReport\`
4. Restart the Smart Client

## Usage

1. Open XProtect Smart Client
2. Navigate to the **SnapReport** workspace tab
3. Select cameras using the checkbox tree
4. Click **Generate PDF**
5. Choose save location
6. Wait for snapshot capture and PDF generation
7. Optionally open the generated PDF

## Requirements

- Milestone XProtect (Professional+, Expert, Corporate, or Essential+)
- XProtect Smart Client
- Read permission for the SnapReport plugin (configured in Management Client roles)
