<div class="show-title" markdown>

# Smart Bar

A workspace toolbar plugin that adds a button to the Smart Client toolbar in Live and Playback mode.

## Quick Start

1. Install the plugin (via installer or manual ZIP)
2. Restart the Smart Client
3. The **Smart Bar** button appears in the workspace toolbar

## Features

- Toolbar button visible in Live and Playback workspaces
- Uses FontAwesome icon via CommunitySDK

## Troubleshooting

| Problem | Fix |
|---|---|
| Button not showing | Check DLLs in `MIPPlugins\SmartBar\`. Unblock ZIP if manual install. |
| Plugin not loading | Restart Smart Client. Verify `plugin.def` is present in the install folder. |
