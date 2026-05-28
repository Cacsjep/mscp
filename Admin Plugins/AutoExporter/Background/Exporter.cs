using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using CommunitySDK;
using VideoOS.Platform;
using VideoOS.Platform.Data;

namespace AutoExporter.Background
{
    internal enum ExportFormat { XProtect, Avi }

    internal class ExportJobConfig
    {
        public Guid JobObjectId;
        public string JobName;
        public ExportFormat Format;
        public bool Encrypt;
        public string Password;
        public bool IncludePlayer;
        public bool IncludeAudio;
        public List<Item> Cameras = new List<Item>();
        public DateTime RangeStartUtc;
        public DateTime RangeEndUtc;
        public string OutputFolder;   // <storage>\<JobName>\<timestamp>\
    }

    internal class ExportRunResult
    {
        public bool Success;
        public string Error;
        public long BytesWritten;
        public int CameraCount;
    }

    /// <summary>
    /// Wraps Milestone's DBExporter (XProtect) and AVIExporter so the
    /// BackgroundPlugin can drive an export with one call and a progress
    /// callback. Polls Progress on a worker thread; cancellation triggers
    /// IExporter.Cancel and waits up to a few seconds for shutdown.
    /// </summary>
    internal class Exporter
    {
        private static readonly PluginLog _log = new PluginLog("AutoExporter.Exporter");

        public ExportRunResult Run(
            ExportJobConfig cfg,
            Action<int, int, string> onProgress,   // (cameraIndex, percent, cameraName)
            CancellationToken ct)
        {
            if (cfg == null) return Fail("Null config");
            if (cfg.Cameras == null || cfg.Cameras.Count == 0)
                return Fail("No cameras to export");

            try
            {
                Directory.CreateDirectory(cfg.OutputFolder);
            }
            catch (Exception ex)
            {
                return Fail("Cannot create output folder: " + ex.Message);
            }

            try
            {
                if (cfg.Format == ExportFormat.XProtect)
                    return RunDb(cfg, onProgress, ct);
                else
                    return RunAvi(cfg, onProgress, ct);
            }
            catch (NoVideoInTimeSpanMIPException)
            {
                _log.Info($"Job '{cfg.JobName}' had no video in time span");
                return Success(cfg.OutputFolder, cfg.Cameras.Count); // empty range is not a failure
            }
            catch (Exception ex)
            {
                _log.Error($"Job '{cfg.JobName}' threw: {ex.Message}", ex);
                return Fail(ex.Message);
            }
        }

        // ─── XProtect (DBExporter) ──────────────────────────

        private ExportRunResult RunDb(ExportJobConfig cfg, Action<int, int, string> onProgress, CancellationToken ct)
        {
            _log.Info($"DBExporter starting: job='{cfg.JobName}' cameras={cfg.Cameras.Count} " +
                      $"encrypt={cfg.Encrypt} includePlayer={cfg.IncludePlayer} includeAudio={cfg.IncludeAudio} " +
                      $"output='{cfg.OutputFolder}'");

            DBExporter exporter = null;
            try
            {
                exporter = new DBExporter(cfg.IncludePlayer)
                {
                    Encryption = cfg.Encrypt,
                    EncryptionStrength = EncryptionStrength.AES128,
                    Password = cfg.Password ?? "",
                    SignExport = false,
                    PreventReExport = false,
                    IncludeBookmarks = false,
                    FailOnInvalidSignature = false
                };

                exporter.Init();
                exporter.Path = cfg.OutputFolder;
                exporter.CameraList.AddRange(cfg.Cameras);

                if (cfg.IncludeAudio)
                {
                    var audio = cfg.Cameras
                        .SelectMany(c => SafeRelated(c))
                        .Where(x => x.FQID.Kind == Kind.Microphone || x.FQID.Kind == Kind.Speaker)
                        .ToList();
                    exporter.AudioList.AddRange(audio);
                }

                if (!exporter.StartExport(cfg.RangeStartUtc, cfg.RangeEndUtc))
                {
                    var msg = exporter.LastErrorString ?? "Unknown error";
                    return Fail($"StartExport failed: {msg} ({exporter.LastError})");
                }

                WaitForCompletion(exporter, cfg.Cameras.FirstOrDefault()?.Name ?? "", 0, onProgress, ct);

                if (exporter.LastError > 0)
                    return Fail($"{exporter.LastErrorString} ({exporter.LastError})");

                long bytes = MeasureSize(cfg.OutputFolder);
                return Success(cfg.OutputFolder, cfg.Cameras.Count, bytes);
            }
            finally
            {
                SafeEnd(exporter);
            }
        }

        // ─── AVI (per camera) ───────────────────────────────

        private ExportRunResult RunAvi(ExportJobConfig cfg, Action<int, int, string> onProgress, CancellationToken ct)
        {
            int total = cfg.Cameras.Count;
            _log.Info($"AVIExporter starting: job='{cfg.JobName}' cameras={total} includeAudio={cfg.IncludeAudio} output='{cfg.OutputFolder}'");

            for (int i = 0; i < total; i++)
            {
                ct.ThrowIfCancellationRequested();
                var cam = cfg.Cameras[i];
                var name = MakeSafeFileName(cam?.Name ?? $"camera_{i}");
                var camDir = Path.Combine(cfg.OutputFolder, name);
                Directory.CreateDirectory(camDir);
                var filename = Path.Combine(camDir, name + ".avi");

                _log.Info($"  AVI camera {i + 1}/{total}: '{cam?.Name}' → '{filename}'");

                AVIExporter exporter = null;
                try
                {
                    exporter = new AVIExporter { Filename = filename };
                    exporter.Init();
                    exporter.Path = camDir;
                    exporter.CameraList.Add(cam);

                    if (cfg.IncludeAudio)
                    {
                        var audio = SafeRelated(cam)
                            .Where(x => x.FQID.Kind == Kind.Microphone || x.FQID.Kind == Kind.Speaker)
                            .ToList();
                        exporter.AudioList.AddRange(audio);
                    }

                    if (!exporter.StartExport(cfg.RangeStartUtc, cfg.RangeEndUtc))
                    {
                        var msg = exporter.LastErrorString ?? "Unknown error";
                        return Fail($"Camera '{cam?.Name}': {msg} ({exporter.LastError})");
                    }

                    WaitForCompletion(exporter, cam?.Name ?? "", i, onProgress, ct);

                    if (exporter.LastError > 0)
                        return Fail($"Camera '{cam?.Name}': {exporter.LastErrorString} ({exporter.LastError})");
                }
                finally
                {
                    SafeEnd(exporter);
                }
            }

            long bytes = MeasureSize(cfg.OutputFolder);
            return Success(cfg.OutputFolder, total, bytes);
        }

        // ─── Shared ─────────────────────────────────────────

        private static void WaitForCompletion(IExporter exporter, string cameraName, int cameraIndex,
            Action<int, int, string> onProgress, CancellationToken ct)
        {
            while (true)
            {
                if (ct.IsCancellationRequested)
                {
                    try { exporter.Cancel(); } catch { }
                    ct.ThrowIfCancellationRequested();
                }

                int p = exporter.Progress;
                if (p < 0) p = 0;
                onProgress?.Invoke(cameraIndex, p, cameraName);

                if (p >= 100 || exporter.LastError > 0) return;

                Thread.Sleep(250);
            }
        }

        private static void SafeEnd(IExporter exporter)
        {
            if (exporter == null) return;
            try { exporter.EndExport(); } catch { }
            try { exporter.Close(); } catch { }
        }

        private static IEnumerable<Item> SafeRelated(Item cam)
        {
            try { return cam?.GetRelated() ?? Enumerable.Empty<Item>(); }
            catch { return Enumerable.Empty<Item>(); }
        }

        private static long MeasureSize(string folder)
        {
            long total = 0;
            try
            {
                foreach (var f in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
                {
                    try { total += new FileInfo(f).Length; } catch { }
                }
            }
            catch { }
            return total;
        }

        internal static string MakeSafeFileName(string s)
        {
            if (string.IsNullOrEmpty(s)) return "_";
            var invalid = Path.GetInvalidFileNameChars();
            var result = new System.Text.StringBuilder(s.Length);
            foreach (var c in s) result.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
            return result.ToString();
        }

        private static ExportRunResult Fail(string error) =>
            new ExportRunResult { Success = false, Error = error };

        private static ExportRunResult Success(string folder, int cameraCount, long bytes = 0) =>
            new ExportRunResult { Success = true, CameraCount = cameraCount, BytesWritten = bytes };
    }
}
