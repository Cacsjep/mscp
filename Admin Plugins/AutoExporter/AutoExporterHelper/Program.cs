using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
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
    /// Args: <serverUri> <configJsonPath>
    ///   serverUri      e.g. https://localhost  (read from EnvironmentManager in the plugin)
    ///   configJsonPath path to a HelperRequest JSON file (next to which we'll write result)
    /// Exit codes:
    ///   0  success
    ///   1  bad args / config parse
    ///   2  SDK init or login failed
    ///   3  no cameras resolved from targets
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
        // [STAThread] mirrors Milestone's ExportSample — UI/Export sub-environments
        // assume a single-threaded apartment for COM interop.
        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length < 2)
            {
                Console.Error.WriteLine("Usage: AutoExporterHelper <serverUri> <configJsonPath>");
                return 1;
            }

            string serverUri  = args[0];
            string configPath = args[1];
            string resultPath = configPath + ".result.json";

            _assemblySearchDirs = BuildSearchDirs(args.Length >= 3 ? args[2] : null);
            Console.Error.WriteLine($"INIT search dirs: {string.Join("; ", _assemblySearchDirs)}");
            AppDomain.CurrentDomain.AssemblyResolve += OnAssemblyResolve;

            try
            {
                return Run(serverUri, configPath, resultPath);
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

            try
            {
                Console.Error.WriteLine($"INIT serverUri={serverUri}");

                // Order matches Milestone's ExportSample/Program.cs exactly:
                //   1. Environment.Initialize        (always required)
                //   2. UI.Environment.Initialize     (registers UI controls; safe in console)
                //   3. Export.Environment.Initialize (registers export references)
                //   4. AddServer + Login
                // Earlier attempts initialized Export AFTER Login, which left the
                // Export pipeline without the hooks it needs to talk to recorders →
                // "Recorder offline -" from DBExporterXpco.StartExport.
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

                Console.Error.WriteLine("INIT done");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"ERR SDK init: {ex.Message}");
                WriteResult(resultPath, false, "SDK init/login failed: " + ex.Message);
                return 2;
            }

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

                Console.Error.WriteLine($"START format={req.Format} range={startUtc:O}->{endUtc:O}");

                bool isAvi = string.Equals(req.Format, "AVI", StringComparison.OrdinalIgnoreCase);
                bool ok = isAvi
                    ? RunAvi(req, cameras, startUtc, endUtc, out string err)
                    : RunDb(req, cameras, startUtc, endUtc, out err);

                long bytes = MeasureSize(req.OutputFolder);

                if (!ok)
                {
                    WriteResult(resultPath, false, err, cameras.Count, bytes, cameras.Select(c => c.Name).ToArray());
                    return 4;
                }

                Console.Error.WriteLine($"DONE bytes={bytes} cameras={cameras.Count}");
                WriteResult(resultPath, true, "", cameras.Count, bytes, cameras.Select(c => c.Name).ToArray());
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
            finally
            {
                // Only the main Environment has a UnInitialize; Media/Export sub-envs don't.
                try { VideoOS.Platform.SDK.Environment.Logout(); } catch { }
                try { VideoOS.Platform.SDK.Environment.RemoveAllServers(); } catch { }
                try { VideoOS.Platform.SDK.Environment.UnInitialize(); } catch { }
            }
        }

        // ─── Camera resolution ──────────────────────────────

        private static List<Item> ResolveCameras(HelperRequest req)
        {
            var result = new List<Item>();
            var seen = new HashSet<Guid>();
            if (req.Targets == null) return result;

            ServerId serverId = null;
            try { serverId = EnvironmentManager.Instance.MasterSite?.ServerId; } catch { }

            foreach (var t in req.Targets)
            {
                if (!Guid.TryParse(t.ObjectId, out var oid)) continue;
                try
                {
                    var item = Configuration.Instance.GetItem(serverId, oid, Kind.Camera);
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
                Thread.Sleep(250);
            }
        }

        private static void EmitProgress(int cameraIdx, int pct, string camName)
        {
            // stderr line consumed by the parent process. Keep this format STABLE — it's parsed.
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

        private static void WriteResult(string path, bool success, string error, int cameraCount = 0, long bytes = 0, string[] cameraNames = null)
        {
            var result = new HelperResult
            {
                Success = success,
                Error = error ?? "",
                CameraCount = cameraCount,
                BytesWritten = bytes,
                CameraNames = cameraNames ?? Array.Empty<string>()
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
