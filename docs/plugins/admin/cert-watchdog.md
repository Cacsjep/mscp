<div class="show-title" markdown>

# Certificate Watchdog

Monitor SSL/TLS certificate expiry for all XProtect HTTPS endpoints. Fires Milestone events at configurable thresholds and provides a Smart Client workspace dashboard.

## Quick Start

1. Open the **Certificates** workspace tab in the Smart Client to see all endpoint statuses

The plugin auto-discovers all HTTPS endpoints. No manual configuration needed.

## Event Types

Available as triggers in XProtect Rules:

### Server Certificate Events

Source: Certificate Watchdog item

| Event | Description |
|---|---|
| Cert Expire (60 Days) | Server certificate expires within 60 days |
| Cert Expire (30 Days) | Server certificate expires within 30 days |
| Cert Expire (15 Days) | Server certificate expires within 15 days |

### Device Certificate Events

Source: individual camera/hardware

| Event | Description |
|---|---|
| Device Cert Expire (60 Days) | Hardware certificate expires within 60 days |
| Device Cert Expire (30 Days) | Hardware certificate expires within 30 days |
| Device Cert Expire (15 Days) | Hardware certificate expires within 15 days |

Use these events to trigger email notifications, alarms, or any other XProtect rule action.

## Smart Client Dashboard

The **Certificates** workspace tab shows two sortable tables. Click any column header to sort.

### Server Certificates

Management Server, Recording Servers, and registered services:

| Column | Description |
|---|---|
| Service | Service type (e.g. Recording Server) |
| Endpoint | Server hostname |
| URL | Full HTTPS URL |
| Issuer | Certificate issuer |
| Expires | Certificate expiry date |
| Days Left | Days remaining (color-coded: green/yellow/red) |
| Status | OK, Expiring, Critical, Expired, or Error |

### Hardware Certificates

HTTPS-enabled cameras and devices:

| Column | Description |
|---|---|
| Name | Hardware device name |
| URL | HTTPS URL (constructed from device address and HTTPS port) |
| Issuer | Certificate issuer |
| Expires | Certificate expiry date |
| Days Left | Days remaining (color-coded) |
| Status | OK, Expiring, Critical, Expired, or Error |

### Dashboard Buttons

- **Recollect**: Triggers a full certificate re-check on the Event Server (endpoint discovery, certificate validation, and event firing). Use this after replacing a certificate to immediately verify the change.
- **Refresh**: Fetches the latest cached results from the Event Server without triggering a new check.

!!! info "Automatic checks"
    The Event Server checks certificates automatically every 6 hours and within 20 seconds of hardware/server config changes. After a restart, the first check runs after approximately 30 seconds.

## Permissions

The Smart Client dashboard is controlled by role-based security. In Management Client under **Security > Roles**, the plugin appears as "Certificate Watchdog" with a **Read** permission. Grant it to allow the Certificates workspace tab; deny or leave unset to hide it. The Event Server background checks and event firing are not affected by this setting.

## Troubleshooting

| Problem | Fix |
|---|---|
| Certificates tab not visible | Check role permissions in Management Client. Grant Read for Certificate Watchdog. |
| No data showing | Ensure Event Server is running. Wait 30 seconds after restart for first check. Click **Refresh**. |
| Status shows "Error" | The endpoint may be unreachable. Check network connectivity and firewall. |
| Events not firing | Verify the Event Server plugin is loaded. Check Event Server logs. |

## Architecture

This plugin spans all three XProtect environments from a single DLL:

| Environment | Component | Purpose |
|---|---|---|
| Event Server | BackgroundPlugin | Discovers endpoints, checks certs, fires events |
| Management Client | ItemManager | Registers event types, shows admin view |
| Smart Client | WorkspacePlugin | "Certificates" dashboard tab |
