<div class="show-title" markdown>

# Auditor

Audit user access to recorded video in XProtect. Tracks playback mode changes, export operations, and independent playback with configurable per-user audit rules. Reason prompts and event triggers are independently configurable.

## Quick Start

1. Open the **Management Client**
2. Navigate to the **Auditor** node in the sidebar
3. Create a new **Audit Rule**
4. Give the rule a name, select users to monitor
5. Optionally enable **Specify Cameras** to restrict the rule to specific cameras
6. Configure reason prompts and event triggers
7. Save the rule

<video controls width="100%">
  <source src="../vids/auditor.mp4" type="video/mp4">
</video>

## Camera Selection

By default, audit rules apply to **all cameras**. To restrict a rule to specific cameras, enable the **Specify Cameras** checkbox and add cameras using the camera picker.

When enabled, reason prompts and event triggers only fire when the user interacts with one of the listed cameras.

## Reason Prompts (Audit Log)

When enabled, the user is shown a dialog and must enter a reason. The reason is written to the Milestone system audit log.

- **Playback Reason Prompt** - when entering playback mode
- **Export Reason Prompt** - when entering the export workspace
- **Independent Playback Reason Prompt** - when enabling independent playback on a camera

!!! info "Auditor Log Category"
    The Auditor log category is only available in 2025 R3. In earlier versions, the MIP SDK does not support setting a category.

## Event Triggers (Rule Events)

When enabled, an XProtect analytics event is fired. Use these events to trigger email notifications, alarms, or any other XProtect rule action.

Source: Audit Rule item

| Event | Description |
|---|---|
| Audit: Playback Entry | User switches from live to playback mode |
| Audit: Export Entry | User enters the export workspace |
| Audit: Independent Playback | User enables independent playback on a camera |

Reason prompts and event triggers are independent - you can enable one, both, or neither for each activity.


## Architecture

The plugin runs across three environments:

| Component | Environment | Role |
|---|---|---|
| Admin UI | Management Client | Configure audit rules (users, prompts, triggers) |
| Background Plugin | Smart Client | Monitor user activity, show reason prompts, send audit reports |
| Background Plugin | Event Server | Write audit log entries, fire XProtect analytics events |

## Troubleshooting

| Problem | Fix |
|---|---|
| No audit events firing | Ensure at least one audit rule is created and enabled with users selected, and event triggers are turned on |
| Reason prompt not showing | Check that the user matches a rule with the relevant reason prompt enabled |
| Events not in alarm list | Verify the Event Server is running and the plugin is loaded |
| Audit log entries missing | Ensure reason prompts are enabled - audit log entries are only written when a reason is provided |
| Rule not triggering for specific cameras | If **Specify Cameras** is enabled, verify the relevant cameras are added to the rule's camera list |

</div>
