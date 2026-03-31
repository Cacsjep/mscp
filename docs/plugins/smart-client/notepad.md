---
title: "Notepad Plugin for Milestone XProtect"
description: "Notepad plugin for Milestone XProtect Smart Client — simple text editor for operator notes that persists across restarts."
---

<div class="show-title" markdown>

# Notepad

Simple text editor for operator notes directly in XProtect Smart Client view items. Notes persist across Smart Client restarts.

## Quick Start

1. In **Setup** mode, drag **Notepad** into a view
3. In Properties, set a title and font size
4. Switch to **Live** mode and start typing notes

<video controls width="100%">
  <source src="../vids/notepad_usage.mp4" type="video/mp4">
</video>

## Configuration

| Setting | Default | Description |
|---|---|---|
| **Title** | *(empty)* | Header text shown above the note area (e.g. "Shift Notes") |
| **Font Size** | 14 | Text size in the editor (1&ndash;72) |

## Troubleshooting

| Problem | Fix |
|---|---|
| Plugin not showing | Check DLLs in `MIPPlugins\Notepad\`. Unblock ZIP if manual install. |
| Notes not saving | Ensure the Smart Client has write access to property storage. |
