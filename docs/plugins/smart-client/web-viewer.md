---
title: "Web Viewer for Milestone XProtect"
description: "Web Viewer plugin for Milestone XProtect Smart Client - embed any web page (dashboard, status board, intranet site) directly in a view item."
---

<div class="show-title" markdown>

# Web Viewer

Embed any web page in a Smart Client view item. One URL per view item, rendered with Microsoft Edge WebView2. Built for displaying dashboards (Grafana, Power BI, vendor analytics consoles), I/O status pages, intranet portals, or any internal tool that lives in a browser, alongside camera tiles in the same view.

For a multi-tab web/RDP launcher with a folder tree, see the **Remote Manager** plugin. Web Viewer is the slim "one URL, no chrome" variant for cases where the URL is fixed and you want it to read like a native widget.

## Quick Start

1. In **Setup** mode, drag a **Web Viewer** view item into a slot
2. Click into the view item; the Setup form appears
3. Enter the **URL** (must start with `http://` or `https://`)
4. (Optional) Set a **Title**, **User** / **Password**, and tick the cert / login options
5. Click **Save**, then switch to **Live** - the page loads inside the view item

## Properties

| Property | Default | Notes |
|---|---|---|
| **URL** | (none) | Full URL including scheme. `http://` and `https://` only |
| **Title** | (empty) | Optional title shown above the page; toggle **Show** to hide it without losing the value |
| **User** / **Password** | (empty) | Used only for **HTTP Basic** auth prompts; passwords are encrypted with the Windows DPAPI under the current user before being saved |
| **Auto-accept invalid TLS certificates** | On | Tolerates self-signed / hostname-mismatch / expired certs - typical for in-house dashboards. Turn off for public-internet pages |
| **Auto-fill credentials on basic-auth prompt** | On | When the page returns a 401 Basic challenge, the saved User / Password are submitted once. Disable if a page should always prompt the operator |

The **Test in browser** button on the Setup form opens the URL in your default browser, useful for verifying the URL works at all before debugging anything inside the view item.

## Authentication Behavior

- **Basic auth (RFC 7617)** - handled in-process via WebView2; controlled by the **Auto-fill credentials** option. The plugin auto-fills the saved credentials only **once** per page load, so a wrong password does not turn into an infinite login loop
- **Form / cookie / OAuth login** - the plugin does not auto-fill these; the page itself handles the login flow. Cookies and session storage are kept in a per-user data folder so subsequent loads stay logged in
- **Windows / Negotiate** - WebView2 may prompt depending on Edge group policy; not auto-handled by the plugin

## Storage and Cookies

- All settings are stored as MIP item properties on the view item; no external configuration files
- Passwords are DPAPI-encrypted under the current Windows user, so the saved view config does not contain plaintext credentials. The encrypted blob can only be decrypted on the same machine by the same user
- Browser session data (cookies, cache, local storage) lives at `%LOCALAPPDATA%\MSCPlugins\WebViewer\WebView2Data`. Clearing this folder logs you out of every embedded site

## Live and Playback

The plugin loads the configured URL in both **Live** and **Playback** modes. Web pages have no native concept of timeline scrub, so playback shows the page as it is right now, alongside the recorded video on the rest of the view.

A loading indicator (pulsing dot + "Loading...") is shown while the page is fetched. If the load fails, an error panel appears with a **Reload** button.

## Prerequisites

- **Microsoft Edge WebView2 Runtime** must be installed on the Smart Client machine (it ships with current Windows 10 / 11 builds and is shipped by Edge updates). Without it, the view item shows an initialization error
- Network access from the Smart Client machine to the target URL. If the page is on a separate VLAN or behind a firewall, the page will time out the same way it would in a normal browser

## Troubleshooting

| Problem | Fix |
|---|---|
| "WebView2 init/navigate failed" | Install / reinstall the **Edge WebView2 Runtime** on the Smart Client machine |
| Blank page or "Navigation failed" | The site itself is unreachable or refused. Use **Test in browser** to verify outside the view item |
| Certificate error on a self-signed dashboard | Make sure **Auto-accept invalid TLS certificates** is on |
| Auto-login loop on a page with wrong credentials | The plugin only auto-fills **once** per load to avoid the loop. Re-open the configuration and update the password, or disable Auto-fill |
| Mixed-content warnings (http content on an https page) | This is browser behavior; serve the embedded resources over the same scheme as the page |
| Page logs me out after every Smart Client restart | The site uses a session cookie, not a persistent one. Use **Auto-fill credentials** if it is HTTP Basic, or accept that form-login pages need a fresh login per session |
| I want one view item to host multiple pages | Use **Remote Manager** instead - it provides tabs, folders, and a tree picker on top of WebView2 |

</div>
