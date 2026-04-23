---
title: "Barcode Reader Plugin for Milestone XProtect"
description: "Barcode Reader plugin for Milestone XProtect - decode QR codes and 1D/2D barcodes from live camera streams. Stores detections as searchable bookmarks and fires rule-engine events for specific known codes."
---

<div class="show-title" markdown>

# Barcode Reader

Decode QR codes and 1D/2D barcodes from live camera streams. Each detection creates a searchable Milestone bookmark and fires analytics events that the Rules engine can act on. Includes a QR Code library where you can generate, preview and export codes whose payload acts as a rule trigger.

## Quick Start

1. Open the **Management Client**
2. Navigate to **MIP Plug-ins &gt; Barcode Reader &gt; Barcode Channels**
3. Right-click **Barcode Channels** &rarr; **Create New**
4. Pick a camera, leave defaults, click **Save**
5. Hold a barcode in front of the camera; detections appear in the Live Status log and on the camera timeline as bookmarks

## Barcode Channels

Each Barcode Channel configures one running decoder, bound to one camera. A helper process runs inside the Event Server per enabled channel, pulling JPEG frames and running them through ZXing.

### Channel fields

| Setting | Purpose |
|---|---|
| **Camera** | Which camera to decode from |
| **Enabled** | Launch / stop the helper process for this channel |
| **Camera Preview** | Live MJPEG preview of the selected camera - the decoder sees the same frames |
| **Barcode formats** | Restrict to expected code types (fewer = faster). QR Code, Data Matrix, Aztec, PDF417, Code 128/39/93, EAN-13/8, UPC-A/E, ITF, Codabar |
| **Try Harder** | Spend more CPU per frame to catch low-contrast, partially blocked, or rotated codes |
| **Auto Rotate** | Retry each frame rotated 90/180/270 degrees. Useful for sideways-mounted cameras |
| **Try Inverted** | Retry with inverted colours for white-on-black codes |
| **Create bookmarks for detections** | One bookmark per detection (2 s pre-roll / 2 s post-roll on the camera timeline) |
| **Target frame rate** | Hard cap on decode attempts per second (real rate also capped by camera live FPS and decode time) |
| **Downscale to width** | Resize each frame before decoding. Big speed-up for 4K/8K cameras |
| **Duplicate debounce** | Suppress repeat detections of the same text within this window (ms) |

### Live Status panel

While a channel is selected the bottom of the detail pane shows:

- **Cam FPS** - frames delivered by the camera per second
- **Decode FPS** - frames actually run through the decoder per second
- **Inference avg / p95** - time spent in ZXing per frame
- **Max FPS** - theoretical ceiling derived from inference time
- **Hint line** - green when Target FPS has headroom, amber at the limit
- **Helper log** - last 40 stderr lines incl. `DETECT` entries (newest first)

### Camera offline / reconnect

If no frames arrive for ~8 seconds while the channel is live, the helper tears down and re-opens the subscription in place. Status transitions (`Running` &rarr; `Error:NoFrames` &rarr; `Running`) show up in the Live Status header. If the helper process itself dies, the Event Server background plugin restarts it within 10 seconds.

## QR Codes

The **QR Codes** node stores a library of known codes. Each item pairs a friendly name with an exact **Payload** string; when a scanner decodes matching text, a dedicated **QR Code Matched** event fires with that QR Code item as the source - letting you attach a rule to a specific code.

### Generating codes

The detail pane regenerates a QR preview live as you type the payload:

- **Copy PNG** - 256 px PNG to the clipboard
- **Save PNG…** - 512 px PNG to disk (suggested filename from the item name)

### Error correction

Higher levels recover from more dirt/damage but produce denser codes.

| Level | Recoverable damage |
|---|---|
| L | ~7% |
| M | ~15% (default) |
| Q | ~25% |
| H | ~30% |

### Payload uniqueness

Each QR Code must have a unique payload. The admin client refuses to save a second item with the same payload text and shows `Payload is already used by '<name>'`. This keeps rule targeting unambiguous.

## Events

Register these on the Rules engine under event group **Barcode Reader**:

| Event | Source kind | Fires when |
|---|---|---|
| **Barcode Detected** | Barcode Channel | any post-debounce decode on that channel |
| **QR Code Matched** | QR Code | decoded text equals the QR Code item's Payload (exact, case-sensitive) |

The decoded text is attached to the event via `CustomTag` (bare text). Use these events to trigger emails, alarms, PTZ presets, HTTP callbacks - anything Rules supports.

### Example rule - "Open barrier on token"

1. Create a QR Code item named "Gate Token" with `Payload = GATE-TOKEN-42`
2. Print the generated PNG, stick it on a delivery vehicle
3. In the Management Client go to **Rules** &rarr; **Add Rule** &rarr; **Perform an action on &lt;event&gt;**
4. Pick **QR Code Matched** under **Barcode Reader**, source = **Gate Token**
5. Pick an action (output trigger, preset, generic event, etc.)

## Persistence & search

- **Bookmarks** - each detection becomes a Milestone bookmark on the camera timeline. Header = decoded text (truncated to ~60 chars). Description = `Format: {FORMAT} / Channel: {channel name}`. Searchable in Smart Client's **Search** workspace via the **Bookmarks** filter.
- **Event Server log** - per-channel `DETECT` / `STATS` / `STATUS` lines in `C:\ProgramData\Milestone\XProtect Event Server\Logs\`
- **Milestone System Log** - one entry per channel start / error / stop / helper crash

## Troubleshooting

| Problem | Fix |
|---|---|
| No detections | Narrow the format list; enable Try Harder; raise Target FPS |
| High CPU | Lower Target FPS; turn off Try Harder / Auto Rotate; enable Downscale |
| Status stuck on `Error:Connect` | Camera or Recording Server unreachable. Helper auto-retries |
| Status stuck on `Error:NoFrames` | Stall watchdog tripped. Check camera health; reconnect is automatic |
| `Helper exe not found` | Ensure `BarcodeReaderHelper.exe` lives next to `BarcodeReader.dll` in `MIPPlugins\BarcodeReader` |
| Bookmarks not appearing | Verify **Create bookmarks for detections** is checked; check the Event Server log for `BookmarkCreate failed` |
| QR Code Matched not firing | Remember match is exact + case-sensitive. Compare the `DETECT` log line payload against the stored QR Code item |

</div>
