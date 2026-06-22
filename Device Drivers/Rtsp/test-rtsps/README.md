# RTSPS test server (MediaMTX)

A throwaway RTSP/RTSPS server for exercising the RTSP Driver, in particular the
**RTSPS (RTSP over TLS)** transport options. It generates a continuous H.264 +
AAC test pattern and serves it over both plain RTSP and RTSPS with a self-signed
certificate, so you do not need a real camera that speaks RTSPS.

## What it exposes

| URL | Transport | Notes |
|---|---|---|
| `rtsp://<host>:8554/test`  | plain RTSP | for a baseline / non-TLS check |
| `rtsps://<host>:8322/test` | RTSP over TLS | self-signed cert (use the "Untrusted" driver option) |

Video: 1280x720 @ 15 fps H.264. Audio: 1 kHz AAC tone (also exercises the microphone device).

## Run it

From this folder:

```bash
docker compose up --build
```

The self-signed certificate is generated at image build time, so there is nothing
to manage on disk. Stop with `Ctrl+C` (or `docker compose down`).

## Point the driver at it

In Management Client, add the RTSP Driver hardware (or reuse an instance), then on a channel:

| Setting | Value |
|---|---|
| Hardware IP | the Docker host (e.g. `127.0.0.1` if Docker runs on the Recording Server, otherwise the host LAN IP) |
| RTSP Port | `8322` |
| RTSP Path (Stream 1) | `/test` |
| Transport Protocol | **RTSPS Untrusted (TLS, skip certificate check)** |
| Channel Enabled | `true` |

The certificate is self-signed, so use the **Untrusted** option. The plain
**RTSPS** (verify) option will fail certificate validation, which is the expected
behavior and a good negative test.

For a plain-RTSP baseline instead: RTSP Port `8554`, path `/test`, Transport `TCP`.

> The Add Hardware wizard port is the HTTP management port and is unused by this driver;
> any value is fine. No credentials are required (the server has no auth), so leave the
> username/password blank if the wizard allows it.

## Verify independently first (optional)

Before the driver, confirm the stream works with VLC (accept the self-signed cert prompt):

```
vlc rtsps://127.0.0.1:8322/test
```

or with ffmpeg:

```
ffplay -rtsp_transport tcp rtsps://127.0.0.1:8322/test
```

## What to expect in the driver log

`C:\ProgramData\Milestone\XProtect Recording Server\Logs\DriverFramework_RTSPDriver.log`

```
Started channel N stream 1 url=rtsps://...:8322/test transport=rtsps-untrusted
RtspClientWorker[N]: Connected codec=H.264/AVC 1280x720 transport=rtsps-untrusted ... streaming
```

The URL scheme is `rtsps://` and transport reads `rtsps-untrusted` (forced over TCP internally).

## Notes

- Docker Desktop on Windows publishes the ports to `localhost`. RTSPS is TLS-over-TCP,
  so it maps cleanly; for the plain-RTSP baseline use **TCP** transport to avoid UDP-over-NAT issues.
- This folder is a standalone test aid. It is not part of the plugin build and is not
  shipped in the installer.
