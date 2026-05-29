using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Threading;
using CommunitySDK;
using VideoOS.Platform;

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
        public List<JobTargetSpec> Targets = new List<JobTargetSpec>();
        public DateTime RangeStartUtc;
        public DateTime RangeEndUtc;
        public string OutputFolder;
    }

    internal class JobTargetSpec
    {
        public string Kind;       // "Camera" or "Group"
        public Guid ObjectId;
        public string Name;
    }

    internal class ExportRunResult
    {
        public bool Success;
        public string Error;
        public long BytesWritten;
        public int CameraCount;
        public List<string> CameraNames = new List<string>();
    }

    /// <summary>
    /// Spawns AutoExporterHelper.exe to perform the actual export (DBExporter /
    /// AVIExporter only work in a standalone SDK environment, not the Event
    /// Server's Service environment). Pipes the helper's stderr, parses
    /// PROGRESS lines, and reads the result JSON the helper writes on exit.
    /// </summary>
    internal class Exporter
    {
        private static readonly PluginLog _log = new PluginLog("AutoExporter.Exporter");
        private const string HelperExeName = "AutoExporterHelper.exe";
        private const int ExitWaitMs = 5 * 60 * 1000;   // upper bound on wait after we send Cancel

        public ExportRunResult Run(
            ExportJobConfig cfg,
            string serverUri,
            Action<int, int, string> onProgress,
            CancellationToken ct)
        {
            if (cfg == null) return Fail("Null config");
            if (cfg.Targets == null || cfg.Targets.Count == 0) return Fail("No targets configured");
            if (string.IsNullOrWhiteSpace(serverUri)) return Fail("No server URI available (Event Server not connected to MasterSite yet)");

            string helperExe = ResolveHelperExe();
            if (helperExe == null)
                return Fail($"{HelperExeName} not found next to plugin DLL — installer or dev-deploy didn't ship it");

            try { Directory.CreateDirectory(cfg.OutputFolder); }
            catch (Exception ex) { return Fail("Cannot create output folder: " + ex.Message); }

            string runDir, configPath, resultPath;
            try
            {
                runDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "MSCPlugins", "AutoExporter", "runs");
                Directory.CreateDirectory(runDir);
                configPath = Path.Combine(runDir, $"{Guid.NewGuid():N}.json");
                resultPath = configPath + ".result.json";
                WriteRequest(configPath, cfg);
            }
            catch (Exception ex) { return Fail("Failed to stage helper request: " + ex.Message); }

            int exitCode;
            try
            {
                _log.Info($"Spawning helper: '{helperExe}' configPath='{configPath}' cameras (targets)={cfg.Targets.Count}");
                exitCode = SpawnHelper(helperExe, serverUri, configPath, onProgress, ct);
                _log.Info($"Helper exited: code={exitCode}");
            }
            catch (OperationCanceledException)
            {
                TryDelete(configPath); TryDelete(resultPath);
                return Fail("Cancelled");
            }
            catch (Exception ex)
            {
                TryDelete(configPath); TryDelete(resultPath);
                return Fail("Helper launch failed: " + ex.Message);
            }

            HelperResult result;
            try { result = ReadResult(resultPath); }
            catch (Exception ex)
            {
                TryDelete(configPath); TryDelete(resultPath);
                return Fail($"Helper exit={exitCode}, result file unreadable: {ex.Message}");
            }
            finally
            {
                TryDelete(configPath);
                TryDelete(resultPath);
            }

            return new ExportRunResult
            {
                Success      = result.Success && exitCode == 0,
                Error        = result.Error ?? (exitCode != 0 ? $"Helper exit code {exitCode}" : ""),
                BytesWritten = result.BytesWritten,
                CameraCount  = result.CameraCount,
                CameraNames  = result.CameraNames?.ToList() ?? new List<string>()
            };
        }

        // ─── Process management ────────────────────────────

        private static int SpawnHelper(string exe, string serverUri, string configPath,
            Action<int, int, string> onProgress, CancellationToken ct)
        {
            var psi = new ProcessStartInfo
            {
                FileName               = exe,
                Arguments              = $"\"{serverUri}\" \"{configPath}\"",
                CreateNoWindow         = true,
                UseShellExecute        = false,
                RedirectStandardError  = true,
                RedirectStandardOutput = true
            };

            using (var proc = Process.Start(psi))
            {
                if (proc == null) throw new InvalidOperationException("Process.Start returned null");

                proc.ErrorDataReceived += (s, e) =>
                {
                    if (string.IsNullOrEmpty(e.Data)) return;
                    var parsed = HelperProgressParser.Parse(e.Data);
                    if (parsed.Kind == HelperProgressParser.HelperLine.LineKind.Progress)
                    {
                        try { onProgress?.Invoke(parsed.CameraIndex, parsed.Percent, parsed.CameraName); }
                        catch { /* progress callback must not break the helper loop */ }
                    }
                    else
                    {
                        _log.Info($"[helper] {e.Data}");
                    }
                };
                proc.OutputDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data)) _log.Info($"[helper:out] {e.Data}");
                };

                proc.BeginErrorReadLine();
                proc.BeginOutputReadLine();

                while (!proc.HasExited)
                {
                    if (ct.IsCancellationRequested)
                    {
                        try { if (!proc.HasExited) proc.Kill(); } catch { }
                        ct.ThrowIfCancellationRequested();
                    }
                    if (!proc.WaitForExit(250)) continue;
                }
                proc.WaitForExit(ExitWaitMs);   // flush async readers
                return proc.ExitCode;
            }
        }

        // ─── Resolve helper exe path ────────────────────────

        private static string ResolveHelperExe()
        {
            var dir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            if (string.IsNullOrEmpty(dir)) return null;
            var path = Path.Combine(dir, HelperExeName);
            return File.Exists(path) ? path : null;
        }

        // ─── Serialization (matches AutoExporterHelper.HelperContract) ─

        private static void WriteRequest(string path, ExportJobConfig cfg)
        {
            var req = new HelperRequest
            {
                RunId         = Guid.NewGuid().ToString(),
                JobName       = cfg.JobName,
                Format        = cfg.Format == ExportFormat.Avi ? "AVI" : "XProtect",
                Encrypt       = cfg.Encrypt,
                Password      = cfg.Password ?? "",
                IncludePlayer = cfg.IncludePlayer,
                IncludeAudio  = cfg.IncludeAudio,
                RangeStartUtc = cfg.RangeStartUtc.ToString("o"),
                RangeEndUtc   = cfg.RangeEndUtc.ToString("o"),
                OutputFolder  = cfg.OutputFolder,
                Targets       = cfg.Targets.Select(t => new HelperTarget
                {
                    Kind     = t.Kind,
                    ObjectId = t.ObjectId.ToString(),
                    Name     = t.Name ?? ""
                }).ToArray()
            };

            using (var fs = File.Create(path))
            {
                var ser = new DataContractJsonSerializer(typeof(HelperRequest));
                ser.WriteObject(fs, req);
            }
        }

        private static HelperResult ReadResult(string path)
        {
            if (!File.Exists(path)) throw new FileNotFoundException("Result file not written", path);
            using (var fs = File.OpenRead(path))
            {
                var ser = new DataContractJsonSerializer(typeof(HelperResult));
                return (HelperResult)ser.ReadObject(fs);
            }
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }

        // ─── Shared helper for filename sanitisation (kept for tests) ──

        internal static string MakeSafeFileName(string s)
        {
            if (string.IsNullOrEmpty(s)) return "_";
            var invalid = Path.GetInvalidFileNameChars();
            var sb = new System.Text.StringBuilder(s.Length);
            foreach (var c in s) sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
            return sb.ToString();
        }

        private static ExportRunResult Fail(string error) =>
            new ExportRunResult { Success = false, Error = error };
    }
}
