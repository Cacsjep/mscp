using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using VideoOS.Platform;
using VideoOS.Platform.Data;

namespace AutoExporterHelper
{
    /// <summary>
    /// Standalone helper exe that runs Milestone exports (DBExporter / AVIExporter)
    /// inside a full SDK environment. The Event Server's "Service" environment
    /// rejects the export pipeline ("Method not supported for this Environment"),
    /// so the BG plugin spawns this process instead.
    ///
    /// Args: <serverUri> <configJsonPath> [milestoneDir]
    ///   serverUri      e.g. https://localhost  (read from EnvironmentManager in the plugin)
    ///   configJsonPath path to a HelperRequest JSON file (next to which we'll write result)
    ///
    /// Self-test mode: --selftest <serverUri> [milestoneDir]
    ///   Logs in, reads the AutoExporter job configuration directly from the
    ///   Management Server, builds a request from the FIRST job, and runs it
    ///   end-to-end. Lets us spawn the helper by hand and exercise the full
    ///   export path without the Event Server. Output goes to the job's storage
    ///   path under a "selftest_<timestamp>" folder (or %TEMP% if unset).
    ///
    /// Exit codes:
    ///   0  success
    ///   1  bad args / config parse
    ///   2  SDK init or login failed
    ///   3  no cameras resolved from targets (or no jobs configured in self-test)
    ///   4  export failed at runtime
    /// </summary>
    internal class Program
    {
        // Integration identity registered with the Management Server on Login.
        // Same GUID as the plugin's PluginId so the recorder sees both the plugin
        // and the helper as the same "Auto Exporter" integration.
        private static readonly Guid   IntegrationId           = new Guid("BB8298C5-8877-4073-BE24-68F67DE4694D");
        private const           string IntegrationName         = "Auto Exporter";
        private const           string IntegrationVersion      = "1.0";
        private const           string IntegrationManufacturer = "MSC Community Plugins";

        private static string[] _assemblySearchDirs;

        // IMPORTANT: Main MUST NOT reference any VideoOS.Platform type directly.
        // The .NET Framework JIT eagerly resolves type tokens when JITting a
        // method body. If VideoOS.Platform.dll isn't next to the exe (it lives in
        // the Milestone install dir, found via AssemblyResolve), JITting Main
        // would throw FileNotFoundException before the resolve handler is armed.
        //
        // Pattern: Main does only System.* work, arms AssemblyResolve, then calls
        // a sub-method whose JIT is deferred. By the time the sub-method JITs,
        // AssemblyResolve can fire and load VideoOS.Platform from disk.
        //
        // [STAThread] mirrors Milestone's ExportSample. UI/Export sub-environments
        // assume a single-threaded apartment for COM interop.
        [STAThread]
        private static int Main(string[] args)
        {
            bool selfTest = args.Length >= 1 &&
                            args[0].Equals("--selftest", StringComparison.OrdinalIgnoreCase);

            if (selfTest)
            {
                string serverUri = args.Length >= 2 ? args[1] : "https://localhost";
                ArmRuntime(args.Length >= 3 ? args[2] : null);

                try
                {
                    return RunSelfTest(serverUri);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"FATAL selftest: {ex}");
                    return 4;
                }
            }

            if (args.Length < 2)
            {
                Console.Error.WriteLine("Usage: AutoExporterHelper <serverUri> <configJsonPath> [milestoneDir]");
                Console.Error.WriteLine("       AutoExporterHelper --selftest <serverUri> [milestoneDir]");
                return 1;
            }

            string serverUriArg = args[0];
            string configPath = args[1];
            string resultPath = configPath + ".result.json";

            ArmRuntime(args.Length >= 3 ? args[2] : null);

            try
            {
                return Run(serverUriArg, configPath, resultPath);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"FATAL outer: {ex}");
                try { WriteResult(resultPath, false, "Fatal: " + ex.Message); } catch { }
                return 4;
            }
        }

        private static int Run(string serverUri, string configPath, string resultPath)
        {
            HelperRequest req;
            try { req = ReadConfig(configPath); }
            catch (Exception ex)
            {
                WriteResult(resultPath, false, "Config parse failed: " + ex.Message);
                return 1;
            }

            try { InitSdk(serverUri); }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"ERR SDK init: {ex.Message}");
                WriteResult(resultPath, false, "SDK init/login failed: " + ex.Message);
                return 2;
            }

            try { return Execute(req, resultPath); }
            finally { Cleanup(); }
        }

        // ─── SDK lifecycle (shared by normal + self-test) ──

        private static void InitSdk(string serverUri)
        {
            Console.Error.WriteLine($"INIT serverUri={serverUri}");

            // Order:
            //   1. Environment.Initialize        (always required)
            //   2. UI.Environment.Initialize     (registers UI controls; safe in console)
            //   3. Export.Environment.Initialize (registers export references)
            //   4. AddServer + Login             (plain user login, see below)
            //   5. Media.Environment.Initialize  (opens the recorder/media connection)
            // Export must be initialized before Login or the pipeline lacks the
            // hooks it needs to talk to recorders.
            VideoOS.Platform.SDK.Environment.Initialize();
            VideoOS.Platform.SDK.UI.Environment.Initialize();
            VideoOS.Platform.SDK.Export.Environment.Initialize();

            var uri = new Uri(serverUri);
            NetworkCredential creds = CredentialCache.DefaultNetworkCredentials;
            VideoOS.Platform.SDK.Environment.AddServer(false, uri, creds, false);

            // 6-arg Login registers a real integration identity so Recording
            // Servers trust the session for media access.
            VideoOS.Platform.SDK.Environment.Login(
                uri,
                IntegrationId,
                IntegrationName,
                IntegrationVersion,
                IntegrationManufacturer,
                false);

            // The Media environment opens the actual connection to the Recording
            // Servers that the export pipeline pulls frames through.
            VideoOS.Platform.SDK.Media.Environment.Initialize();

            Console.Error.WriteLine("INIT done");
        }

        private static void Cleanup()
        {
            // Only the main Environment has a UnInitialize; Media/Export sub-envs don't.
            try { VideoOS.Platform.SDK.Environment.Logout(); } catch { }
            try { VideoOS.Platform.SDK.Environment.RemoveAllServers(); } catch { }
            try { VideoOS.Platform.SDK.Environment.UnInitialize(); } catch { }
        }

        // ─── Export execution (shared by normal + self-test) ──

        private static int Execute(HelperRequest req, string resultPath)
        {
            try
            {
                var cameras = ResolveCameras(req);
                Console.Error.WriteLine($"RESOLVE targets={req.Targets?.Length ?? 0} cameras={cameras.Count}");

                if (cameras.Count == 0)
                {
                    WriteResult(resultPath, false, "No cameras resolved from job targets");
                    return 3;
                }

                Directory.CreateDirectory(req.OutputFolder);

                var startUtc = DateTime.Parse(req.RangeStartUtc, CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind);
                var endUtc = DateTime.Parse(req.RangeEndUtc, CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind);

                // The recording-server online status arrives via async callbacks
                // that the SDK dispatches on a WinForms message loop. A console /
                // service process has no such loop, so without pumping messages the
                // recorder stays "offline" forever and StartExport throws
                // "Recorder offline -". WinForms ExportSample / DialogLoginForm work
                // precisely because their dialog pumps the loop. So we pump here, after
                // login, to let the recorder come online before exporting.
                Console.Error.WriteLine("SETTLE pumping message loop for recorder status...");
                PumpMessages(6000);

                // DBExporter throws "Recorder offline -" (by design, confirmed by
                // Milestone) for ANY camera in the list that has no entry in the
                // recordings database - e.g. a device that has never recorded. One
                // such camera fails the WHOLE export. So drop cameras with no recorded
                // data before exporting. Must run AFTER the pump so the recorder
                // connection is up and the probe results are accurate.
                var exportable = FilterCamerasWithData(cameras, endUtc, out var skipped);
                if (exportable.Count == 0)
                {
                    Console.Error.WriteLine("INFO no camera has recorded data in/before the range (treating as success)");
                    WriteResult(resultPath, true, "", 0, 0, Array.Empty<string>(), skipped.ToArray());
                    return 0;
                }
                cameras = exportable;

                Console.Error.WriteLine($"START format={req.Format} cameras={cameras.Count} range={startUtc:O}->{endUtc:O}");

                bool isAvi = string.Equals(req.Format, "AVI", StringComparison.OrdinalIgnoreCase);
                bool ok = isAvi
                    ? RunAvi(req, cameras, startUtc, endUtc, out string err)
                    : RunDb(req, cameras, startUtc, endUtc, out err);

                long bytes = MeasureSize(req.OutputFolder);

                if (!ok)
                {
                    WriteResult(resultPath, false, err, cameras.Count, bytes, cameras.Select(c => c.Name).ToArray(), skipped.ToArray());
                    return 4;
                }

                Console.Error.WriteLine($"DONE bytes={bytes} cameras={cameras.Count} skipped={skipped.Count}");
                WriteResult(resultPath, true, "", cameras.Count, bytes, cameras.Select(c => c.Name).ToArray(), skipped.ToArray());
                return 0;
            }
            catch (NoVideoInTimeSpanMIPException)
            {
                Console.Error.WriteLine("INFO no video in time span (treating as success)");
                WriteResult(resultPath, true, "", 0, 0, Array.Empty<string>());
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"ERR fatal: {ex}");
                WriteResult(resultPath, false, "Fatal: " + ex.Message);
                return 4;
            }
        }

        // ─── Self-test: run the first configured job end-to-end ──

        private static int RunSelfTest(string serverUri)
        {
            Console.Error.WriteLine("=== SELF-TEST MODE ===");

            try { InitSdk(serverUri); }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"ERR SDK init: {ex.Message}");
                return 2;
            }

            try
            {
                HelperRequest req = BuildRequestFromFirstJob();
                if (req == null)
                {
                    Console.Error.WriteLine("SELFTEST no jobs configured on the Management Server");
                    return 3;
                }

                Console.Error.WriteLine(
                    $"SELFTEST job='{req.JobName}' format={req.Format} encrypt={req.Encrypt} " +
                    $"targets={req.Targets?.Length ?? 0} range={req.RangeStartUtc}->{req.RangeEndUtc} " +
                    $"out='{req.OutputFolder}'");

                string resultPath = Path.Combine(Path.GetTempPath(), "autoexporter_selftest.result.json");
                int code = Execute(req, resultPath);
                Console.Error.WriteLine($"SELFTEST exit code={code}, result written to '{resultPath}'");
                return code;
            }
            finally { Cleanup(); }
        }

        // Reads the AutoExporter job items straight off the Management Server and
        // builds a HelperRequest from the first one. Mirrors the property keys the
        // BG plugin (JobItemManager / JobUserControl / TimeRange) writes and reads.
        private static HelperRequest BuildRequestFromFirstJob()
        {
            var pluginId  = new Guid("BB8298C5-8877-4073-BE24-68F67DE4694D");
            var jobKindId = new Guid("1425AA67-4D80-42BA-8B1B-15FA60B3331C");

            List<Item> jobs;
            try { jobs = Configuration.Instance.GetItemConfigurations(pluginId, null, jobKindId); }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"ERR read job configs: {ex.Message}");
                return null;
            }

            Console.Error.WriteLine($"SELFTEST found {jobs?.Count ?? 0} job(s)");
            var job = jobs?.FirstOrDefault();
            if (job == null) return null;

            string Get(string key, string def) =>
                job.Properties != null && job.Properties.ContainsKey(key) ? job.Properties[key] : def;

            if (!int.TryParse(Get("RangeValue", "1"), out int rangeValue)) rangeValue = 1;
            string rangeUnit = Get("RangeUnit", "Days");
            DateTime endUtc = DateTime.UtcNow;
            DateTime startUtc = SubtractRange(endUtc, rangeValue, rangeUnit);

            string storage = Get("StoragePath", "");
            string stamp = "selftest_" + endUtc.ToLocalTime().ToString("dd.MM.yyyy_HHmm");
            string outFolder = string.IsNullOrWhiteSpace(storage)
                ? Path.Combine(Path.GetTempPath(), "AutoExporterSelfTest", stamp)
                : Path.Combine(storage, stamp);

            var targets = new List<HelperTarget>();
            int.TryParse(Get("Targets_Count", "0"), out int count);
            for (int i = 0; i < count; i++)
            {
                string kind = Get($"Targets_{i}_Kind", null);
                string oid  = Get($"Targets_{i}_ObjectId", null);
                string name = Get($"Targets_{i}_Name", "");
                if (string.IsNullOrEmpty(kind) || string.IsNullOrEmpty(oid)) continue;
                targets.Add(new HelperTarget { Kind = kind, ObjectId = oid, Name = name });
            }

            return new HelperRequest
            {
                RunId         = Guid.NewGuid().ToString(),
                JobName       = job.Name,
                Format        = Get("Format", "XProtect"),
                Encrypt       = Get("Encrypt", "No") == "Yes",
                Password      = Get("Password", ""),
                IncludePlayer = Get("IncludePlayer", "Yes") != "No",
                IncludeAudio  = Get("IncludeAudio", "Yes") != "No",
                RangeStartUtc = startUtc.ToString("o"),
                RangeEndUtc   = endUtc.ToString("o"),
                OutputFolder  = outFolder,
                Targets       = targets.ToArray()
            };
        }

        // Mirrors AutoExporter.Background.TimeRange.Subtract.
        private static DateTime SubtractRange(DateTime end, int value, string unit)
        {
            if (value <= 0) value = 1;
            switch ((unit ?? "Days").ToLowerInvariant())
            {
                case "minutes": return end.AddMinutes(-value);
                case "hours":   return end.AddHours(-value);
                case "months":  return end.AddDays(-value * 30);
                default:        return end.AddDays(-value);
            }
        }

        // ─── Camera resolution ──────────────────────────────

        private static List<Item> ResolveCameras(HelperRequest req)
        {
            var result = new List<Item>();
            var seen = new HashSet<Guid>();
            if (req.Targets == null) return result;

            foreach (var t in req.Targets)
            {
                if (!Guid.TryParse(t.ObjectId, out var oid)) continue;
                try
                {
                    // GetItem(oid, Kind.Camera) WITHOUT a ServerId returns the camera
                    // bound to its Recording Server (FQID.ServerId = the recorder).
                    // Passing the master-site ServerId instead stamps the item with the
                    // Management Server, and the export pipeline then can't find the
                    // recorder -> "Recorder offline -" with an empty recorder name.
                    // This matches the RtmpStreamer / BarcodeReader helpers.
                    var item = Configuration.Instance.GetItem(oid, Kind.Camera);
                    if (item == null) continue;

                    bool isGroup = item.FQID != null && item.FQID.FolderType != FolderType.No;
                    if (isGroup)
                    {
                        foreach (var leaf in FlattenCameras(item))
                            if (seen.Add(leaf.FQID.ObjectId)) result.Add(leaf);
                    }
                    else if (seen.Add(item.FQID.ObjectId))
                    {
                        result.Add(item);
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"WARN resolve failed for {t.Kind}/{t.ObjectId}: {ex.Message}");
                }
            }

            return result;
        }

        // ─── Recorded-data filter ───────────────────────────

        // Keeps only cameras that actually have something in the recordings
        // database; the rest would each abort the whole DBExporter run with
        // "Recorder offline -". Probe = ask the recorder for a recorded frame at or
        // before the range end. No frame (or an error) => no recordings => skip.
        private static List<Item> FilterCamerasWithData(List<Item> cameras, DateTime endUtc, out List<string> skippedNames)
        {
            var keep = new List<Item>();
            skippedNames = new List<string>();
            foreach (var cam in cameras)
            {
                if (HasRecordedData(cam, endUtc))
                {
                    keep.Add(cam);
                }
                else
                {
                    skippedNames.Add(cam?.Name ?? "");
                    Console.Error.WriteLine($"SKIP no recordings in database: '{cam?.Name}'");
                }
            }
            Console.Error.WriteLine($"FILTER {cameras.Count} camera(s) -> {keep.Count} with recorded data");
            return keep;
        }

        private static bool HasRecordedData(Item cam, DateTime endUtc)
        {
            if (cam == null) return false;
            try
            {
                // Query recording-sequence METADATA (no frame decode) for the most
                // recent recording at or before the range end. We deliberately look
                // back far rather than only inside the export window: a camera that
                // records continuously can have a single long sequence a narrow query
                // would miss, and the crash we're guarding against fires only when the
                // camera has NO recordings-database entry at all. maxCount=1 keeps it
                // cheap - we just need existence.
                var source = new SequenceDataSource(cam);
                var sequences = source.GetData(
                    endUtc,
                    TimeSpan.FromDays(3650),                 // look back up to ~10 years
                    1,                                       // at most one (existence check)
                    TimeSpan.Zero, 0,                        // nothing after the range end
                    DataType.SequenceTypeGuids.RecordingSequence);
                return sequences != null && sequences.Count > 0;
            }
            catch (Exception ex)
            {
                // No database entry for this camera surfaces here (e.g. "Unable to
                // connect to toolkit!"). Treat as "no data" so it's skipped rather
                // than aborting the whole export.
                Console.Error.WriteLine($"PROBE '{cam.Name}' returned no data: {ex.Message.Trim()}");
                return false;
            }
        }

        private static IEnumerable<Item> FlattenCameras(Item folder)
        {
            IEnumerable<Item> children = Enumerable.Empty<Item>();
            try { children = folder.GetChildren() ?? Enumerable.Empty<Item>(); }
            catch { }
            foreach (var c in children)
            {
                if (c?.FQID == null || c.FQID.Kind != Kind.Camera) continue;
                if (c.FQID.FolderType != FolderType.No)
                {
                    foreach (var grand in FlattenCameras(c)) yield return grand;
                }
                else
                {
                    yield return c;
                }
            }
        }

        // ─── XProtect (DBExporter) ──────────────────────────

        private static bool RunDb(HelperRequest req, List<Item> cameras, DateTime startUtc, DateTime endUtc, out string error)
        {
            error = null;
            DBExporter exporter = null;
            try
            {
                exporter = new DBExporter(req.IncludePlayer)
                {
                    Encryption = req.Encrypt,
                    EncryptionStrength = EncryptionStrength.AES128,
                    Password = req.Password ?? "",
                    SignExport = false,
                    PreventReExport = false,
                    IncludeBookmarks = false,
                    FailOnInvalidSignature = false
                };

                exporter.Init();
                exporter.Path = req.OutputFolder;
                exporter.CameraList.AddRange(cameras);

                if (req.IncludeAudio)
                {
                    var audio = cameras
                        .SelectMany(c => SafeRelated(c))
                        .Where(x => x.FQID.Kind == Kind.Microphone || x.FQID.Kind == Kind.Speaker)
                        .ToList();
                    exporter.AudioList.AddRange(audio);
                }

                if (!exporter.StartExport(startUtc, endUtc))
                {
                    error = $"{exporter.LastErrorString} ({exporter.LastError})";
                    return false;
                }

                WaitForCompletion(exporter, cameras.FirstOrDefault()?.Name ?? "", 0);

                if (exporter.LastError > 0)
                {
                    error = $"{exporter.LastErrorString} ({exporter.LastError})";
                    return false;
                }
                return true;
            }
            finally { SafeEnd(exporter); }
        }

        // ─── AVI (per camera) ───────────────────────────────

        private static bool RunAvi(HelperRequest req, List<Item> cameras, DateTime startUtc, DateTime endUtc, out string error)
        {
            error = null;
            for (int i = 0; i < cameras.Count; i++)
            {
                var cam = cameras[i];
                var name = MakeSafeFileName(cam?.Name ?? $"camera_{i}");
                var camDir = Path.Combine(req.OutputFolder, name);
                Directory.CreateDirectory(camDir);
                var filename = Path.Combine(camDir, name + ".avi");

                Console.Error.WriteLine($"AVI cam {i + 1}/{cameras.Count}: '{cam?.Name}' -> '{filename}'");

                AVIExporter exporter = null;
                try
                {
                    exporter = new AVIExporter { Filename = filename };
                    exporter.Init();
                    exporter.Path = camDir;
                    exporter.CameraList.Add(cam);

                    if (req.IncludeAudio)
                    {
                        var audio = SafeRelated(cam)
                            .Where(x => x.FQID.Kind == Kind.Microphone || x.FQID.Kind == Kind.Speaker)
                            .ToList();
                        exporter.AudioList.AddRange(audio);
                    }

                    if (!exporter.StartExport(startUtc, endUtc))
                    {
                        error = $"Camera '{cam?.Name}': {exporter.LastErrorString} ({exporter.LastError})";
                        return false;
                    }

                    WaitForCompletion(exporter, cam?.Name ?? "", i);

                    if (exporter.LastError > 0)
                    {
                        error = $"Camera '{cam?.Name}': {exporter.LastErrorString} ({exporter.LastError})";
                        return false;
                    }
                }
                finally { SafeEnd(exporter); }
            }
            return true;
        }

        // ─── Common ────────────────────────────────────────

        private static void WaitForCompletion(IExporter exporter, string cameraName, int cameraIndex)
        {
            while (true)
            {
                int p = exporter.Progress;
                if (p < 0) p = 0;
                EmitProgress(cameraIndex, p, cameraName);
                if (p >= 100 || exporter.LastError > 0) return;
                // Pump (not plain Sleep) so the export's own media callbacks keep
                // being dispatched on this thread while we wait.
                PumpMessages(250);
            }
        }

        // Drains the WinForms message queue on this (STA) thread for roughly the
        // given duration. The MIP SDK posts recorder connection/status and media
        // callbacks here; a console process must pump them itself or the recorder
        // is never marked online. Uses Environment.TickCount (not Stopwatch) to keep
        // the dependency surface minimal.
        private static void PumpMessages(int ms)
        {
            int start = System.Environment.TickCount;
            do
            {
                System.Windows.Forms.Application.DoEvents();
                Thread.Sleep(25);
            }
            while (unchecked(System.Environment.TickCount - start) < ms);
        }

        private static void EmitProgress(int cameraIdx, int pct, string camName)
        {
            // stderr line consumed by the parent process. Keep this format STABLE (it's parsed).
            // PROGRESS cameraIdx=<int> pct=<int> name=<string-may-have-spaces>
            Console.Error.WriteLine($"PROGRESS cameraIdx={cameraIdx} pct={pct} name={camName}");
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

        private static string MakeSafeFileName(string s)
        {
            if (string.IsNullOrEmpty(s)) return "_";
            var invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(s.Length);
            foreach (var c in s) sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
            return sb.ToString();
        }

        // ─── IO ────────────────────────────────────────────

        private static HelperRequest ReadConfig(string path)
        {
            using (var fs = File.OpenRead(path))
            {
                var ser = new DataContractJsonSerializer(typeof(HelperRequest));
                return (HelperRequest)ser.ReadObject(fs);
            }
        }

        private static void WriteResult(string path, bool success, string error, int cameraCount = 0, long bytes = 0, string[] cameraNames = null, string[] skippedCameras = null)
        {
            var result = new HelperResult
            {
                Success = success,
                Error = error ?? "",
                CameraCount = cameraCount,
                BytesWritten = bytes,
                CameraNames = cameraNames ?? Array.Empty<string>(),
                SkippedCameras = skippedCameras ?? Array.Empty<string>()
            };
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                using (var fs = File.Create(path))
                {
                    var ser = new DataContractJsonSerializer(typeof(HelperResult));
                    ser.WriteObject(fs, result);
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"ERR result write: {ex.Message}");
            }
        }

        // ─── Runtime arming: assembly + native DLL resolution ──

        // Builds the Milestone search dirs, points BOTH the managed assembly
        // resolver and the native (OS) DLL loader at them, then arms the resolver.
        // Native setup matters: the export pipeline loads CoreToolkits.dll and its
        // siblings through the OS loader, which AssemblyResolve never sees. Without
        // them on the native search path, DBExporter.StartExport reports the toolkit
        // as unreachable and surfaces it as "Recorder offline -".
        private static void ArmRuntime(string milestoneDir)
        {
            _assemblySearchDirs = BuildSearchDirs(milestoneDir);
            Console.Error.WriteLine($"INIT search dirs: {string.Join("; ", _assemblySearchDirs)}");

            ConfigureNativeDllSearch(_assemblySearchDirs);

            AppDomain.CurrentDomain.AssemblyResolve += OnAssemblyResolve;
        }

        // Native DLL search list (Win8+/Server2012+). Kernel32 falls back to legacy
        // search rules if SetDefaultDllDirectories isn't available, in which case the
        // AddDllDirectory calls are no-ops.
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetDefaultDllDirectories(uint DirectoryFlags);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr AddDllDirectory(string NewDirectory);

        private const uint LOAD_LIBRARY_SEARCH_DEFAULT_DIRS    = 0x00001000;
        private const uint LOAD_LIBRARY_SEARCH_USER_DIRS       = 0x00000400;
        private const uint LOAD_LIBRARY_SEARCH_SYSTEM32        = 0x00000800;
        private const uint LOAD_LIBRARY_SEARCH_APPLICATION_DIR = 0x00000200;

        private static void ConfigureNativeDllSearch(string[] dirs)
        {
            try
            {
                SetDefaultDllDirectories(
                    LOAD_LIBRARY_SEARCH_DEFAULT_DIRS |
                    LOAD_LIBRARY_SEARCH_USER_DIRS |
                    LOAD_LIBRARY_SEARCH_SYSTEM32 |
                    LOAD_LIBRARY_SEARCH_APPLICATION_DIR);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"WARN SetDefaultDllDirectories not available: {ex.Message}");
            }

            if (dirs == null) return;
            foreach (var d in dirs)
            {
                if (string.IsNullOrEmpty(d)) continue;
                try
                {
                    var h = AddDllDirectory(d);
                    if (h == IntPtr.Zero)
                        Console.Error.WriteLine($"WARN AddDllDirectory failed (gle={Marshal.GetLastWin32Error()}): {d}");
                }
                catch (Exception ex) { Console.Error.WriteLine($"WARN AddDllDirectory threw for {d}: {ex.Message}"); }
            }
        }

        // ─── Assembly resolution (same pattern as RtmpStreamerHelper) ──

        private static string[] BuildSearchDirs(string milestoneDir)
        {
            var dirs = new List<string>();
            if (!string.IsNullOrEmpty(milestoneDir) && Directory.Exists(milestoneDir))
                dirs.Add(milestoneDir);

            // Order matters: the standard server install dirs ship VideoOS.Platform.dll
            // and a subset of the SDK DLLs (Recording Server has SDK + Export + Media).
            // SDK.UI.dll, however, is only present under MIPDrivers\GisDriver. Without
            // that dir, VideoOS.Platform.SDK.UI.Environment.Initialize() throws
            // FileNotFoundException for "VideoOS.Platform.SDK.UI".
            var candidates = new[]
            {
                @"C:\Program Files\Milestone\XProtect Event Server",
                @"C:\Program Files\Milestone\XProtect Recording Server",
                @"C:\Program Files\Milestone\XProtect Management Server",
                @"C:\Program Files\Milestone\MIPDrivers\GisDriver"
            };

            foreach (var dir in candidates)
                if (Directory.Exists(dir) && !dirs.Contains(dir)) dirs.Add(dir);

            return dirs.ToArray();
        }

        private static Assembly OnAssemblyResolve(object sender, ResolveEventArgs args)
        {
            var name = new AssemblyName(args.Name).Name + ".dll";
            foreach (var dir in _assemblySearchDirs)
            {
                var path = Path.Combine(dir, name);
                if (File.Exists(path))
                {
                    try { return Assembly.LoadFrom(path); } catch { }
                }
            }
            return null;
        }
    }
}
