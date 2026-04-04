---
title: "Remote Manager Plugin for Milestone XProtect"
description: "Remote Manager plugin for Milestone XProtect Smart Client - unified workspace for hardware web interfaces, custom websites, and RDP connections with tree-based organization and credential management."
---

<div class="show-title" markdown>

# Remote Manager

A Smart Client workspace plugin that provides a unified manager for hardware device web interfaces, custom websites, and RDP connections. All connections are organized in a tree view with drag-and-drop support, search filtering, and optional HTTP autologin.

!!! warning "Administrator Use Only"
    This plugin exposes hardware device credentials (usernames and passwords) from the management server. It should **only** be deployed to Smart Client installations used by administrators. Do not install it on operator workstations where users should not have access to device credentials.

## Quick Start

1. Open XProtect Smart Client
2. Navigate to the **Remote Manager** workspace tab
3. Hardware devices load automatically from your recording servers under the "Remote Manager" root node
4. Click any entry to open its web interface or RDP session in a new tab
5. Right-click the root node or any folder to add websites, RDP connections, or new folders
6. Drag and drop items between folders to organize them

## Connection Tree

The left panel shows all connections in a tree view. The root node "Remote Manager" is always present and cannot be deleted.

### Search

Use the search field to filter by name or IP address. The search is case-insensitive and updates in real-time. Matching items and their parent folders remain visible to preserve tree structure.

### Drag and Drop

Drag items between folders to organize them:

- Drop on a **folder** to move the item into it
- Drop on a **leaf item** to move into that item's parent folder
- **Root node** cannot be dragged
- Circular nesting is prevented (cannot drop a folder into its own descendant)
- Visual feedback: cyan highlight for valid drop targets, red for invalid

### Context Menu

Right-click for actions based on node type:

| Node Type | Actions |
|---|---|
| Root node | New Folder, Add Website, Add RDP Connection |
| Folder | New Folder, Add Website, Add RDP Connection, Rename, Delete |
| User website | Edit, Delete |
| RDP connection | Edit, Delete |
| Hardware device | *(no actions)* |

### Deleting Folders

When deleting a folder that contains hardware devices (system-defined items), those items are automatically moved back to the root node. User-defined items inside the folder are deleted along with it.

## Tabbed Browser

Clicking a leaf item opens its web interface or RDP session in a new tab on the right panel.

| Feature | Behavior |
|---|---|
| New tab | Click an entry not yet open |
| Focus existing | Click an entry that's already open |
| Close tab | Click the X on the tab |
| Close all | Click the close-all icon in the toolbar |

The tree selection stays in sync with the active tab - switching tabs updates the highlighted tree node.

### WebView2 Browser

Web interfaces are powered by Microsoft WebView2 (Chromium-based), supporting modern web frameworks.

The plugin automatically detects the correct protocol:

- HTTPS if enabled in the device's driver settings
- HTTP otherwise

### RDP Sessions

RDP connections use the built-in Windows RDP ActiveX control with these settings:

| Setting | Value |
|---|---|
| Color Depth | 32-bit |
| Smart Sizing | On |
| Connection Timeout | 5 seconds |
| Resolution | Auto (minimum 1024x768) |
| Public Mode | On (no local caching) |
| Drive/Printer Redirection | Off |

Clipboard redirection and NLA are configurable per connection.

## Credential Bar

When a hardware device tab is active, the credential bar appears at the top showing:

- **Device name** - highlighted in blue
- **Username** - with a Copy button
- **Password** - masked by default with Show/Hide toggle and Copy button

Passwords are read on-demand from the management server when you first select a device.

## Adding Connections

Right-click the root node or any folder and select the entry type:

### Web View

| Field | Required | Description |
|---|---|---|
| Name | Yes | Display name |
| URL | Yes | Full URL (`http://` or `https://`) |
| User | No | Optional username |
| Password | No | Encrypted with Windows DPAPI |

### RDP Connection

| Field | Required | Description |
|---|---|---|
| Name | Yes | Display name |
| Host / IP | Yes | Target RDP server |
| Port | No | Default: 3389 |
| Username | No | RDP username |
| Password | No | Encrypted with Windows DPAPI |
| NLA | No | Network Level Authentication |
| Clipboard | No | Clipboard redirection (default: on) |

## Settings

Two checkboxes at the bottom of the left panel control global behavior:

| Setting | Description |
|---|---|
| **Accept untrusted SSL** | Automatically accept self-signed or untrusted SSL certificates when opening web pages. Common for IP cameras with self-signed certs. Default: on. |
| **Use Autologin** | Automatically fill in username and password when a web page requests HTTP authentication (Basic/Digest). Credentials are sent only once per tab to avoid infinite retry on wrong credentials. Default: off. |

Both settings are persisted per workspace.

## Tree Persistence

The folder structure and item positions are saved automatically. When the plugin reloads:

- Previously organized items stay in their folders
- Newly discovered hardware devices appear under the root node
- Hardware devices that no longer exist are removed from the tree

## Toolbar Icons

| Icon | Color | Action |
|---|---|---|
| **Refresh** | Gray | Reload devices from recording servers |
| **Close all** | Red | Close all open tabs |

!!! info "Password Security"
    All stored passwords (websites and RDP) are encrypted using Windows Data Protection API (DPAPI) with CurrentUser scope. Only the same Windows user account can decrypt them. Hardware device passwords are read from the management server on demand and require appropriate user permissions.

## Troubleshooting

| Problem | Fix |
|---|---|
| Workspace tab not visible | Check role permissions in Management Client. Unblock ZIP if manual install. |
| Device list is empty | Verify you have recording servers with enabled hardware. Check network connectivity. |
| Password shows empty | The logged-in user may lack permissions to read hardware passwords. |
| Web page won't load | Check if the device is reachable. Try the URL in a regular browser. |
| Autologin not working | Ensure "Use Autologin" is checked and the device has credentials stored. Only works for HTTP Basic/Digest auth, not HTML form logins. |
| RDP connection timed out | Check port 3389, firewall, and that Remote Desktop is enabled on the target. |
| NLA required (code 2825) | Enable NLA in the RDP connection settings. |
| WebView2 fails | The plugin bundles WebView2. If issues persist, install the [WebView2 Runtime](https://developer.microsoft.com/en-us/microsoft-edge/webview2/) manually. |

</div>
