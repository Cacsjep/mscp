using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Reflection;
using System.Threading;
using CommunitySDK;
using VideoOS.Platform;

namespace BarcodeReaderHelper
{
    internal class Program
    {
        private static readonly PluginLog _log = new PluginLog("BarcodeReader");
        private static string[] _assemblySearchDirs;

        // Arg indices (must match BackgroundPlugin.LaunchHelper):
        //   0 serverUri
        //   1 cameraId
        //   2 itemId (channel id, echoed back in STATUS/STATS/DETECT context)
        //   3 formatsCsv
        //   4 tryHarder (0/1)
        //   5 autoRotate (0/1)
        //   6 tryInverted (0/1)
        //   7 targetFps (int)
        //   8 downscaleWidth (int, 0 = native)
        //   9 debounceMs (int)
        //  10 createBookmarks (0/1)
        //  11 channelName (used in bookmark description; passed quoted)
        //  12 milestoneDir (for assembly resolution)
        private const int ExpectedArgCount = 13;

        private static int Main(string[] args)
        {
            // IMPORTANT: Main must not reference any Milestone type or the DecodeLoop/Config
            // types that transitively reference Milestone. The JIT resolves type tokens used
            // in Main's IL when it compiles the method  that happens before the first line
            // of Main runs. If any of those tokens pull in VideoOS.Platform, we crash with
            // FileNotFoundException before we get a chance to attach the AssemblyResolve
            // handler. Keep the body primitive-only; dispatch to Run() once the resolver
            // is wired up so the JIT of Run() picks up the resolved assemblies.
            if (args.Length < ExpectedArgCount)
            {
                Console.Error.WriteLine("Usage: BarcodeReaderHelper.exe <serverUri> <cameraId> <itemId> <formatsCsv> <tryHarder> <autoRotate> <tryInverted> <targetFps> <downscaleWidth> <debounceMs> <createBookmarks> <channelName> <milestoneDir>");
                return 1;
            }

            _assemblySearchDirs = BuildSearchDirs(args[12]);
            AppDomain.CurrentDomain.AssemblyResolve += OnAssemblyResolve;

            return Run(args);
        }

        private static int Run(string[] args)
        {
            if (!Guid.TryParse(args[1], out var cameraId) || cameraId == Guid.Empty)
            {
                _log.Error($"Invalid camera id: {args[1]}");
                WriteStatus("Error:InvalidCameraId");
                return 2;
            }

            var cfg = new DecodeLoop.Config
            {
                ServerUri       = args[0],
                CameraId        = cameraId,
                ItemId          = args[2],
                FormatsCsv      = args[3],
                TryHarder       = args[4] == "1",
                AutoRotate      = args[5] == "1",
                TryInverted     = args[6] == "1",
                TargetFps       = ParseInt(args[7], 1, 1, 60),
                DownscaleWidth  = ParseInt(args[8], 0,  0, 7680),
                DebounceMs      = ParseInt(args[9], 2000, 0, 60000),
                CreateBookmarks = args[10] == "1",
                ChannelName     = args[11] ?? "",
            };

            _log.Info($"Starting helper: camera={cfg.CameraId} fps={cfg.TargetFps} formats={cfg.FormatsCsv} downscale={cfg.DownscaleWidth} debounce={cfg.DebounceMs}ms");

            var exitEvent = new ManualResetEventSlim(false);
            Console.CancelKeyPress += (s, e) => { e.Cancel = true; exitEvent.Set(); };

            DecodeLoop loop = null;
            try
            {
                WriteStatus("Connecting");
                _log.Info("Initializing MIP SDK...");
                VideoOS.Platform.SDK.Environment.Initialize();

                var uri = new Uri(cfg.ServerUri);
                VideoOS.Platform.SDK.Environment.AddServer(uri, CredentialCache.DefaultNetworkCredentials);
                VideoOS.Platform.SDK.Environment.Login(uri);

                VideoOS.Platform.SDK.Media.Environment.Initialize();

                var cameraItem = Configuration.Instance.GetItem(cameraId, Kind.Camera);
                if (cameraItem == null)
                {
                    _log.Error($"Camera not found: {cameraId}");
                    WriteStatus("Error:CameraNotFound");
                    return 3;
                }

                _log.Info($"Camera resolved: {cameraItem.Name}");
                loop = new DecodeLoop(cameraItem, cfg, _log);
                loop.Start();
                // DecodeLoop.OpenSource already publishes "Running" / "Error:Connect" and
                // will keep the admin UI in sync through reconnects. No manual WriteStatus
                // here  the watchdog owns the Running state from now on.
                exitEvent.Wait();
            }
            catch (Exception ex)
            {
                _log.Error($"Fatal error: {ex.Message}", ex);
                WriteStatus("Error:" + ex.GetType().Name);
                return 4;
            }
            finally
            {
                try { loop?.Stop(); } catch { }
                try { VideoOS.Platform.SDK.Environment.Logout(); } catch { }
                try { VideoOS.Platform.SDK.Environment.RemoveAllServers(); } catch { }
                try { VideoOS.Platform.SDK.Environment.UnInitialize(); } catch { }

                WriteStatus("Stopped");
                _log.Info("Shutdown complete.");
            }
            return 0;
        }

        internal static void WriteStatus(string status) => Console.Error.WriteLine("STATUS " + status);

        private static int ParseInt(string s, int def, int min, int max)
        {
            if (!int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)) return def;
            if (v < min) v = min;
            if (v > max) v = max;
            return v;
        }

        private static string[] BuildSearchDirs(string milestoneDir)
        {
            var dirs = new System.Collections.Generic.List<string>();
            if (!string.IsNullOrEmpty(milestoneDir) && Directory.Exists(milestoneDir)) dirs.Add(milestoneDir);

            foreach (var dir in new[]
            {
                @"C:\Program Files\Milestone\XProtect Event Server",
                @"C:\Program Files\Milestone\XProtect Recording Server",
                @"C:\Program Files\Milestone\XProtect Management Server"
            })
            {
                if (Directory.Exists(dir) && !dirs.Contains(dir)) dirs.Add(dir);
            }
            return dirs.ToArray();
        }

        private static Assembly OnAssemblyResolve(object sender, ResolveEventArgs args)
        {
            var dllName = new AssemblyName(args.Name).Name + ".dll";
            foreach (var dir in _assemblySearchDirs)
            {
                var path = Path.Combine(dir, dllName);
                if (File.Exists(path))
                {
                    try { return Assembly.LoadFrom(path); } catch { }
                }
            }
            return null;
        }
    }
}
