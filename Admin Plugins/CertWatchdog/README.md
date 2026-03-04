# Certificate Watchdog Plugin

Monitor SSL/TLS certificate expiry for all XProtect HTTPS endpoints. Fires Milestone events at 60/30/15 day thresholds and provides a Smart Client workspace dashboard.

## Features

- **Automatic endpoint discovery**  Finds Management Server, Recording Servers, and all registered service HTTPS endpoints
- **Periodic certificate checking**  Checks all discovered endpoints every 6 hours (configurable)
- **Milestone event integration**  Fires events at 60, 30, and 15 day thresholds for use in XProtect Rules
- **Smart Client dashboard**  "Certificates" workspace tab showing all endpoints with expiry status
- **System log entries**  Certificate status changes logged to XProtect System Log
- **Duplicate prevention**  Each threshold event fires only once per certificate per threshold

## Architecture

This plugin spans all three XProtect environments from a single DLL:

| Environment | Component | Purpose |
|---|---|---|
| Event Server | BackgroundPlugin | Discovers endpoints, checks certs, fires events |
| Management Client | ItemManager | Registers event types, shows admin view |
| Smart Client | WorkspacePlugin | "Certificates" dashboard tab |

## Event Types

Available as triggers in XProtect Rules:

| Event | Description |
|---|---|
| Cert Expire (60 Days) | Certificate expires within 60 days |
| Cert Expire (30 Days) | Certificate expires within 30 days |
| Cert Expire (15 Days) | Certificate expires within 15 days |

## Smart Client Dashboard

The "Certificates" workspace tab displays a grid with:

- **Endpoint**  Hostname of the server
- **URL**  Full HTTPS URL
- **Issuer**  Certificate issuer
- **Expires**  Certificate expiry date
- **Days Left**  Days remaining (color-coded: green/yellow/red)
- **Status**  OK, Expiring, Critical, Expired, or Error

## Installation

### Via Installer

Select "Certificate Watchdog Plugin" in the Admin Plugins section of the unified installer.

### Manual

1. Copy the contents of the ZIP to `C:\Program Files\Milestone\MIPPlugins\CertWatchdog\`
2. Restart the Event Server service
3. Restart the Management Client
4. Restart the Smart Client

## Requirements

- Milestone XProtect Professional+ or higher
- Event Server, Management Client, and Smart Client
- HTTPS endpoints to monitor (the plugin auto-discovers these)

## Development

This plugin spans all three XProtect environments, so building it stops the Event Server, Smart Client, and Management Client, deploys to `MIPPlugins\CertWatchdog\`, and restarts the Event Server. The shared `Directory.Build.targets` handles this automatically.

Use the VS launch profile dropdown (F5) to pick which process to debug:

- **Smart Client** — launches `Client.exe` (debug the workspace UI)
- **Management Client** — launches `VideoOS.Administration.exe` (debug the admin view)
- **Event Server (console)** — launches `VideoOS.Event.Server.exe -x` (debug background cert checking)

The `-x` flag runs the Event Server as a console process so VS can attach the debugger from startup.

