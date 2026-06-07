---
title: "Local Download Page"
description: "Optional MSI feature that turns the management server into an internal download portal for the MSC Community Plugins MSI, every plugin ZIP, the PKI Cert Installer, and any extras the admin drops into a folder."
---

<div class="show-title" markdown>

# Local Download Page

The MSI ships with an optional feature called **Local download page on this server**. When picked at install time on a box that has IIS, the installer publishes the MSI, every per-plugin ZIP, the [PKI Cert Installer](../plugins/admin/pki.md), and any admin-supplied extras to `http://<this-server>/mscp/`. Other admins on the same network browse the page and download what they need without going through GitHub.

## When the feature is offered

The feature is hidden on machines without IIS. The installer probes for `appcmd.exe` in `%SystemRoot%\System32\inetsrv\`; if it's absent the feature is set to `Level=0` (invisible, cannot be selected via `ADDLOCAL`). On a Smart Client workstation install this option never appears.

On the Management Server it shows up in the **Customize** tree as an opt-in checkbox. Default is off. The "Typical" install path does not select it.

## What gets published

When the feature is enabled, the installer creates `C:\inetpub\wwwroot\mscp\` and points an IIS virtual directory `/mscp` (under Default Web Site, port 80, anonymous) at it. The folder ends up looking like this:

```
C:\inetpub\wwwroot\mscp\
  index.html                                  the page itself
  web.config                                  IIS config (default doc, MIME)
  MscpCommunityPlugins-<Version>.msi          the running installer, copied at install time
  Mscp.PkiCertInstaller-<Version>.exe         standalone cert deployer
  plugins\
    Weather-v<Version>.zip                    one ZIP per plugin in the manifest
    MetadataDisplay-v<Version>.zip
    ...
  extras\                                     admin drop folder, empty by default
```

Browsing `http://<management-server>/mscp/` returns the page with three cards: a "Full installer" download button for the MSI, a row of cards for the PKI Cert Installer and Additional downloads, and a per-category list of individual plugins.

If the Management Server has a certificate bound to its Default Web Site (the encryption setting in Management Client), `https://<management-server>/mscp/` works without any extra configuration.

## Extras folder

`C:\inetpub\wwwroot\mscp\extras\` is a free-form drop point. Anything you copy in shows up on the page in an "Additional downloads" card on the next refresh. Useful for:

- Helper EXEs not bundled in the MSI (custom installers, diagnostic tools).
- Vendor SDKs you want neighbouring admins to grab.
- One-off CSV / JSON config files.
- Internal docs.

No upload UI. To add a file, sign in to the management server (RDP or local console), drop the file into the folder via Explorer, refresh the page. To remove, delete the file.

The card is hidden when the folder is empty.

### Security notes for extras

- The folder is served by the same anonymous virtual directory as the rest of the page. Anything you drop there is downloadable by anyone who can reach `http://<management-server>/mscp/` on the network. Treat it as "internal but public on the LAN".
- IIS Directory Browsing is enabled only for `/mscp/extras/`. Trying to browse `/mscp/` itself returns `index.html` because the default document handler kicks in first.
- IIS does emit a `To Parent Directory` link on its listing. Clicking it walks back up to `/mscp/`, which serves `index.html`. It does not expose any other listings.
- If `extras\` contains subdirectories, IIS lists them too (browsing is inherited under the location path). Use this if you want to organize a bigger drop into folders.

### Requirements for the extras card

The card relies on the **Directory Browsing** IIS role feature being installed on the server. It's part of the default IIS install on Windows Server, but some hardened environments strip it. If it's missing, fetching `/mscp/extras/` returns `404.2` or `403.14`. The page handles this silently: the Additional downloads card stays hidden. To enable, install the IIS role:

```powershell
Install-WindowsFeature -Name Web-Dir-Browsing
```

## PKI Cert Installer

`Mscp.PkiCertInstaller-<Version>.exe` is a single-file Windows EXE shipped alongside the MSI. It connects to this management server, lists every certificate issued by the [PKI plugin](../plugins/admin/pki.md), and installs picked ones into the local Windows certificate store with the right private-key ACLs. Use it on Recording / Management / Mobile / Event / Failover servers that don't have the PKI plugin installed.

The same EXE is bundled inside the PKI plugin's ZIP; the download here is for remote servers where the PKI plugin isn't deployed.

## Uninstall

Uninstalling the MSI (or unchecking the IISHosting feature in Programs and Features and re-running setup) removes:

- The `/mscp` virtual directory from IIS.
- `index.html`, `web.config`, the cert installer EXE, the MSI copy.
- Every plugin ZIP in `plugins\` (the sweep CA wipes `*.zip` before the folder is torn down).
- Everything in `extras\`. A cleanup custom action recursively deletes the contents so the folder can be removed by `RemoveFiles`.

Major Upgrade keeps the extras folder contents intact: the folder component has a persistent GUID and the cleanup CA only fires on uninstall, not on upgrade.

## Troubleshooting

| Problem | Fix |
|---|---|
| Feature checkbox doesn't appear in the installer | The installer didn't find `appcmd.exe`. Install the IIS Management Console role or the basic IIS feature set, then re-run the MSI. |
| `/mscp/` returns 403.1 | Some other web.config in the inheritance chain stripped Script from the handler access policy. Verify no parent web.config sets `<handlers accessPolicy="Read"/>`. |
| `/mscp/extras/` returns 404.2 or 403.14 | The Directory Browsing IIS role feature is missing. `Install-WindowsFeature -Name Web-Dir-Browsing`. |
| Additional downloads card doesn't appear | Either `extras\` is empty (drop a file and refresh) or Directory Browsing isn't installed (see above). |
| `.msi` download triggers a security warning | The MSI is signed via the build pipeline. Right-click, Properties, check the Digital Signatures tab. If unsigned, the build was done locally without code signing. |
| HTTPS link works but shows a certificate warning | The Management Server's Default Web Site is bound to a self-signed certificate. Bind a CA-signed cert (or use the [PKI plugin](../plugins/admin/pki.md) to issue one) and rebind it in `inetmgr`. |
