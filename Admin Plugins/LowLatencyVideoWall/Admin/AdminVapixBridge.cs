using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LowLatencyVideoWall.Model;
using LowLatencyVideoWall.Vapix;

namespace LowLatencyVideoWall.Admin
{
    /// <summary>
    /// What one bulk change did to one camera, in the shape the admin UI needs.
    ///
    /// The UI deliberately depends on this type rather than on whatever the Vapix layer
    /// returns, so a signature change over in Vapix is a two line fix inside this file
    /// instead of a rewrite of the overview control.
    /// </summary>
    internal sealed class BulkOutcome
    {
        internal Guid CameraId { get; set; }
        internal string CameraName { get; set; } = "";
        internal string Setting { get; set; } = "";
        internal string OldValue { get; set; } = "";
        internal string NewValue { get; set; } = "";
        internal bool Success { get; set; }
        internal string Error { get; set; } = "";
    }

    /// <summary>The one click bulk changes the overview offers.</summary>
    internal enum BulkAction
    {
        LowLatencyModeOn,
        WdrOff
    }

    /// <summary>
    /// The only place in the admin UI that talks to the Vapix layer.
    ///
    /// Everything here runs off the UI thread by contract: callers are expected to be on a
    /// background task already. Passwords are cached per hardware path because reading them
    /// goes through the configuration API, which is a server roundtrip per hardware and a
    /// site with 200 cameras would otherwise pay it twice per refresh.
    /// </summary>
    internal static class AdminVapixBridge
    {
        private static readonly object PasswordLock = new object();

        private static readonly Dictionary<string, string> PasswordCache =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // ---- integration points with the Vapix layer -------------------------------------
        // Assumed surface, reconciled at integration:
        //   CameraDiscovery.Discover()                      -> List<WallCamera>
        //   CameraDiscovery.ReadPassword(string hardwarePath)       -> string or null
        //   CameraStatusService.ReadAsync(cam, password, token)     -> Task<CameraStatus>
        //   BulkConfigurator.Set*Async(cams, value, token, progress, pwdLookup) -> Task<List<BulkResult>>

        internal static List<WallCamera> DiscoverCameras()
        {
            var cameras = CameraDiscovery.Discover();
            return cameras ?? new List<WallCamera>();
        }

        internal static string PasswordFor(WallCamera camera)
        {
            var path = camera?.HardwarePath ?? "";
            if (path.Length == 0) return null;

            lock (PasswordLock)
            {
                if (PasswordCache.TryGetValue(path, out var cached)) return cached;
            }

            string password = null;
            try
            {
                password = CameraDiscovery.ReadPassword(path);
            }
            catch (Exception ex)
            {
                // A camera whose credentials cannot be read still belongs in the table, it
                // just reads as unreachable, so this is logged and swallowed.
                LowLatencyVideoWallDefinition.Log.Error(
                    $"Reading credentials for {camera?.Name} failed: {ex.Message}");
            }

            lock (PasswordLock)
            {
                PasswordCache[path] = password;
            }

            return password;
        }

        internal static void ForgetPasswords()
        {
            lock (PasswordLock)
            {
                PasswordCache.Clear();
            }
        }

        internal static Task<CameraStatus> ReadStatusAsync(WallCamera camera, CancellationToken token)
            => CameraStatusService.ReadAsync(camera, PasswordFor(camera), token);

        /// <summary>
        /// Applies one setting to every camera handed in and returns one outcome per camera.
        ///
        /// Every outcome is written to the plugin log with its old and new value before this
        /// returns, because the bulk buttons apply without a confirmation dialog and there is
        /// no undo: the log is the only record of what the site looked like before.
        /// </summary>
        internal static async Task<List<BulkOutcome>> ApplyAsync(
            BulkAction action, IReadOnlyList<WallCamera> cameras, CancellationToken token)
        {
            var outcomes = new List<BulkOutcome>();
            if (cameras == null || cameras.Count == 0) return outcomes;

            var label = Describe(action);
            LowLatencyVideoWallDefinition.Log.Info(
                $"Bulk change starting: {label} on {cameras.Count} camera(s)");

            var results = await DispatchAsync(action, cameras, token).ConfigureAwait(false);

            if (results != null)
            {
                foreach (var r in results)
                {
                    if (r == null) continue;

                    var outcome = new BulkOutcome
                    {
                        CameraId = r.CameraId,
                        CameraName = r.CameraName ?? "",
                        Setting = string.IsNullOrEmpty(r.Setting) ? label : r.Setting,
                        OldValue = r.OldValue ?? "",
                        NewValue = r.NewValue ?? "",
                        Success = r.Success,
                        Error = r.Error ?? ""
                    };

                    outcomes.Add(outcome);
                    LogOutcome(outcome);
                }
            }

            LowLatencyVideoWallDefinition.Log.Info(
                $"Bulk change finished: {label}, {CountSucceeded(outcomes)} ok, " +
                $"{outcomes.Count - CountSucceeded(outcomes)} failed");

            return outcomes;
        }

        private static void LogOutcome(BulkOutcome o)
        {
            if (o.Success)
            {
                LowLatencyVideoWallDefinition.Log.Info(
                    $"Bulk change applied: camera '{o.CameraName}' ({o.CameraId}) " +
                    $"setting '{o.Setting}' old '{o.OldValue}' new '{o.NewValue}'");
            }
            else
            {
                LowLatencyVideoWallDefinition.Log.Error(
                    $"Bulk change failed: camera '{o.CameraName}' ({o.CameraId}) " +
                    $"setting '{o.Setting}' old '{o.OldValue}' intended '{o.NewValue}' " +
                    $"error '{o.Error}'");
            }
        }

        internal static int CountSucceeded(IEnumerable<BulkOutcome> outcomes)
        {
            int n = 0;
            if (outcomes == null) return 0;
            foreach (var o in outcomes) if (o != null && o.Success) n++;
            return n;
        }

        internal static string Describe(BulkAction action)
        {
            switch (action)
            {
                case BulkAction.LowLatencyModeOn: return "Low latency mode on";
                case BulkAction.WdrOff: return "WDR off";
                default: return action.ToString();
            }
        }

        /// <summary>
        /// Maps a bulk button onto the configurator. There is no single generic entry point
        /// on purpose: each setting needs different arguments, and capture mode in particular
        /// has to confirm the sensor really switched before it is safe to write the frame
        /// rate, so a one-size enum would have hidden that.
        /// </summary>
        private static Task<List<BulkResult>> DispatchAsync(BulkAction action,
            IReadOnlyList<WallCamera> cameras, CancellationToken token)
        {
            switch (action)
            {
                case BulkAction.LowLatencyModeOn:
                    return BulkConfigurator.SetLowLatencyModeAsync(cameras, true, token, null, PasswordFor);

                case BulkAction.WdrOff:
                    return BulkConfigurator.SetWdrAsync(cameras, false, token, null, PasswordFor);

                default:
                    throw new ArgumentOutOfRangeException(nameof(action));
            }
        }
    }
}
