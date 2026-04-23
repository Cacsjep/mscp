using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using CommunitySDK;
using VideoOS.Platform;
using VideoOS.Platform.Data;
using VideoOS.Platform.Live;
using ZXing;
using ZXing.Common;

namespace BarcodeReaderHelper
{
    /// <summary>
    /// Per-channel decode pump: owns a JPEGLiveSource subscription and a single worker thread
    /// that pulls the most-recent JPEG, (optionally) downscales it, and runs it through ZXing.
    ///
    /// Design notes:
    ///  - Use a single-slot "latest frame" variable instead of a queue. If decoding is slower
    ///    than the camera's live FPS, we want to always decode the newest frame, not a stale
    ///    backlog. Unused frames are simply dropped  the camera's _observed_ fps vs the
    ///    helper's _decode_ fps is the signal the admin UI uses to recommend settings.
    ///  - JPEGLiveSource runs the LiveContentEvent on a Milestone thread. Keep that callback
    ///    non-blocking; hand off to the worker thread.
    ///  - Stats snapshot is emitted on a fixed interval (not per-frame) so we don't spam
    ///    stderr and the BackgroundPlugin's buffer.
    /// </summary>
    internal class DecodeLoop
    {
        internal class Config
        {
            public string ServerUri;
            public Guid CameraId;
            public string ItemId;

            public string FormatsCsv;
            public bool TryHarder;
            public bool AutoRotate;
            public bool TryInverted;

            public int TargetFps;
            public int DownscaleWidth;   // 0 = native
            public int DebounceMs;
            public bool CreateBookmarks;
            public string ChannelName;
        }

        // Pre/post window around the detection timestamp when creating a bookmark.
        // Matches the value surfaced in the admin UI description; keep in sync if changed.
        private static readonly TimeSpan BookmarkPreRoll  = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan BookmarkPostRoll = TimeSpan.FromSeconds(2);
        private const int BookmarkHeaderMaxChars = 60;

        private const int StatsIntervalMs = 1000;
        private const int InferenceSampleSize = 64;     // rolling window for avg + p95

        // Reconnect policy: if no frames have arrived from JPEGLiveSource for this long
        // while we think we're live, tear down and re-open the source. Chosen >> one
        // live-stream frame interval (slowest practical ~1 fps = 1000 ms) with headroom
        // for short network blips before we declare the pipe dead.
        private const int StallTimeoutMs = 8000;
        // After a reconnect attempt, wait this long before declaring the next stall  gives
        // the recorder time to push the first frame of the fresh subscription.
        private const int ReconnectSettleMs = 3000;

        private readonly Item _cameraItem;
        private readonly Config _cfg;
        private readonly PluginLog _log;
        private readonly BarcodeReader _reader;
        private readonly int _frameIntervalMs;          // 1000 / TargetFps, floor 1ms

        private JPEGLiveSource _src;
        private Thread _worker;
        private volatile bool _stopping;

        private readonly object _frameLock = new object();
        private byte[] _latestJpeg;                     // single-slot; overwritten on each new frame
        private DateTime _latestJpegTime;

        // Stats counters  all writes on worker thread, reads on stats thread. volatile/Interlocked
        // is overkill given we snapshot once per second and don't care about sub-second skew.
        private long _frames;
        private long _decoded;
        private long _failed;
        private DateTime _lastStatsTime = DateTime.UtcNow;
        private long _lastStatsFrames;
        private long _lastStatsDecoded;
        private long _camFrameCount;
        private DateTime _lastCamFrameStatsTime = DateTime.UtcNow;

        private readonly Queue<double> _inferenceSamples = new Queue<double>(InferenceSampleSize);
        private readonly object _sampleLock = new object();

        // Debounce: same barcode (format + text) logged at most once per DebounceMs.
        private readonly Dictionary<string, DateTime> _recentDetections = new Dictionary<string, DateTime>();

        private Timer _statsTimer;

        // Reconnect/stall tracking. UTC ticks for interlocked-safe R/W from the Milestone
        // callback thread (writer) and the stats timer thread (reader).
        private long _lastFrameTicks;
        private long _reconnectSettleUntilTicks;
        private int  _reconnecting;              // 0 = idle, 1 = in flight (Interlocked gate)
        private volatile string _publishedStatus = "Connecting";
        private readonly object _srcLock = new object();

        public DecodeLoop(Item cameraItem, Config cfg, PluginLog log)
        {
            _cameraItem = cameraItem;
            _cfg = cfg;
            _log = log;
            _frameIntervalMs = Math.Max(1, 1000 / Math.Max(1, cfg.TargetFps));

            _reader = new BarcodeReader
            {
                AutoRotate = cfg.AutoRotate,
                Options = new DecodingOptions
                {
                    TryHarder = cfg.TryHarder,
                    TryInverted = cfg.TryInverted,
                    PossibleFormats = ParseFormats(cfg.FormatsCsv)
                }
            };
        }

        public void Start()
        {
            Interlocked.Exchange(ref _lastFrameTicks, DateTime.UtcNow.Ticks);
            Interlocked.Exchange(ref _reconnectSettleUntilTicks, DateTime.UtcNow.AddMilliseconds(ReconnectSettleMs).Ticks);
            OpenSource();

            _worker = new Thread(WorkerLoop) { IsBackground = true, Name = "BarcodeDecode" };
            _worker.Start();

            _statsTimer = new Timer(_ => OnStatsTick(), null, StatsIntervalMs, StatsIntervalMs);
        }

        public void Stop()
        {
            _stopping = true;
            try { _statsTimer?.Dispose(); } catch { }
            CloseSource();
            try { _worker?.Join(2000); } catch { }
        }

        private void OpenSource()
        {
            lock (_srcLock)
            {
                if (_stopping) return;
                try
                {
                    _src = new JPEGLiveSource(_cameraItem) { SendInitialImage = false };
                    _src.LiveContentEvent += OnLiveContent;
                    _src.LiveStatusEvent  += OnLiveStatus;
                    _src.Init();
                    _src.LiveModeStart = true;
                    UpdateStatus("Running");
                }
                catch (Exception ex)
                {
                    _log.Error("OpenSource failed", ex);
                    UpdateStatus("Error:Connect");
                    // Leave _src dangling only after clean-up attempt.
                    try { CloseSourceInternal(); } catch { }
                }
            }
        }

        private void CloseSource()
        {
            lock (_srcLock) CloseSourceInternal();
        }

        private void CloseSourceInternal()
        {
            if (_src == null) return;
            try { _src.LiveContentEvent -= OnLiveContent; } catch { }
            try { _src.LiveStatusEvent  -= OnLiveStatus; } catch { }
            try { _src.LiveModeStart = false; } catch { }
            try { _src.Close(); } catch { }
            _src = null;
        }

        private void OnLiveContent(object sender, EventArgs e)
        {
            try
            {
                if (_stopping) return;
                if (!(e is LiveContentEventArgs liveContent)) return;
                if (liveContent.Exception != null) return;

                var content = liveContent.LiveContent;
                if (content?.Content is byte[] data && data.Length > 0)
                {
                    lock (_frameLock)
                    {
                        _latestJpeg = data;
                        _latestJpegTime = content.EndTime;
                    }
                    Interlocked.Increment(ref _camFrameCount);
                    Interlocked.Exchange(ref _lastFrameTicks, DateTime.UtcNow.Ticks);
                    if (_publishedStatus != "Running") UpdateStatus("Running");
                }
            }
            catch (Exception ex)
            {
                _log.Error("OnLiveContent error", ex);
            }
        }

        private void OnLiveStatus(object sender, EventArgs e)
        {
            // Intentionally silent. Milestone's LiveStatusEventArgs has no useful ToString
            // and just spammed the admin UI log with "LiveStatus: ...EventArgs" lines. The
            // stall watchdog owns reconnect decisions, so we don't need this feed.
        }

        private void WorkerLoop()
        {
            while (!_stopping)
            {
                var t0 = Stopwatch.GetTimestamp();
                byte[] jpeg = null;
                lock (_frameLock)
                {
                    if (_latestJpeg != null)
                    {
                        jpeg = _latestJpeg;
                        _latestJpeg = null;       // consume  don't decode same frame twice
                    }
                }

                if (jpeg != null)
                {
                    _frames++;
                    try { DecodeFrame(jpeg); }
                    catch (Exception ex) { _failed++; _log.Error("Decode error", ex); }
                }

                var elapsedMs = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
                var sleep = _frameIntervalMs - (int)elapsedMs;
                if (sleep > 0) Thread.Sleep(sleep);
                else if (jpeg == null) Thread.Sleep(5);  // no frame, don't spin
            }
        }

        private void DecodeFrame(byte[] jpegBytes)
        {
            Bitmap bitmap = null;
            Bitmap scaled = null;
            try
            {
                // GDI+ gotcha: Image.FromStream keeps a weak tie to the MemoryStream for the
                // lifetime of the Image. If we dispose the stream before ZXing reads pixels
                // (which is exactly what "using (var ms = ...)" did before), GDI+ returns
                // garbage from LockBits  ZXing then sees noise and decodes nothing, which
                // matched the "no detections ever" symptom we hit. Copy the decoded image
                // into a standalone Bitmap so the stream can safely go away.
                using (var ms = new MemoryStream(jpegBytes))
                using (var loaded = Image.FromStream(ms, useEmbeddedColorManagement: false, validateImageData: false))
                {
                    bitmap = new Bitmap(loaded.Width, loaded.Height, PixelFormat.Format24bppRgb);
                    using (var g = Graphics.FromImage(bitmap))
                    {
                        g.DrawImageUnscaled(loaded, 0, 0);
                    }
                }

                var toDecode = bitmap;
                if (_cfg.DownscaleWidth > 0 && bitmap.Width > _cfg.DownscaleWidth)
                {
                    scaled = Downscale(bitmap, _cfg.DownscaleWidth);
                    toDecode = scaled;
                }

                var t0 = Stopwatch.GetTimestamp();
                var result = _reader.Decode(toDecode);
                var ms_inf = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
                RecordInferenceSample(ms_inf);

                if (result != null)
                {
                    _decoded++;
                    EmitDetection(result.BarcodeFormat.ToString(), result.Text ?? "");
                }
                else
                {
                    _failed++;
                }
            }
            finally
            {
                try { scaled?.Dispose(); } catch { }
                try { bitmap?.Dispose(); } catch { }
            }
        }

        private static Bitmap Downscale(Bitmap src, int targetWidth)
        {
            // Only scale down, preserve aspect. Low-quality interpolation is intentional -
            // ZXing does its own thresholding/binarization, so lanczos/bicubic just burns CPU
            // without improving decode rates on real-world camera frames.
            var scale = (double)targetWidth / src.Width;
            var h = Math.Max(1, (int)(src.Height * scale));
            var dst = new Bitmap(targetWidth, h, PixelFormat.Format24bppRgb);
            using (var g = Graphics.FromImage(dst))
            {
                g.InterpolationMode = InterpolationMode.Low;
                g.PixelOffsetMode = PixelOffsetMode.HighSpeed;
                g.SmoothingMode = SmoothingMode.None;
                g.DrawImage(src, 0, 0, targetWidth, h);
            }
            return dst;
        }

        private void RecordInferenceSample(double ms)
        {
            lock (_sampleLock)
            {
                _inferenceSamples.Enqueue(ms);
                while (_inferenceSamples.Count > InferenceSampleSize) _inferenceSamples.Dequeue();
            }
        }

        private (double avg, double p95) GetInferenceStats()
        {
            double[] arr;
            lock (_sampleLock)
            {
                if (_inferenceSamples.Count == 0) return (0, 0);
                arr = _inferenceSamples.ToArray();
            }
            double sum = 0;
            for (int i = 0; i < arr.Length; i++) sum += arr[i];
            var avg = sum / arr.Length;

            Array.Sort(arr);
            var p95Idx = (int)Math.Min(arr.Length - 1, Math.Ceiling(arr.Length * 0.95) - 1);
            return (avg, arr[Math.Max(0, p95Idx)]);
        }

        private void EmitDetection(string format, string text)
        {
            var now = DateTime.UtcNow;

            if (_cfg.DebounceMs > 0)
            {
                var key = format + "|" + text;
                if (_recentDetections.TryGetValue(key, out var last) &&
                    (now - last).TotalMilliseconds < _cfg.DebounceMs)
                {
                    return;
                }
                _recentDetections[key] = now;

                // Garbage-collect the dedup map occasionally. Not strictly needed for small
                // volumes; cheap insurance for high-throughput barcode rain.
                if (_recentDetections.Count > 256) PruneDedup(now);
            }

            var b64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(text));
            Console.Error.WriteLine($"DETECT {DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()} {format} {b64}");

            if (_cfg.CreateBookmarks)
            {
                TryCreateBookmark(format, text, now);
            }
        }

        private void TryCreateBookmark(string format, string text, DateTime triggerUtc)
        {
            // Bookmark search in Smart Client matches on Header (and Description). Header
            // shows in the list, so put the decoded text there (truncated for readability).
            // Description carries format + channel for context.
            var header = text ?? "";
            if (header.Length > BookmarkHeaderMaxChars)
                header = header.Substring(0, BookmarkHeaderMaxChars - 3) + "...";

            var description = $"Format: {format} / Channel: {_cfg.ChannelName}";
            var reference = $"barcode-{_cfg.ItemId}-{new DateTimeOffset(triggerUtc).ToUnixTimeMilliseconds()}";
            var start = triggerUtc - BookmarkPreRoll;
            var end = triggerUtc + BookmarkPostRoll;
            var cameraFqid = _cameraItem.FQID;

            // Fire and forget: BookmarkCreate is a server round-trip. If we ran it on
            // the decode worker the next frame's decode would be delayed by whatever
            // latency the management server introduces. The worker drops frames anyway
            // when it can't keep up, so blocking decoding on persistence would directly
            // hurt detection rate on a slow server.
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    BookmarkService.Instance.BookmarkCreate(
                        cameraFqid, start, triggerUtc, end,
                        reference, header, description);
                }
                catch (Exception ex)
                {
                    _log.Error("BookmarkCreate failed", ex);
                }
            });
        }

        private void PruneDedup(DateTime now)
        {
            var cutoff = _cfg.DebounceMs;
            var stale = new List<string>();
            foreach (var kv in _recentDetections)
            {
                if ((now - kv.Value).TotalMilliseconds > cutoff) stale.Add(kv.Key);
            }
            foreach (var k in stale) _recentDetections.Remove(k);
        }

        private void OnStatsTick()
        {
            if (_stopping) return;
            CheckStallAndReconnect();
            EmitStats();
        }

        private void CheckStallAndReconnect()
        {
            // Respect the post-reconnect grace window so we don't immediately declare a
            // stall while the new subscription is still warming up.
            var now = DateTime.UtcNow;
            var settleTicks = Interlocked.Read(ref _reconnectSettleUntilTicks);
            if (now.Ticks < settleTicks) return;

            var lastTicks = Interlocked.Read(ref _lastFrameTicks);
            var sinceMs = (now - new DateTime(lastTicks, DateTimeKind.Utc)).TotalMilliseconds;
            if (sinceMs < StallTimeoutMs) return;

            // Single-flight gate: only one reconnect in flight at a time.
            if (Interlocked.CompareExchange(ref _reconnecting, 1, 0) != 0) return;

            UpdateStatus("Error:NoFrames");
            _log.Info($"No frames for {sinceMs:F0} ms  reconnecting JPEGLiveSource");

            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    CloseSource();
                    OpenSource();
                    Interlocked.Exchange(ref _reconnectSettleUntilTicks,
                        DateTime.UtcNow.AddMilliseconds(ReconnectSettleMs).Ticks);
                    Interlocked.Exchange(ref _lastFrameTicks, DateTime.UtcNow.Ticks);
                }
                catch (Exception ex)
                {
                    _log.Error("Reconnect failed", ex);
                    UpdateStatus("Error:Reconnect");
                }
                finally
                {
                    Interlocked.Exchange(ref _reconnecting, 0);
                }
            });
        }

        private void UpdateStatus(string status)
        {
            // Coalesce duplicates so the parent BackgroundPlugin doesn't log the same
            // STATUS line over and over while the stall persists.
            if (_publishedStatus == status) return;
            _publishedStatus = status;
            try { Program.WriteStatus(status); } catch { }
        }

        private void EmitStats()
        {
            if (_stopping) return;
            try
            {
                var now = DateTime.UtcNow;
                var windowSec = (now - _lastStatsTime).TotalSeconds;
                if (windowSec <= 0) return;

                var framesDelta  = _frames  - _lastStatsFrames;
                var decodedDelta = _decoded - _lastStatsDecoded;
                var decodeFps    = framesDelta  / windowSec;
                var okFps        = decodedDelta / windowSec;

                _lastStatsTime    = now;
                _lastStatsFrames  = _frames;
                _lastStatsDecoded = _decoded;

                var camWin = (now - _lastCamFrameStatsTime).TotalSeconds;
                var camFps = 0.0;
                if (camWin > 0)
                {
                    var camFrames = Interlocked.Exchange(ref _camFrameCount, 0);
                    camFps = camFrames / camWin;
                    _lastCamFrameStatsTime = now;
                }

                var (avg, p95) = GetInferenceStats();
                var maxFps = avg > 0 ? 1000.0 / avg : 0.0;

                var inv = System.Globalization.CultureInfo.InvariantCulture;
                Console.Error.WriteLine(string.Format(inv,
                    "STATS frames={0} decoded={1} failed={2} cam_fps={3:F1} decode_fps={4:F1} ok_fps={5:F1} inf_ms_avg={6:F2} inf_ms_p95={7:F2} max_fps={8:F1}",
                    _frames, _decoded, _failed, camFps, decodeFps, okFps, avg, p95, maxFps));
            }
            catch (Exception ex)
            {
                _log.Error("EmitStats error", ex);
            }
        }

        private static BarcodeFormat[] ParseFormats(string csv)
        {
            var list = new List<BarcodeFormat>();
            if (string.IsNullOrWhiteSpace(csv)) csv = DefaultFormatsCsv;

            foreach (var raw in csv.Split(','))
            {
                var s = raw.Trim().ToLowerInvariant();
                if (s.Length == 0) continue;
                switch (s)
                {
                    case "qr": case "qrcode": case "qr_code": list.Add(BarcodeFormat.QR_CODE); break;
                    case "dm": case "datamatrix": case "data_matrix": list.Add(BarcodeFormat.DATA_MATRIX); break;
                    case "aztec": list.Add(BarcodeFormat.AZTEC); break;
                    case "pdf417": case "pdf_417": list.Add(BarcodeFormat.PDF_417); break;
                    case "code128": case "code_128": list.Add(BarcodeFormat.CODE_128); break;
                    case "code39":  case "code_39":  list.Add(BarcodeFormat.CODE_39); break;
                    case "code93":  case "code_93":  list.Add(BarcodeFormat.CODE_93); break;
                    case "ean13":   case "ean_13":   list.Add(BarcodeFormat.EAN_13); break;
                    case "ean8":    case "ean_8":    list.Add(BarcodeFormat.EAN_8); break;
                    case "upca":    case "upc_a":    list.Add(BarcodeFormat.UPC_A); break;
                    case "upce":    case "upc_e":    list.Add(BarcodeFormat.UPC_E); break;
                    case "itf":     list.Add(BarcodeFormat.ITF); break;
                    case "codabar": list.Add(BarcodeFormat.CODABAR); break;
                }
            }
            if (list.Count == 0) list.Add(BarcodeFormat.QR_CODE);
            return list.ToArray();
        }

        private const string DefaultFormatsCsv =
            "qr,data_matrix,aztec,pdf417,code128,code39,code93,ean13,ean8,upca,upce,itf,codabar";
    }
}
