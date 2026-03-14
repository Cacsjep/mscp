using System;
using System.IO;
using System.Net;
using System.Reflection;
using System.Threading;
using CommunitySDK;
using RTMPStreamer.Streaming;
using VideoOS.Platform;

namespace RTMPStreamerHelper
{
    class Program
    {
        private static readonly PluginLog _log = new PluginLog("RTMPStreamer");
        private static string[] _assemblySearchDirs;

        static int Main(string[] args)
        {
            if (args.Length < 3)
            {
                Console.Error.WriteLine("Usage: RTMPStreamerHelper <serverUri> <cameraId> <rtmpUrl> [milestoneDir]");
                return 1;
            }

            string serverUri = args[0];
            string cameraIdStr = args[1];
            string rtmpUrl = args[2];
            bool allowUntrustedCerts = args.Length >= 5
                && string.Equals(args[4], "true", StringComparison.OrdinalIgnoreCase);

            // Set up assembly resolution BEFORE any Milestone code runs
            _assemblySearchDirs = BuildSearchDirs(args.Length >= 4 ? args[3] : null);
            AppDomain.CurrentDomain.AssemblyResolve += OnAssemblyResolve;

            if (!Guid.TryParse(cameraIdStr, out Guid cameraId))
            {
                _log.Error($"Invalid camera ID: {cameraIdStr}");
                return 1;
            }

            _log.Info($"Starting RTMP stream helper");
            _log.Info($"  Server: {serverUri}");
            _log.Info($"  Camera: {cameraId}");
            _log.Info($"  RTMP:   {MaskStreamKey(rtmpUrl)}");
            _log.Info($"  Assembly search dirs: {string.Join("; ", _assemblySearchDirs)}");

            StreamSession session = null;
            var exitEvent = new ManualResetEvent(false);

            Console.CancelKeyPress += (s, e) =>
            {
                e.Cancel = true;
                _log.Info("Ctrl+C received, shutting down...");
                exitEvent.Set();
            };

            try
            {
                // Initialize MIP SDK in standalone mode
                _log.Info("Initializing MIP SDK...");
                VideoOS.Platform.SDK.Environment.Initialize();

                var uri = new Uri(serverUri);
                VideoOS.Platform.SDK.Environment.AddServer(uri, CredentialCache.DefaultNetworkCredentials);

                _log.Info("Logging in to management server...");
                VideoOS.Platform.SDK.Environment.Login(uri);

                _log.Info("Login successful, initializing media environment...");
                VideoOS.Platform.SDK.Media.Environment.Initialize();

                _log.Info("Loading configuration...");

                // Resolve camera
                var cameraItem = Configuration.Instance.GetItem(cameraId, Kind.Camera);
                if (cameraItem == null)
                {
                    _log.Error($"Camera not found: {cameraId}");
                    return 2;
                }

                _log.Info($"Camera resolved: {cameraItem.Name}");

                // Start streaming
                session = new StreamSession(cameraItem, rtmpUrl, allowUntrustedCerts);
                session.Start();

                _log.Info("Stream session started, waiting for exit signal...");

                // Report stats every 5 seconds so BackgroundPlugin can relay to admin
                var statsTimer = new Timer(_ =>
                {
                    if (session != null && session.IsRunning)
                    {
                        double fps = session.Uptime.TotalSeconds > 0
                            ? session.FramesSent / session.Uptime.TotalSeconds
                            : 0;
                        Console.Error.WriteLine(string.Format(
                            System.Globalization.CultureInfo.InvariantCulture,
                            "STATS frames={0} fps={1:F1} bytes={2} keyframes={3}",
                            session.FramesSent, fps, session.BytesSent, session.KeyFramesSent));
                    }
                }, null, 5000, 5000);

                // Wait until killed by parent or Ctrl+C
                exitEvent.WaitOne();

                statsTimer.Dispose();
            }
            catch (Exception ex)
            {
                _log.Error($"Fatal error: {ex}");
                return 3;
            }
            finally
            {
                _log.Info("Shutting down...");

                try { session?.Stop(); } catch { }
                try { session?.Dispose(); } catch { }

                try { VideoOS.Platform.SDK.Environment.Logout(); } catch { }
                try { VideoOS.Platform.SDK.Environment.RemoveAllServers(); } catch { }
                try { VideoOS.Platform.SDK.Environment.UnInitialize(); } catch { }

                _log.Info("Shutdown complete.");
            }

            return 0;
        }

        private static string[] BuildSearchDirs(string milestoneDir)
        {
            var dirs = new System.Collections.Generic.List<string>();

            // Directory passed by parent process (most reliable)
            if (!string.IsNullOrEmpty(milestoneDir) && Directory.Exists(milestoneDir))
                dirs.Add(milestoneDir);

            // Common Milestone install directories
            var candidates = new[]
            {
                @"C:\Program Files\Milestone\XProtect Event Server",
                @"C:\Program Files\Milestone\XProtect Recording Server",
                @"C:\Program Files\Milestone\XProtect Management Server"
            };

            foreach (var dir in candidates)
            {
                if (Directory.Exists(dir) && !dirs.Contains(dir))
                    dirs.Add(dir);
            }

            return dirs.ToArray();
        }

        private static string MaskStreamKey(string rtmpUrl)
        {
            try
            {
                var lastSlash = rtmpUrl.LastIndexOf('/');
                if (lastSlash > 0 && lastSlash < rtmpUrl.Length - 1)
                {
                    var key = rtmpUrl.Substring(lastSlash + 1);
                    if (key.Length > 4)
                        return rtmpUrl.Substring(0, lastSlash + 1) + key.Substring(0, 4) + "****";
                }
            }
            catch { }
            return "rtmp://****";
        }

        private static Assembly OnAssemblyResolve(object sender, ResolveEventArgs args)
        {
            var assemblyName = new AssemblyName(args.Name).Name + ".dll";

            foreach (var dir in _assemblySearchDirs)
            {
                var path = Path.Combine(dir, assemblyName);
                if (File.Exists(path))
                {
                    try
                    {
                        return Assembly.LoadFrom(path);
                    }
                    catch { }
                }
            }

            return null;
        }
    }
}
