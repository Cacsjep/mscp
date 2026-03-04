# Certificate Watchdog Plugin

Monitor SSL/TLS certificate expiry for all XProtect HTTPS endpoints. Fires Milestone events at 60/30/15 day thresholds and provides a Smart Client workspace dashboard.

## Features

- **Automatic endpoint discovery**  Finds Management Server, Recording Servers, registered services, and HTTPS-enabled hardware devices
- **Hardware device monitoring**  Reads HTTPS settings from each hardware's driver configuration via the Configuration API
- **Periodic certificate checking**  Checks all discovered endpoints every 6 hours with parallel connections (up to 40 concurrent)
- **Config change detection**  Re-checks certificates within 20 seconds when hardware or servers are added/removed/modified
- **Milestone event integration**  Fires events at 60, 30, and 15 day thresholds for use in XProtect Rules
- **Separate device events**  Hardware certificate events use the camera as event source, allowing per-camera rules
- **Smart Client dashboard**  "Certificates" workspace tab with separate server and hardware certificate tables
- **System log entries**  Certificate status changes logged to XProtect System Log
- **Duplicate prevention**  Each threshold event fires only once per endpoint per threshold during runtime

## Architecture

This plugin spans all three XProtect environments from a single DLL:

| Environment | Component | Purpose |
|---|---|---|
| Event Server | BackgroundPlugin | Discovers endpoints, checks certs, fires events |
| Management Client | ItemManager | Registers event types, shows admin view |
| Smart Client | WorkspacePlugin | "Certificates" dashboard tab |

## Event Types

Available as triggers in XProtect Rules:

**Server Certificate Events** (source: Certificate Watchdog item)

| Event | Description |
|---|---|
| Cert Expire (60 Days) | Server certificate expires within 60 days |
| Cert Expire (30 Days) | Server certificate expires within 30 days |
| Cert Expire (15 Days) | Server certificate expires within 15 days |

**Device Certificate Events** (source: individual camera)

| Event | Description |
|---|---|
| Device Cert Expire (60 Days) | Hardware certificate expires within 60 days |
| Device Cert Expire (30 Days) | Hardware certificate expires within 30 days |
| Device Cert Expire (15 Days) | Hardware certificate expires within 15 days |

## Smart Client Dashboard

The "Certificates" workspace tab shows two sortable tables. Click any column header to sort.

**Server Certificates**  Management Server, Recording Servers, and registered services:

| Column | Description |
|---|---|
| Service | Service type (e.g. Recording Server) |
| Endpoint | Server hostname |
| URL | Full HTTPS URL |
| Issuer | Certificate issuer |
| Expires | Certificate expiry date |
| Days Left | Days remaining (color-coded: green/yellow/red) |
| Status | OK, Expiring, Critical, Expired, or Error |

**Hardware Certificates**  HTTPS-enabled cameras and devices:

| Column | Description |
|---|---|
| Name | Hardware device name |
| URL | HTTPS URL (constructed from device address and HTTPS port) |
| Issuer | Certificate issuer |
| Expires | Certificate expiry date |
| Days Left | Days remaining (color-coded) |
| Status | OK, Expiring, Critical, Expired, or Error |

**Buttons:**

- **Recollect** — Triggers a full certificate re-check on the Event Server (endpoint discovery, certificate validation, and event firing). Use this after replacing a certificate to immediately verify the change.
- **Refresh** — Fetches the latest cached results from the Event Server without triggering a new check.

> **Note:** The Event Server checks certificates automatically every 6 hours and within 20 seconds of hardware/server config changes. After a restart, the first check runs after approximately 30 seconds.

## Installation

### Via Installer

Select "Certificate Watchdog Plugin" in the Admin Plugins section of the unified installer.

### Manual

1. Copy the contents of the ZIP to `C:\Program Files\Milestone\MIPPlugins\CertWatchdog\`
2. Restart the Event Server service
3. Restart the Management Client
4. Restart the Smart Client

## Permissions

The Smart Client dashboard is controlled by role-based security. In Management Client under **Security > Roles**, the plugin appears as "Certificate Watchdog" with a **Read** permission. Grant it to allow the Certificates workspace tab; deny or leave unset to hide it. The Event Server background checks and event firing are not affected by this setting.

## Requirements

- Milestone XProtect Professional+ or higher
- Event Server, Management Client, and Smart Client
- HTTPS endpoints to monitor (the plugin auto-discovers these)

## Development

This plugin spans all three XProtect environments, so building it stops the Event Server, Smart Client, and Management Client, deploys to `MIPPlugins\CertWatchdog\`, and restarts the Event Server. The shared `Directory.Build.targets` handles this automatically.

Use the VS launch profile dropdown (F5) to pick which process to debug:

- **Smart Client**  launches `Client.exe` (debug the workspace UI)
- **Management Client**  launches `VideoOS.Administration.exe` (debug the admin view)
- **Event Server (console)**  launches `VideoOS.Event.Server.exe -x` (debug background cert checking)
