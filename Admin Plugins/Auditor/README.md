# Auditor

Audit user access to recorded video in Milestone XProtect. Tracks playback mode changes, export operations, and independent playback on individual cameras. Generates XProtect analytics events with configurable per-user audit rules.

## Features

- Per-user audit rules configured in the Management Client
- Reason prompts when monitored users enter playback or export
- Tracks playback, export, and independent playback activities
- Generates XProtect analytics events for alarm and rule integration
- Multi-environment plugin (Smart Client, Management Client, Event Server)

## Installation

Copy the contents of the release ZIP to:

```
C:\Program Files\Milestone\MIPPlugins\Auditor\
```

Restart the Smart Client, Management Client, and Event Server.

## Configuration

1. Open the Management Client
2. Navigate to the **Auditor** node in the sidebar
3. Create a new **Audit Rule**
4. Select the users to monitor and choose which activities to audit
5. Save the rule

## Audited Activities

| Event | Description |
|---|---|
| Playback Entry | User switches from live to playback mode |
| Export Entry | User enters the export workspace |
| Export Started | Export operation begins |
| Export Completed | Export operation finishes |
| Independent Playback | User enables independent playback on a camera |
| Restricted Media | Restricted media access detected |

## Requirements

- Milestone XProtect (Professional+, Expert, Corporate, or Essential+)
- Smart Client, Management Client, and Event Server
