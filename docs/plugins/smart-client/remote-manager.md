---
title: "Remote Manager Plugin for Milestone XProtect"
description: "Remote Manager plugin for Milestone XProtect Smart Client - unified workspace for hardware web interfaces, custom websites, and RDP connections with tag-based filtering and credential management."
---

<div class="show-title" markdown>

# Remote Manager

A Smart Client workspace plugin that provides a unified manager for hardware device web interfaces, custom websites, and RDP connections. All connections are displayed in a flat, searchable list with a tag-based filtering system for fast access.

## Quick Start

1. Open XProtect Smart Client
2. Navigate to the **Remote Manager** workspace tab
3. Hardware devices load automatically from your recording servers
4. Click any entry to open its web interface or RDP session in a new tab
5. Use the **+** button to add custom websites or RDP connections
6. Filter entries using the tag chips at the top

## Connection List

The left panel shows all connections in a flat list sorted by name. Each entry shows an icon indicating its type:

- **Globe icon** - Hardware web interface or custom website
- **Desktop icon** - RDP connection

Hover over any entry to see its address and tags in the tooltip.

### Search

Use the search field to filter by name or IP address. The search is case-insensitive and updates in real-time as you type.

## Tag System

Every connection has tags that describe its type and origin. Tags are displayed in the filter bar above the list.

### Factory Tags

Factory tags are assigned automatically and cannot be removed:

| Tag | Applied To |
|---|---|
| **Hardware Web Interface** | Devices discovered from recording servers |
| **Website** | User-defined web entries |
| **RDP** | RDP connections |
| *Server name* (e.g. "RecServer1") | Hardware devices from that recording server |

### Custom Tags

You can create and assign custom tags (e.g. "Floor 2", "Entrance", "Critical") to organize your connections:

- **Add tags** via the Edit dialog (right-click > Edit) for websites and RDP entries
- **Add tags** via right-click > Edit Tags for hardware devices
- **Multiple tags** per item are supported

### Filtering

Click tag chips in the filter bar to filter the list:

- **Unselected** chips show a **+** icon
- **Selected** chips show a **checkmark** with amber highlight
- Click a selected chip again to deselect it
- Multiple selected tags use **AND** logic - only entries matching all selected tags are shown

## Tabbed Browser

Clicking an entry opens its web interface or RDP session in a new tab on the right panel.

| Feature | Behavior |
|---|---|
| New tab | Click an entry not yet open |
| Focus existing | Click an entry that's already open |
| Close tab | Click the X on the tab |
| Close all | Click the close-all icon in the toolbar |

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

Click the **+** button in the toolbar to add:

### Web View

| Field | Required | Description |
|---|---|---|
| Name | Yes | Display name |
| URL | Yes | Full URL (`http://` or `https://`) |
| User | No | Optional username |
| Password | No | Encrypted with Windows DPAPI |
| Tags | No | Custom tags for filtering |

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
| Tags | No | Custom tags for filtering |

## Context Menu

Right-click entries for additional options:

| Entry Type | Options |
|---|---|
| Hardware device | Edit Tags |
| Custom website | Edit, Remove |
| RDP connection | Edit, Remove |

## SSL Certificate Handling

Many IP cameras use self-signed SSL certificates. Toggle the **Accept untrusted SSL** checkbox at the bottom of the left panel. The setting is persisted per workspace.

## Toolbar Icons

| Icon | Color | Action |
|---|---|---|
| **+** | Blue | Add a website or RDP connection |
| **Refresh** | Gray | Reload devices from recording servers |
| **Close all** | Red | Close all open tabs |

!!! info "Password Security"
    All stored passwords (websites and RDP) are encrypted using Windows Data Protection API (DPAPI) with CurrentUser scope. Only the same Windows user account can decrypt them.

## Troubleshooting

| Problem | Fix |
|---|---|
| Workspace tab not visible | Check role permissions in Management Client. Unblock ZIP if manual install. |
| Device list is empty | Verify you have recording servers with enabled hardware. Check network connectivity. |
| Password shows empty | The logged-in user may lack permissions to read hardware passwords. |
| Web page won't load | Check if the device is reachable. Try the URL in a regular browser. |
| RDP connection timed out | Check port 3389, firewall, and that Remote Desktop is enabled on the target. |
| NLA required (code 2825) | Enable NLA in the RDP connection settings. |
| WebView2 fails | The plugin bundles WebView2. If issues persist, install the [WebView2 Runtime](https://developer.microsoft.com/en-us/microsoft-edge/webview2/) manually. |

</div>
