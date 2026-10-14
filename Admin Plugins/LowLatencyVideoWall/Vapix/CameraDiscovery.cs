using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LowLatencyVideoWall.Model;
using VideoOS.Platform;
using VideoOS.Platform.ConfigurationItems;

namespace LowLatencyVideoWall.Vapix
{
    /// <summary>
    /// Reads the camera list out of the Milestone configuration, address, user name and
    /// password included, so nothing about a camera has to be typed into this plugin twice.
    ///
    /// The walk is recording server to hardware to camera channel. The hardware level is
    /// where the address and the credentials live, the camera level is where the device Guid
    /// and the channel number live, and both halves are needed before an RTSP or a VAPIX
    /// request can be built.
    /// </summary>
    internal static class CameraDiscovery
    {
        /// <summary>
        /// Every enabled camera channel on every recording server, ordered for display.
        /// Never throws: a configuration this plugin cannot read means an empty list and a
        /// line in MIPLog, not a broken Management Client node.
        /// </summary>
        internal static List<WallCamera> Discover(CancellationToken token = default(CancellationToken))
        {
            var cameras = new List<WallCamera>();

            try
            {
                var management = new ManagementServer(EnvironmentManager.Instance.MasterSite);

                foreach (var server in management.RecordingServerFolder.RecordingServers)
                {
                    if (token.IsCancellationRequested) break;

                    try
                    {
                        DiscoverOnServer(server, cameras, token);
                    }
                    catch (Exception ex)
                    {
                        // One unreachable recording server must not cost us the others.
                        LowLatencyVideoWallDefinition.Log.Error(
                            "Camera discovery failed on recording server '" + server.Name + "'", ex);
                    }
                }
            }
            catch (Exception ex)
            {
                LowLatencyVideoWallDefinition.Log.Error("Camera discovery failed", ex);
            }

            LowLatencyVideoWallDefinition.Log.Info("Camera discovery found " + cameras.Count + " camera(s)");

            return cameras
                .OrderBy(c => c.RecordingServer, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// Discover on a worker thread. The configuration API is a blocking call to the
        /// management server and takes seconds on a large system, which is far too long to
        /// hold a UI thread for.
        /// </summary>
        internal static Task<List<WallCamera>> DiscoverAsync(CancellationToken token = default(CancellationToken))
        {
            return Task.Run(() => Discover(token), token);
        }

        /// <summary>
        /// The camera password Milestone holds for this hardware, or null when it cannot be
        /// read. ReadPasswordHardware is a server task, but GetProperty returns synchronously
        /// once it has run, which is why no polling is needed here.
        /// </summary>
        internal static string ReadPassword(string hardwarePath)
        {
            if (string.IsNullOrEmpty(hardwarePath))
            {
                LowLatencyVideoWallDefinition.Log.Info("Cannot read a camera password without a hardware path");
                return null;
            }

            try
            {
                var hardware = new Hardware(
                    EnvironmentManager.Instance.MasterSite.ServerId,
                    hardwarePath);

                var task = hardware.ReadPasswordHardware();
                var password = task.GetProperty("Password");

                if (string.IsNullOrEmpty(password))
                {
                    LowLatencyVideoWallDefinition.Log.Info(
                        "Milestone returned an empty password for hardware '" + hardware.Name + "'");
                    return password;
                }

                return password;
            }
            catch (Exception ex)
            {
                // Usually a rights problem: reading a hardware password needs the hardware
                // read password right, which an ordinary operator role does not have.
                LowLatencyVideoWallDefinition.Log.Error(
                    "Could not read the camera password for hardware path '" + hardwarePath + "'", ex);
                return null;
            }
        }

        private static void DiscoverOnServer(RecordingServer server, List<WallCamera> cameras,
            CancellationToken token)
        {
            foreach (var hardware in server.HardwareFolder.Hardwares)
            {
                if (token.IsCancellationRequested) return;
                if (!hardware.Enabled) continue;

                try
                {
                    if (IsLoopback(hardware.Address))
                    {
                        // Nothing this plugin can stream: a loopback address only resolves on
                        // the recording server itself, not on the machine showing the wall.
                        LowLatencyVideoWallDefinition.Log.Info(
                            "Skipping hardware '" + hardware.Name + "' on a loopback address");
                        continue;
                    }

                    AddChannels(server, hardware, cameras, token);
                }
                catch (Exception ex)
                {
                    LowLatencyVideoWallDefinition.Log.Error(
                        "Could not read hardware '" + hardware.Name + "'", ex);
                }
            }
        }

        private static void AddChannels(RecordingServer server, Hardware hardware,
            List<WallCamera> cameras, CancellationToken token)
        {
            var address = hardware.Address;
            var userName = hardware.UserName;
            var model = hardware.Model;
            var hardwarePath = hardware.Path;
            var serverName = server.Name;

            foreach (var camera in hardware.CameraFolder.Cameras)
            {
                if (token.IsCancellationRequested) return;
                if (!camera.Enabled) continue;

                cameras.Add(new WallCamera
                {
                    // The camera device Guid, not the hardware Guid. This is the id every
                    // other Milestone surface uses: view items, rules and Item lookups.
                    CameraId = CameraGuid(camera),
                    Name = camera.Name,
                    HardwarePath = hardwarePath,
                    Address = address,
                    UserName = userName,
                    Model = model,
                    RecordingServer = serverName,
                    Channel = camera.Channel
                });
            }
        }

        private static Guid CameraGuid(Camera camera)
        {
            try
            {
                if (camera.Guid != Guid.Empty) return camera.Guid;
            }
            catch
            {
            }

            // Older configuration items only fill the string id.
            Guid parsed;
            return Guid.TryParse(camera.Id, out parsed) ? parsed : Guid.Empty;
        }

        private static bool IsLoopback(string address)
        {
            if (string.IsNullOrEmpty(address)) return false;

            try
            {
                var uri = VapixClient.BuildBaseUri(address);
                if (uri == null) return false;

                var host = uri.Host;
                return host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                    || host == "127.0.0.1"
                    || host == "::1"
                    || host == "[::1]";
            }
            catch
            {
                return false;
            }
        }
    }
}
