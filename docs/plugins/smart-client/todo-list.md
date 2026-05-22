---
title: "Todo List Plugin for Milestone XProtect"
description: "Todo List plugin for Milestone XProtect Smart Client - operator checklist with free-form tasks or pre-defined shift templates that persists across restarts."
---

<div class="show-title" markdown>

# Todo List

Operator checklist view item for XProtect Smart Client. Add ad-hoc tasks
freely, or define re-usable shift templates and tick them off as a shift
progresses. Templates and free tasks persist across Smart Client restarts.

## Quick Start

1. In **Setup** mode, drag **Todo List** into a view tile
2. In Properties, set a title, pick **Mode** (Free or Template), and optionally
   open **Manage templates...** to define one or more shift templates
3. Switch to **Live** mode
4. In Free mode, type a task and press Enter (or click Add). Check the box
   to mark it done. Use the up/down arrows on each row to reorder, the
   trash icon to delete a single task, or the bottom buttons to clear
   completed tasks or wipe the list
5. In Template mode, click **Pick template...** to start a shift, tick
   tasks as they complete, and use **Start new shift** when you want to
   reset all checks

## Modes

| Mode | Use it when |
|---|---|
| **Free** | The tile is a quick scratch list. Tasks are added and removed at will. Reordering is allowed in the live view. |
| **Template** | The tile follows a fixed shift checklist. The task list is defined in advance in the template editor; only the checked state changes during the shift. |

You can switch modes from the Properties panel at any time. Free-mode
tasks and the currently active template are kept independently, so
flipping between modes does not destroy either list.

### Template mode shift flow

- Every time the view is opened in Template mode, the tile starts in the
  **No template active** state with a `Pick template...` button. This is
  deliberate: the operator must consciously start the shift, so a stale
  in-progress template is never silently resumed.
- The **Default template** setting in Properties pre-highlights one
  template in the picker dialog. It does **not** auto-load.
- During a shift, `Start new shift` clears all checks (same template).
  `Switch template...` reopens the picker so a different template can be
  selected.

## Templates

Templates are managed from the Properties panel via **Manage templates...**.
The editor is a two-pane window:

- **Left pane**: list of templates, with **+ New**, **Duplicate**,
  **Rename**, and **Delete** actions.
- **Right pane**: the template name and its ordered task list. Add a task
  with the input box at the bottom (Enter or **Add task**), edit a task
  with the pencil icon, reorder with the up/down arrows, delete with the
  trash icon.
- **Save** commits all changes to this view item. **Cancel** discards
  unsaved changes.

If you edit a template while it is the active template in the live view,
checks for tasks that still exist are preserved. Tasks added by the edit
start unchecked; tasks deleted by the edit drop out cleanly.

Templates are stored per view item. Drag a second Todo List tile into the
same view and it will start with its own empty template set.

## Configuration

| Setting | Default | Description |
|---|---|---|
| **Title** | *(empty)* | Header text shown above the list (e.g. "Night shift checklist"). |
| **Mode** | Free | Free for ad-hoc tasks, Template for pre-defined shift checklists. |
| **Default template** | *(none)* | Pre-selects this entry in the template picker dialog. Does not auto-load. Disabled in Free mode. |
| **Font size** | 14 | Text size in the list (1-72). |

## Troubleshooting

| Problem | Fix |
|---|---|
| Plugin not showing in Setup mode | Check DLLs are present in `MIPPlugins\TodoList\`. If installed from a manual ZIP, unblock the ZIP before extracting. |
| Tasks lost after restart | Ensure Smart Client has write access to its configuration store. The plugin uses standard view-item property storage. |
| Template picker shows no entries | Open Properties, click **Manage templates...**, and add at least one template with at least one task. |
| Picker keeps opening on every view switch | This is the intended behaviour in Template mode. The default template never auto-loads; the operator always starts a shift explicitly. |
