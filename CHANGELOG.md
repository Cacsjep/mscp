# Changelog

All notable changes to this project will be documented in this file.

## [1.4.2] - 2026-03-10

- Add: WiX v5 MSI installer (replaces NSIS)
- Add: Dev build CI workflow (`d*` tags for development releases)
- Add: Auto-incrementing build version for local MSI builds
- Fix: Icon rendering skipped in Service environment (eliminates STA thread errors)
- Fix: Duplicate event type registration in HTTP Requests plugin
- Fix: `LogMessage.CategoryName` compatibility with older XProtect versions (pre-2025R2)
c- Improve: Reduced plugin output size by filtering Milestone SDK DLLs from build output
- Remove: Unnecessary `MilestoneSystems.VideoOS.Platform.SDK` references from CommunitySDK, SnapReport, Auditor, CertWatchdog

## [1.4.1] - 2026-03-09

- Add: Command line args for Smart Bar Programms
- Change: Use 6 args logclient ctor for broader compatiblity

## [1.4.0] - 2026-03-09

- Add: HTTP Requests Plugin

## [1.3.0] - 2026-03-08

- Add: RTSP Driver
- Add: Smart Bar Plugin

## [1.1.0] - 2026-03-07

- Add: Auditor Plugin
- Add: CommunitySDK shared library (CrossMessageHandler, SystemLogBase, PluginLog)
- Add: Help page to CertWatchdog
- Update: Migrate plugins to CommunitySDK (CrossMessageHandler, PluginLog)
- Update: Improve monitor capture via GDI capture
- Update: Change icons to FontAwesome
- Update: Clean up dead code and convert RTMPDriver to SDK-style project
- Update: Docs

## [1.0.1] - 2026-03-06

- Add: RDP Port (RDP Smart Client Plugin)


## [1.0.0] - 2026-03-05

- Add: Snaphshot Reporter Plugin
- Add: RTMP Desktop Streamer Plugin
- Update: Optimize Github Workflows
- Update: Docs

Previous Changes are on Github