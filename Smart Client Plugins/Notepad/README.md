# Notepad

Simple text editor for operator notes directly in XProtect Smart Client view items. Notes persist across Smart Client restarts.

> [!IMPORTANT]
> Not affiliated with or supported by Milestone Systems. XProtect is a trademark of Milestone Systems A/S.

## Quick Start

1. Download the installer from [Releases](../../releases)
2. **Setup** mode: drag **Notepad** into a view
3. In Properties, set a title and font size
4. **Live** mode: start typing notes

**Requires:** XProtect Smart Client (Professional+, Expert, Corporate, or Essential+)

## Installation

### Installer (Recommended)

Download `MSCPlugins-vX.X-Setup.exe` from [Releases](../../releases) and run as **Administrator**. Select **Notepad Plugin** in the component list.

### Manual (ZIP)

1. Download `Notepad-vX.X.zip` from [Releases](../../releases)
2. **Unblock** it first: right-click -> Properties -> Unblock
3. Extract to `C:\Program Files\Milestone\MIPPlugins\Notepad\`
4. Restart the Smart Client

## Configuration

| Setting | Default | Description |
|---|---|---|
| **Title** | *(empty)* | Header text shown above the note area (e.g. "Shift Notes") |
| **Font Size** | 14 | Text size in the editor (1-72) |

## Features

- Editable text area in Live mode for operator notes
- Notes persist via XProtect property storage (survive restarts)
- Auto-save every 30 seconds with explicit Save button
- Configurable title and font size
