---
title: "PKI Plugin for Milestone XProtect"
description: "Internal certificate authority and TLS cert vault for Milestone XProtect. Issue Root/Intermediate CAs, generate HTTPS / 802.1X / Service certs, and deploy them with the bundled Cert Installer."
---

<div class="show-title" markdown>

# PKI

Run a small internal certificate authority for your XProtect&trade; site without leaving the Management Client. Issue Root and Intermediate CAs, generate HTTPS / 802.1X / Service certificates, import existing ones (PEM, DER, PKCS#12), and deploy them onto each XProtect server with the bundled **PKI Cert Installer**.

Every certificate (and private key, where present) is stored as a MIP item on the Management Server, so the entire vault travels with your XProtect configuration backups.

<video controls width="100%">
  <source src="../vids/pki_usage.mp4" type="video/mp4">
</video>

## Quick Start

1. Open **Site Navigation > MIP Plug-ins > PKI > Overview** in the Management Client
2. Click **Auto setup**. The plugin generates a Root CA (`MSCP PKI (yyyy.MM) - Root CA`, valid 15 years) and issues one Service certificate per discovered XProtect service, with hostname / FQDN / IPs already in the SAN list
3. On each XProtect server, run **PKI Cert Installer** (see [Deploying certs](#deploying-certs) below), sign in, pick the matching cert, click **Install**, then open the Server Configurator to apply

## Tree layout

| Node | Purpose |
|---|---|
| Overview | Master grid of every cert. Filter by folder / issuer, search, bulk export, bulk delete, run Auto setup. |
| CA Certificates > Root Certificates | Self-signed Root CAs. Sign Intermediates and leaf certs. |
| CA Certificates > Intermediate Certificates | CAs signed by a Root. Recommended issuer for day-to-day leaf certs. |
| Client Certificates > HTTPS Certificates | Server TLS certs (Recording Server / Mobile / Event Server / Mgmt Server). |
| Client Certificates > 802.1X Certificates | Client certs for IEEE 802.1X port authentication on cameras / NVRs. |
| Client Certificates > Service Certificates | Generic service-to-service TLS certs. |

## Deploying certs

The Management Client side only stores certificates. It does not push them into the Windows certificate store on each XProtect server. Two ways to deploy:

### PKI Cert Installer (recommended)

A standalone single-file EXE that talks to the same Management Server REST endpoint as the plugin. No MIP SDK runtime required.

1. Download `Mscp.PkiCertInstaller-vX.X.exe` from your local download page at `http://<management-server>/mscp/` (see [Local Download Page](../../getting-started/installation.md#local-download-page))
2. Run it on the target XProtect server **as Administrator**
3. Sign in to the Management Server with a user that has the required PKI read permissions
4. Pick the row where **Machine Match** shows *Yes*, click **Install**
5. For an XProtect service cert: click **Open Server Configurator** and apply the new cert
6. For a workstation (Smart Client / Mgmt Client): set the Folder filter to **Root CA**, pick the Root, click **Install**. Any Intermediates are installed automatically

!!! info "What the installer does for you"
    Places the cert in the correct Windows store (LocalMachine\My for leaf certs, LocalMachine\Root for CA certs) and grants the matching XProtect service account the private-key ACL so the service can read it.

### Manual export

Select certs in the Overview pane and click **Export**. PFX includes the private key (with optional password); cert-only formats (PEM / DER / .crt) are also available.

## Role permissions

Each folder has its own read permission, granted per role under **Security > Roles > [Role] > MIP > PKI**.

| Permission | Effect when granted |
|---|---|
| Read Root Certificates | See and read certs in the Root CA folder. |
| Read Intermediate Certificates | See and read certs in the Intermediate CA folder. |
| Read HTTPS Certificates | See and read certs in the HTTPS folder. |
| Read 802.1X Certificates | See and read certs in the 802.1X folder. |
| Read Service Certificates | See and read certs in the Service folder. |

The **Overview** node is visible as long as the role has read on *at least one* folder; the cert list filters per-row by the same per-folder permissions. The PKI Cert Installer EXE talks to the same REST surface, so the same permissions decide which certs an installer operator can deploy on a given XProtect server.

## Storage and security notes

- Cert and key bytes are stored as PKCS#12 (PFX) on the MIP item, base64-encoded. Anyone with read access to the Management Server config can extract them.
- The plugin does not auto-renew. Watch **Remaining** in the Overview; generate a fresh cert before expiry and re-deploy with the Cert Installer.
- Deleting a CA item is blocked while other certs in the vault still reference its thumbprint as their issuer. Delete the dependents (or re-issue them against a different CA) first.

## Troubleshooting

| Problem | Fix |
|---|---|
| PKI node not visible in Mgmt Client | Check role permissions. Grant at least one **Read &hellip;** action under MIP > PKI. |
| Cert Installer can't connect | Verify the Management Server hostname/port and that the signed-in user has PKI read permissions. Check `installer.log` via the Help dialog's **Open log folder** button. |
| Installed cert not picked up by service | Open the Server Configurator on that machine and apply the cert. The installer only places the cert in the store, the Configurator wires it to the service. |
| Auto setup did not create a cert for a service | The service must be registered with the Management Server (visible under Recording Servers / Mobile Servers / etc.). Re-run Auto setup after the service is registered. |

</div>
