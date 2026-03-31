---
title: "WebView Plugin for Milestone XProtect"
description: "WebView plugin for Milestone XProtect Smart Client -browse hardware device web interfaces and custom websites with tabbed WebView2 browser, credential display, and DPAPI-encrypted password storage."
---

<div class="show-title" markdown>

# WebView

A Smart Client workspace plugin that provides a built-in tabbed web browser for accessing hardware device web interfaces and custom websites directly from within XProtect Smart Client. Automatically discovers all hardware devices from your recording servers and displays them in a searchable tree with one-click access to their web UIs.

## Quick Start

1. Open XProtect Smart Client
2. Navigate to the **WebView** workspace tab
3. The device tree loads automatically, grouped by Recording Server
4. Click any device to open its web interface in a new tab
5. Use the credential bar at the top to copy username/password

<video controls width="100%">
  <source src="../vids/wv_usage.mp4" type="video/mp4">
</video>

## Device Tree

The left panel shows all enabled hardware devices organized by Recording Server. Each device shows its name with the IP address on hover (tooltip).

- **Search** -filter by device name or IP address using the search field
- **Refresh** -click the refresh icon to reload the device tree from the management server
- Localhost and loopback devices (`127.0.0.1`, `::1`) are automatically filtered out

## Tabbed Browser

Clicking a device opens its web interface in a new browser tab on the right panel. The browser is powered by Microsoft WebView2 (Chromium-based), supporting modern web frameworks including React, Vue, and Angular.

| Feature | Behavior |
|---|---|
| New tab | Click a device not yet open |
| Focus existing | Click a device that's already open |
| Close tab | Click the red × on the tab |
| Close all | Click the close-all icon in the toolbar |

### Protocol Detection

The plugin automatically determines the correct protocol:

- If the device has HTTPS enabled in its driver settings, it connects via `https://`
- Otherwise, it uses the configured `http://` address

## Credential Bar

When a device tab is active, the credential bar appears at the top showing:

- **Device name** -highlighted in blue
- **Username** -with a Copy button
- **Password** -masked by default with Show/Hide toggle and Copy button

Passwords are read on-demand from the management server using `ReadPasswordHardware()` when you first select a device.

## User Defined Websites

Click the **+** button in the toolbar to add custom website entries. These appear under a dedicated **User Defined** section in the tree.

| Field | Required | Description |
|---|---|---|
| Name | Yes | Display name in the tree |
| URL | Yes | Full URL (must start with `http://` or `https://`) |
| User | No | Optional username (shown in credential bar) |
| Password | No | Optional password, encrypted with Windows DPAPI |

Right-click a user-defined entry to remove it.

!!! info "Password Security"
    User-defined passwords are encrypted using Windows Data Protection API (DPAPI) with CurrentUser scope. Only the same Windows user account that created the entry can decrypt the password.

## SSL Certificate Handling

Many IP cameras use self-signed SSL certificates. By default, the plugin auto-accepts all SSL certificate errors to provide seamless access.

Toggle the **Accept untrusted SSL** checkbox at the bottom of the left panel to change this behavior. The setting is persisted per workspace.

## Toolbar Icons

| Icon | Color | Action |
|---|---|---|
| **+** | Blue | Add a custom website entry |
| **↻** | Gray | Refresh the device tree |
| **⊗** | Red | Close all open browser tabs |

## Troubleshooting

| Problem | Fix |
|---|---|
| WebView tab not visible | Check role permissions in Management Client. Unblock ZIP if manual install. |
| Device tree is empty | Verify you have recording servers with enabled hardware. Check network connectivity to the management server. |
| Password shows empty | The logged-in user may lack permissions to read hardware passwords. Check Management Client user roles. |
| Web page won't load | Check if the device is reachable on the network. Try the URL directly in a regular browser. |
| SSL error despite auto-accept | Ensure "Accept untrusted SSL" is checked at the bottom of the left panel. Close and re-open the tab. |
| WebView2 fails to initialize | The plugin bundles WebView2, but if issues persist, install the [WebView2 Runtime](https://developer.microsoft.com/en-us/microsoft-edge/webview2/) manually. |
</div>
