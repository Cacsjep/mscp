<div class="show-title" markdown>

# RDP

Embed interactive RDP sessions directly into XProtect Smart Client view items.

## Quick Start

1. In **Setup** mode, drag **Remote Desktop Connection** into a view
3. Set **IP Address**, **Username**, and optionally a **Name**
4. Switch to **Live** mode, enter the password, and click **Connect**

## Configuration

| Setting | Default | Description |
|---|---|---|
| **Name** | *(empty)* | Display label (falls back to "Remote Desktop") |
| **IP Address** | *(empty)* | Target RDP server (required) |
| **Username** | *(empty)* | RDP username |
| **Require NLA** | Off | Network Level Authentication (CredSSP) |
| **Enable clipboard** | On | Clipboard redirection |

Password is entered at connect time and cleared immediately. Never stored.

## RDP Settings

| Setting | Value |
|---|---|
| Port | 3389 |
| Color Depth | 32-bit |
| Smart Sizing | On |
| Connection Timeout | 5s |
| Resolution | Auto (min 1024x768) |
| Auth Level | 2 (TLS) |
| NLA/CredSSP | Per config |

## Security

- **Public mode** enabled, no local credential/bitmap caching
- All redirection disabled except optional clipboard
- Drives, printers, smart cards, ports: always off

## Troubleshooting

| Problem | Fix |
|---|---|
| Plugin not showing | Check DLLs in `MIPPlugins\RDP\`. Unblock ZIP if manual install. |
| "IP Address is not configured" | Set IP in Properties panel (Setup mode). |
| Bad username or password | Username is in Properties, password on login overlay. |
| Connection timed out | Check port 3389, firewall, Remote Desktop enabled on target. |
| NLA required (code 2825) | Enable **Require NLA** in Properties. |

### Common Error Codes

| Code | Meaning |
|---|---|
| 260 | DNS lookup failure |
| 516 | Cannot connect. Check IP and reachability. |
| 520 | Host not found |
| 2055 | Bad username or password |
| 2825 | NLA required (enable in settings) |
| 3335 | Account locked out |
| 3847 | Password expired |
| 6919 | Server certificate expired/invalid |

60+ codes are decoded and shown on disconnect.
