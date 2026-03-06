<div class="show-title" markdown>

# Auditor

Audit user access to recorded video in XProtect. Tracks playback mode changes, export operations, and independent playback with configurable per-user audit rules and reason prompts.

## Quick Start

1. Open the **Management Client**
2. Navigate to the **Auditor** node in the sidebar
3. Create a new **Audit Rule**
4. Select the users to monitor and choose which activities to audit
5. Save the rule

## Events

Source: Audit Rule item

| Event | Description |
|---|---|
| Audit: Playback Entry | User switches from live to playback mode |
| Audit: Export Entry | User enters the export workspace |
| Audit: Export Completed | Export operation finishes |
| Audit: Independent Playback | User enables independent playback on a camera |
| Audit: Restricted Media | Restricted media access detected |

Use these events to trigger email notifications, alarms, or any other XProtect rule action.

## Reason Prompts

When a monitored user performs an audited action, a dialog prompts them to provide a reason. The reason is included in the analytics event's custom tag and can be viewed in the alarm details.

Reason prompts can be configured per audit type:

- **Audit Playback** - prompt when entering playback mode
- **Audit Export** - prompt when entering the export workspace
- **Audit Independent Playback** - prompt when enabling independent playback on a camera

## Architecture

The plugin runs across three environments:

| Component | Environment | Role |
|---|---|---|
| Admin UI | Management Client | Configure audit rules (users, audit types) |
| Background Plugin | Smart Client | Monitor user activity, show reason prompts, send audit reports |
| Background Plugin | Event Server | Receive audit reports, fire XProtect analytics events |

## Troubleshooting

| Problem | Fix |
|---|---|
| No audit events firing | Ensure at least one audit rule is created and enabled with users selected |
| Reason prompt not showing | Check that the user matches a rule with the relevant audit type enabled |
| Events not in alarm list | Verify the Event Server is running and the plugin is loaded |

</div>
