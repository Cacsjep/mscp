# RDP

Embed interactive RDP sessions directly into XProtect™ Smart Client view items.

> [!IMPORTANT]
> Not affiliated with or supported by Milestone Systems. XProtect™ is a trademark of Milestone Systems A/S.

## Quick Start

1. Download the installer from [Releases](../../releases)
2. **Setup** mode: drag **Remote Desktop Connection** into a view
3. Set **IP Address**, **Username**, and optionally a **Name**
4. **Live** mode: enter password and click **Connect**

**Requires:** XProtect™ Smart Client (Professional+, Expert, Corporate, or Essential+)

## Installation

### Installer (Recommended)

Download `MSCPlugins-vX.X-Setup.exe` from [Releases](../../releases) and run as **Administrator**. Select **RDP Plugin** in the component list.

### Manual (ZIP)

1. Download `RDP-vX.X.zip` from [Releases](../../releases)
2. **Unblock** it first: right-click -> Properties -> Unblock
3. Extract to `C:\Program Files\Milestone\MIPPlugins\RDP\`
4. Restart the Smart Client

## Configuration

| Setting | Default | Description |
|---|---|---|
| **Name** | *(empty)* | Display label (falls back to "Remote Desktop") |
| **IP Address** | *(empty)* | Target RDP server (required) |
| **Username** | *(empty)* | RDP username |
| **Require NLA** | Off | Network Level Authentication (CredSSP) |
| **Enable clipboard** | On | Clipboard redirection |

Password is entered at connect time and cleared immediately -- never stored.

## Security

- **Public mode** enabled -- no local credential/bitmap caching
- All redirection disabled except optional clipboard
- Drives, printers, smart cards, ports: always off

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

## Troubleshooting

| Problem | Fix |
|---|---|
| Plugin not showing | Check DLLs in `MIPPlugins\RDP\`. Unblock ZIP if manual install. |
| "IP Address is not configured" | Set IP in Properties panel (Setup mode). |
| Bad username or password | Username is in Properties, password on login overlay. |
| Connection timed out | Check port 3389, firewall, Remote Desktop enabled. |
| NLA required (code 2825) | Enable **Require NLA** in Properties. |
| DLLs blocked | Unblock the ZIP before extracting. |

### Common Error Codes

| Code | Meaning |
|---|---|
| 260 | DNS lookup failure |
| 516 | Cannot connect -- check IP and reachability |
| 520 | Host not found |
| 2055 | Bad username or password |
| 2825 | NLA required (enable in settings) |
| 3335 | Account locked out |
| 3847 | Password expired |
| 6919 | Server certificate expired/invalid |

60+ codes are decoded and shown on disconnect.
