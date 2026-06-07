---
title: "Security & Trust - MS Community Plugins"
description: "Security posture for MS Community Plugins for Milestone XProtect: open-source, public CI builds, CodeQL and OpenSSF Scorecard scanning, coordinated vulnerability disclosure, and a no-telemetry privacy guarantee."
hide:
  - navigation
---

<div class="show-title" markdown>

# Security &amp; Trust

MS Community Plugins runs inside your Milestone XProtect™ system: recording
servers, event servers, and operator clients. That is sensitive ground, so this
page lays out exactly how the project is built, scanned, and disclosed, and what
the plugins do and do **not** do on your network.

[![Build & Release](https://github.com/Cacsjep/mscp/actions/workflows/build-release.yml/badge.svg)](https://github.com/Cacsjep/mscp/actions/workflows/build-release.yml)
[![OpenSSF Scorecard](https://api.scorecard.dev/projects/github.com/Cacsjep/mscp/badge)](https://scorecard.dev/viewer/?uri=github.com/Cacsjep/mscp)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://github.com/Cacsjep/mscp/blob/main/LICENSE)
[![Latest release](https://img.shields.io/github/v/release/Cacsjep/mscp?label=release)](https://github.com/Cacsjep/mscp/releases/latest)

## No telemetry. No phone-home.

The plugins, drivers, and standalone applications **do not collect analytics, do
not send usage or crash telemetry, and do not check for updates**. Nothing about
your system, cameras, or operators is sent anywhere.

All processing happens locally inside your Milestone environment. The only
component that contacts a third-party internet service on its own is the
**Weather** plugin (see the table below), and only to fetch a public weather
forecast for the location you configure. Everything else connects **only where
you explicitly point it**.

### What each component talks to

The following components make network connections **by design**. Every one of
them is opt-in and goes only to a destination **you** configure.

| Component | Connects to | Direction | Why |
|---|---|---|---|
| Weather | `api.open-meteo.com` (Open-Meteo) | Outbound | Public weather forecast for your configured location. No account, no key, no personal data sent. |
| RTMP Streamer (Admin) | RTMP/RTMPS destination you set | Outbound | Stream cameras to YouTube / Twitch / Facebook / custom. |
| Monitor RTMP Streamer | RTMP destination you set | Outbound | Stream desktop monitors. |
| HTTP Requests (Admin) | URLs you define in rule actions | Outbound | Event-driven HTTP(S) automation. |
| Certificate Watchdog | Hosts/endpoints you monitor | Outbound | Reads TLS certificates to check expiry. |
| Web Viewer | The web page URL you configure | Outbound | Embeds a page via Microsoft Edge WebView2. |
| Remote Manager | Web/RDP endpoints you configure | Outbound | Opens hardware web UIs and RDP sessions. |
| RTSP Driver | The camera/RTSP sources you add | Outbound (LAN) | Pulls RTSP streams into XProtect. |
| RTMP Push Driver | Listens for incoming RTMP/RTMPS | Inbound | Receives pushed streams (e.g. OBS, drones). |
| Remote Control | Local REST API on a port you set | Inbound | Controls the Smart Client over a local API. |

**All other plugins** (Flex View, Smart Bar, Snapshot Report, Metadata Display,
Timelapse, Auditor, PKI, System Status, and the rest) operate entirely within
your Milestone system and make **no outbound connections**.

!!! note "About this website"
    The trust badges above and the "latest version" shown on the
    [Download](../getting-started/installation.md) page are fetched by *this
    documentation website* from GitHub when you browse it. They are part of the
    docs, not the installed software. The plugins themselves never call home.

## How it's built

- **Fully open source.** Every line (plugins, drivers, installer) is public on
  [GitHub](https://github.com/Cacsjep/mscp) under the MIT license. You never have
  to trust a binary you can't read.
- **Public CI builds.** Releases are produced by
  [GitHub Actions](https://github.com/Cacsjep/mscp/actions/workflows/build-release.yml)
  from a tagged commit, in the open, not from a maintainer's laptop.
- **Build it yourself.** You can compile the exact artifacts from source
  (`MSCPlugins.sln` / `build.ps1`) and skip our binaries entirely. See
  [Building from Source](https://github.com/Cacsjep/mscp#building-from-source).

## How it's scanned

- **CodeQL code scanning** runs on the repository to catch common vulnerability
  patterns in the source.
- **OpenSSF Scorecard** continuously audits the project against supply-chain and
  security best practices (branch protection, pinned/maintained dependencies,
  CI hardening, and more). The live score is linked in the badge above.
- **Dependabot** monitors all dependencies monthly; updates are tracked in the
  [Changelog](changelog.md).
- **Third-party licenses** are documented in
  [Third-Party Notices](third-party-notices.md).

## Installer signature

The installer is **not yet code-signed**, so Windows SmartScreen may warn the
first time you run it. This is a free community project and an Authenticode
certificate is a recurring cost; we are pursuing a free open-source signing
program. Until then, you can verify integrity by **building from source**, or by
confirming the download came from the official
[GitHub Releases](https://github.com/Cacsjep/mscp/releases) page.

## Reporting a vulnerability

Please **do not** open a public issue for security problems. Use GitHub's private
reporting:

1. Open the [Security tab](https://github.com/Cacsjep/mscp/security).
2. Click **Report a vulnerability**.
3. Include the affected component/version, impact, and reproduction steps.

We aim to acknowledge within **3 business days**, practice **coordinated
disclosure**, publish fixes as
[Security Advisories](https://github.com/Cacsjep/mscp/security/advisories), and
credit reporters. Full policy: [SECURITY.md](https://github.com/Cacsjep/mscp/blob/main/.github/SECURITY.md).

</div>
