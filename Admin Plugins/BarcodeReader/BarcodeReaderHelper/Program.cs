using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using CommunitySDK;
using Microsoft.Win32;
using VideoOS.Platform;

namespace BarcodeReaderHelper
{
    internal class Program
    {
        private static readonly PluginLog _log = new PluginLog("BarcodeReader");
        private static string[] _assemblySearchDirs;

        // Exit codes are a contract with BackgroundPlugin: anything > 0 means the
        // helper failed to enter or stay in the running state. Keep in sync with
        // BackgroundPlugin.MapExitCode so MIPLog explains what each value means.
        public const int ExitOk                  = 0;
        public const int ExitBadArgs             = 1;
        public const int ExitInvalidCameraId     = 2;
        public const int ExitCameraNotFound      = 3;
        public const int ExitManagedException    = 4;
        public const int ExitEnvironmentMissing  = 5;
        public const int ExitNativeDepsMissing   = 6;
        // 255 / -1 / 0xc06d007e etc. are produced by the OS when our process is
        // terminated by an unhandled native exception (e.g. delay-load DLL miss).
        // We never return these explicitly  they signal "we never got to clean up".

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
                return ExitBadArgs;
            }

            // Wire diagnostics in this exact order:
            //   1. Attach the on-disk log sink first so a crash in the resolver itself
            //      still leaves a trail when stderr is racing the dying process.
            //   2. Install assembly resolver (must precede any Milestone type reference).
            //   3. Install last-chance handlers for managed exceptions that escape Run()'s
            //      try/catch  e.g. exceptions thrown during JIT of a method whose try-frame
            //      is not yet established, or on background threads we don't own.
            //
            // Native unhandled exceptions (SEH 0xc06d007e delay-load misses, AVs in unmanaged
            // code) still bypass all of this  they take down the process directly. Those
            // show up to BackgroundPlugin as exit code 255 and are decoded by MapExitCode.
            _log.AttachFile(BuildLogFilePath(args[2]));

            _assemblySearchDirs = BuildSearchDirs(args[12]);
            _log.Info($"Assembly search dirs: {string.Join(" ; ", _assemblySearchDirs)}");
            AppDomain.CurrentDomain.AssemblyResolve += OnAssemblyResolve;

            // Native (delay-load) DLL search: the Windows native loader is a separate
            // code path from the managed AssemblyResolve handler above. MIP SDK Media
            // delay-binds CoreToolkits.dll / IMV1.dll / FFmpeg natives once the first
            // JPEG frame arrives. Wire those dirs into the loader's default search list
            // here so the bind succeeds without depending on cwd or PATH.
            ConfigureNativeDllSearch(_assemblySearchDirs);

            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

            // Probe the canonical Milestone native (CoreToolkits.dll) eagerly so a
            // missing-deps host fails fast with a diagnosable exit code instead of
            // crashing later inside MIP SDK Media with SEH 0xc06d007e (exit 255).
            if (!ProbeCoreToolkits())
            {
                _log.Error("CoreToolkits.dll could not be located on this host. " +
                           "Event Server Dir not found - add the Event Server install dir " +
                           "(e.g. C:\\Program Files\\Milestone\\XProtect Event Server) to the " +
                           "Machine PATH environment variable, or install the plugin on a host " +
                           "that has Event Server / Recording Server. " +
                           $"Searched: {string.Join(" ; ", _assemblySearchDirs)}");
                WriteStatus("Error:NativeDepsMissing");
                return ExitNativeDepsMissing;
            }

            return Run(args);
        }

        private static int Run(string[] args)
        {
            if (!Guid.TryParse(args[1], out var cameraId) || cameraId == Guid.Empty)
            {
                _log.Error($"Invalid camera id: {args[1]}");
                WriteStatus("Error:InvalidCameraId");
                return ExitInvalidCameraId;
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
            _log.Info($"Process: PID={System.Diagnostics.Process.GetCurrentProcess().Id} arch={(IntPtr.Size == 8 ? "x64" : "x86")} clr={System.Environment.Version} os={System.Environment.OSVersion}");

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
                    return ExitCameraNotFound;
                }

                _log.Info($"Camera resolved: {cameraItem.Name}");
                _log.Info("Run: pre-DecodeLoop ctor");
                loop = new DecodeLoop(cameraItem, cfg, _log);
                _log.Info("Run: pre-DecodeLoop.Start");
                loop.Start();
                _log.Info("Run: post-DecodeLoop.Start, entering wait");
                // DecodeLoop.OpenSource already publishes "Running" / "Error:Connect" and
                // will keep the admin UI in sync through reconnects. No manual WriteStatus
                // here  the watchdog owns the Running state from now on.
                exitEvent.Wait();
            }
            catch (Exception ex)
            {
                _log.Error("Fatal error", ex);
                WriteStatus("Error:" + ex.GetType().Name);
                return ExitManagedException;
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
            return ExitOk;
        }

        internal static void WriteStatus(string status) => Console.Error.WriteLine("STATUS " + status);

        private static int ParseInt(string s, int def, int min, int max)
        {
            if (!int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)) return def;
            if (v < min) v = min;
            if (v > max) v = max;
            return v;
        }

        private static string BuildLogFilePath(string itemId)
        {
            try
            {
                var safeId = string.IsNullOrEmpty(itemId) ? "unknown" : itemId;
                var dir = Path.Combine(
                    System.Environment.GetFolderPath(System.Environment.SpecialFolder.CommonApplicationData),
                    "Milestone", "BarcodeReader");
                return Path.Combine(dir, $"helper-{safeId}.log");
            }
            catch
            {
                return null;
            }
        }

        // OEM-safe install-dir discovery. Probes in priority order so the first hit
        // wins, but every probe runs so the resolved list is the *union* of valid
        // directories (FFmpeg may sit in Recording Server while CoreToolkits is in
        // Event Server, etc.). Hardcoded `C:\Program Files\Milestone\...` paths are a
        // last-chance fallback only - on Husky / Arcules / non-default installs they
        // simply don't exist and the registry/walk-up probes carry the load.
        private static string[] BuildSearchDirs(string milestoneDir)
        {
            var dirs = new System.Collections.Generic.List<string>();

            void Add(string dir, string source)
            {
                if (string.IsNullOrEmpty(dir)) return;
                try { dir = Path.GetFullPath(dir.TrimEnd('\\', '/')); } catch { return; }
                if (!Directory.Exists(dir)) return;
                foreach (var existing in dirs)
                    if (string.Equals(existing, dir, StringComparison.OrdinalIgnoreCase)) return;
                dirs.Add(dir);
                _log.Info($"Search dir [{source}]: {dir}");
            }

            // 1. Authoritative: dir handed in by the BackgroundPlugin host.
            Add(milestoneDir, "host");

            // 2. Walk up from the helper exe location. ...\MIPPlugins\BarcodeReader\
            //    sits under the parent service install dir (Event/Recording Server).
            try
            {
                var exeDir = Path.GetDirectoryName(typeof(Program).Assembly.Location);
                for (int i = 0; i < 4 && !string.IsNullOrEmpty(exeDir); i++)
                {
                    Add(exeDir, $"walk-up[{i}]");
                    exeDir = Path.GetDirectoryName(exeDir);
                }
            }
            catch { }

            // 3. Registry probes. Milestone writes these on standard installs but OEM
            //    rebrands sometimes ship without them - missing keys are normal here,
            //    not an error. Treat each independently so one missing key doesn't
            //    skip the others.
            foreach (var (subkey, name) in new[]
            {
                (@"SOFTWARE\VideoOS\Server",   "InstallationPath"),
                (@"SOFTWARE\VideoOS\Recorder", "InstallationPath"),
                (@"SOFTWARE\VideoOS\Platform", "InstallationPath"),
            })
            {
                Add(TryReadRegistry(RegistryHive.LocalMachine, subkey, name), $"reg:HKLM\\{subkey}\\{name}");
            }

            // 4. Last-chance hardcoded defaults. On a stock English-locale Milestone
            //    install these line up; on OEM/non-default installs they simply don't
            //    exist and Add() drops them.
            foreach (var dir in new[]
            {
                @"C:\Program Files\Milestone\XProtect Event Server",
                @"C:\Program Files\Milestone\XProtect Recording Server",
                @"C:\Program Files\Milestone\XProtect Management Server",
            })
            {
                Add(dir, "default");
            }

            if (dirs.Count == 0)
            {
                _log.Error("Event Server Dir not found - no Milestone install dir located via host arg, " +
                           "exe walk-up, registry (HKLM\\SOFTWARE\\VideoOS\\Server\\InstallationPath etc.), " +
                           "or default paths. On OEM / non-default installations, add the Event Server " +
                           "install dir to the Machine PATH environment variable so native delay-load " +
                           "(CoreToolkits.dll, FFmpeg, etc.) can resolve.");
            }

            return dirs.ToArray();
        }

        private static string TryReadRegistry(RegistryHive hive, string subkey, string valueName)
        {
            try
            {
                using (var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64))
                using (var key = baseKey.OpenSubKey(subkey))
                {
                    return key?.GetValue(valueName) as string;
                }
            }
            catch { return null; }
        }

        // Native DLL search list (Win8+/Server2012+). Kernel32 falls back to legacy
        // search rules if SetDefaultDllDirectories isn't available, in which case the
        // AddDllDirectory calls are no-ops - same end result the helper already had.
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetDefaultDllDirectories(uint DirectoryFlags);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr AddDllDirectory(string NewDirectory);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr LoadLibraryW(string lpLibFileName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FreeLibrary(IntPtr hLibModule);

        private const uint LOAD_LIBRARY_SEARCH_DEFAULT_DIRS = 0x00001000;
        private const uint LOAD_LIBRARY_SEARCH_USER_DIRS    = 0x00000400;
        private const uint LOAD_LIBRARY_SEARCH_SYSTEM32     = 0x00000800;
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
                _log.Info($"SetDefaultDllDirectories not available: {ex.Message}");
            }

            if (dirs == null) return;
            foreach (var d in dirs)
            {
                if (string.IsNullOrEmpty(d)) continue;
                try
                {
                    var h = AddDllDirectory(d);
                    if (h == IntPtr.Zero) _log.Info($"AddDllDirectory failed (gle={Marshal.GetLastWin32Error()}): {d}");
                }
                catch (Exception ex) { _log.Info($"AddDllDirectory threw for {d}: {ex.Message}"); }
            }
        }

        private static bool ProbeCoreToolkits()
        {
            try
            {
                var h = LoadLibraryW("CoreToolkits.dll");
                if (h == IntPtr.Zero) return false;
                FreeLibrary(h);
                return true;
            }
            catch { return false; }
        }

        private static Assembly OnAssemblyResolve(object sender, ResolveEventArgs args)
        {
            var name = new AssemblyName(args.Name).Name;
            var dllName = name + ".dll";
            foreach (var dir in _assemblySearchDirs)
            {
                var path = Path.Combine(dir, dllName);
                if (!File.Exists(path)) continue;
                try
                {
                    return Assembly.LoadFrom(path);
                }
                catch (Exception ex)
                {
                    _log.Error($"AssemblyResolve found but failed to load: {name} <- {path}", ex);
                }
            }
            // Empty result is normal for assemblies the CLR resolves through other means
            // (GAC, default probing). We only flag this when the requester is one of ours
            // so we don't drown the log in noise from framework probes.
            if (LooksLikeOurDependency(name))
                _log.Error($"AssemblyResolve miss: {name} not found in any search dir (requested by {args.RequestingAssembly?.GetName().Name ?? "?"})");
            return null;
        }

        private static bool LooksLikeOurDependency(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            // Satellite-assembly probes (e.g. "VideoOS.Platform.resources") are CLR
            // localization lookups that are expected to miss for an unlocalized binary.
            if (name.EndsWith(".resources", StringComparison.OrdinalIgnoreCase)) return false;
            return name.StartsWith("VideoOS.", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("zxing", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("CommunitySDK", StringComparison.OrdinalIgnoreCase);
        }

        private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            var ex = e.ExceptionObject as Exception;
            var typeName = ex?.GetType().Name ?? "Unknown";
            try
            {
                _log.Error($"Unhandled exception (terminating={e.IsTerminating})", ex ?? new Exception("non-Exception throwable"));
                if (ex is ReflectionTypeLoadException rtle && rtle.LoaderExceptions != null)
                {
                    for (int i = 0; i < rtle.LoaderExceptions.Length; i++)
                    {
                        _log.Error($"LoaderException[{i}]", rtle.LoaderExceptions[i]);
                    }
                }
                WriteStatus("Error:Unhandled:" + typeName);
            }
            catch
            {
                // Last-chance handler must never throw  it runs while the runtime is dying.
            }
        }

        private static void OnUnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
        {
            try
            {
                _log.Error("Unobserved task exception", e.Exception);
                e.SetObserved();
            }
            catch { }
        }
    }
}
