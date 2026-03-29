---
hide:
  - navigation
  - toc
---

<style>
  .plugin-list {
    border: 1px solid #30363d;
    border-radius: 6px;
    overflow: hidden;
    margin-top: 0.75rem;
    margin-bottom: 2rem;
  }
  .plugin-list a.pr {
    display: flex;
    align-items: center;
    gap: 10px;
    padding: 7px 12px;
    background: transparent;
    text-decoration: none !important;
    color: inherit !important;
    transition: background 0.12s;
    border-bottom: 1px solid #21262d;
  }
  .plugin-list a.pr:last-child {
    border-bottom: none;
  }
  .plugin-list a.pr:hover {
    background: #161b22;
  }
  .pi {
    width: 28px;
    height: 28px;
    border-radius: 6px;
    display: flex;
    align-items: center;
    justify-content: center;
    flex-shrink: 0;
    font-size: 14px;
  }
  .pi.sc { background: #1e3a2a; color: #3fb950; }
  .pi.dd { background: #3a2e1a; color: #d29922; }
  .pi.ap { background: #2a1e3a; color: #bc8cff; }
  .pt { flex: 1; min-width: 0; }
  .pn {
    display: block;
    font-weight: 500;
    font-size: 0.68rem;
    line-height: 1.25;
    color: #e6edf3;
  }
  .pd {
    display: block;
    color: #8b949e;
    font-size: 0.58rem;
    font-weight: 400;
    line-height: 1.3;
  }
  #smart-client-plugins{
        margin-top: -40px;
  }
</style>

<link rel="stylesheet" href="https://cdn.jsdelivr.net/npm/@mdi/font@7/css/materialdesignicons.min.css">

### Smart Client Plugins

<div class="plugin-list">
  <a class="pr" href="smart-client/flexview/">
    <div class="pi sc"><i class="mdi mdi-grid"></i></div>
    <div class="pt"><span class="pn">Flex View</span><span class="pd">Design custom view layouts beyond the standard view templates.</span></div>
  </a>
  <a class="pr" href="smart-client/monitor-rtmp-streamer/">
    <div class="pi sc"><i class="mdi mdi-monitor-share"></i></div>
    <div class="pt"><span class="pn">Monitor RTMP Streamer</span><span class="pd">Capture desktop monitors and stream via RTMP</span></div>
  </a>
  <a class="pr" href="smart-client/notepad/">
    <div class="pi sc"><i class="mdi mdi-note-text"></i></div>
    <div class="pt"><span class="pn">Notepad</span><span class="pd">Simple text editor for operator notes</span></div>
  </a>
  <a class="pr" href="smart-client/rdp/">
    <div class="pi sc"><i class="mdi mdi-remote-desktop"></i></div>
    <div class="pt"><span class="pn">RDP</span><span class="pd">Embedded interactive Remote Desktop sessions</span></div>
  </a>
  <a class="pr" href="smart-client/sc-remote-control/">
    <div class="pi sc"><i class="mdi mdi-remote"></i></div>
    <div class="pt"><span class="pn">Remote Control</span><span class="pd">Control Smart Client remotely via REST API with Swagger UI</span></div>
  </a>
  <a class="pr" href="smart-client/smartbar/">
    <div class="pi sc"><i class="mdi mdi-ballot"></i></div>
    <div class="pt"><span class="pn">Smart Bar</span><span class="pd">A hotkey-opened command palette for quickly finding and launching cameras, views, commands, and programs with keyboard control, recent items, and undo history.</span></div>
  </a>
  <a class="pr" href="smart-client/snapreport/">
    <div class="pi sc"><i class="mdi mdi-file-pdf-box"></i></div>
    <div class="pt"><span class="pn">Snapshot Report</span><span class="pd">Camera snapshot PDF report generator</span></div>
  </a>
  <a class="pr" href="smart-client/timelapse/">
    <div class="pi sc"><i class="mdi mdi-timelapse"></i></div>
    <div class="pt"><span class="pn">Timelapse</span><span class="pd">Generate timelapse videos from recorded cameras with multi-camera stitch support</span></div>
  </a>
  <a class="pr" href="smart-client/view-carousel/">
    <div class="pi sc"><i class="mdi mdi-view-carousel"></i></div>
    <div class="pt"><span class="pn">View Carousel</span><span class="pd">Cycle through Smart Client views inside a single view item slot</span></div>
  </a>
  <a class="pr" href="smart-client/weather/">
    <div class="pi sc"><i class="mdi mdi-weather-partly-cloudy"></i></div>
    <div class="pt"><span class="pn">Weather</span><span class="pd">Live weather display powered by Open-Meteo</span></div>
  </a>
</div>

### Device Drivers

<div class="plugin-list">
  <a class="pr" href="device-drivers/rtmp/">
    <div class="pi dd"><i class="mdi mdi-video-input-antenna"></i></div>
    <div class="pt"><span class="pn">RTMP Push Driver</span><span class="pd">Receive RTMP/RTMPS push streams (H.264) directly into XProtect™</span></div>
  </a>
  <a class="pr" href="device-drivers/rtsp/">
    <div class="pi dd"><i class="mdi mdi-cctv"></i></div>
    <div class="pt"><span class="pn">RTSP Driver</span><span class="pd">Pull RTSP streams (H.264/H.265 + audio) from IP cameras with dual streams, rich status frames, and diagnostics.</span></div>
  </a>
</div>

### Admin Plugins

<div class="plugin-list">
  <a class="pr" href="admin/auditor/">
    <div class="pi ap"><i class="mdi mdi-shield-check"></i></div>
    <div class="pt"><span class="pn">Auditor</span><span class="pd">Audit user access to recorded video with per-user rules</span></div>
  </a>
  <a class="pr" href="admin/cert-watchdog/">
    <div class="pi ap"><i class="mdi mdi-certificate"></i></div>
    <div class="pt"><span class="pn">Certificate Watchdog</span><span class="pd">Monitor SSL certificate expiry for all HTTPS endpoints</span></div>
  </a>
  <a class="pr" href="admin/http-requests/">
    <div class="pi ap"><i class="mdi mdi-send"></i></div>
    <div class="pt"><span class="pn">HTTP Requests</span><span class="pd">HTTP requests actions that are more powerful and flexible.</span></div>
  </a>
  <a class="pr" href="admin/rtmp-streamer/">
    <div class="pi ap"><i class="mdi mdi-broadcast"></i></div>
    <div class="pt"><span class="pn">RTMP Streamer</span><span class="pd">Stream cameras to RTMP destinations (YouTube, Twitch, Facebook)</span></div>
  </a>
</div>
