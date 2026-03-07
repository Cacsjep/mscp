# Auditor

Audit user access to recorded video in Milestone XProtect. Tracks playback mode changes, export operations, and independent playback on individual cameras. Per-user audit rules with independent reason prompts (audit log) and event triggers (analytics events).

## Features

- Per-user audit rules configured in the Management Client
- **Reason Prompts** — force users to provide a reason, written to the Milestone audit log
- **Event Triggers** — fire XProtect analytics events for use in rules, alarms, and notifications
- Reason prompts and event triggers are independently configurable per activity
- Tracks playback, export, and independent playback activities
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
4. Give the rule a name and select the users to monitor
5. Configure which reason prompts and event triggers to enable
6. Save the rule

## Reason Prompts (Audit Log)

When enabled, a dialog is shown to the user who must enter a reason. The reason is written to the Milestone system audit log.

| Prompt | When |
|---|---|
| Playback Reason Prompt | User enters playback mode |
| Export Reason Prompt | User enters the export workspace |
| Independent Playback Reason Prompt | User enables independent playback on a camera |

## Event Triggers (Rule Events)

When enabled, an XProtect analytics event is fired that can be used in rules, alarms, and notifications.

| Event | When |
|---|---|
| Audit: Playback Entry | User enters playback mode |
| Audit: Export Entry | User enters the export workspace |
| Audit: Independent Playback | User enables independent playback on a camera |

## Requirements

- Milestone XProtect (Professional+, Expert, Corporate, or Essential+)
- Smart Client, Management Client, and Event Server
