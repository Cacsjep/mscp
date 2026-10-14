using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LowLatencyVideoWall.Config;
using LowLatencyVideoWall.Model;
using LowLatencyVideoWall.Render;
using LowLatencyVideoWall.Vapix;

namespace LowLatencyVideoWall.Client
{
    /// <summary>
    /// Turns a stored view profile into a set of live panes.
    ///
    /// This is the one place that touches camera discovery, credentials and the renderer at
    /// the same time, so the awkward parts stay in a single file. Credentials are read live
    /// and never persisted, which is why the Smart Client role needs permission to read
    /// hardware passwords.
    ///
    /// Layout and stream resolution are deliberately NOT decided here. The renderer owns both,
    /// because it is the only thing that knows the wall's pixel size and when a pane is
    /// maximised. All this does is hand it the camera, the credentials, the list of
    /// resolutions the camera supports, and where the pane goes in normalised space.
    /// </summary>
    internal static class WallSessionBuilder
    {
        internal sealed class BuildResult
        {
            internal List<WallPane> Panes { get; } = new List<WallPane>();
            internal List<string> Problems { get; } = new List<string>();
            internal ViewProfile Profile { get; set; }
        }

        /// <summary>
        /// Resolves every pane in the profile. Cameras that cannot be resolved are reported
        /// rather than silently dropped, because a black pane with no explanation is the
        /// worst possible failure mode on a video wall.
        /// </summary>
        internal static async Task<BuildResult> BuildAsync(ViewProfile profile, CancellationToken token)
        {
            var result = new BuildResult { Profile = profile };
            if (profile == null) return result;

            var cameras = SafeDiscover(result);
            var byId = cameras.ToDictionary(c => c.CameraId, c => c);

            foreach (var definition in profile.Panes)
            {
                if (token.IsCancellationRequested) break;

                if (!byId.TryGetValue(definition.CameraId, out var camera))
                {
                    result.Problems.Add($"{Describe(definition)}: not found in the Milestone configuration");
                    continue;
                }

                if (!camera.IsAxis)
                {
                    result.Problems.Add($"{camera.Name}: not an Axis camera, direct streaming is not supported");
                    continue;
                }

                var password = await Task.Run(() => CameraDiscovery.ReadPassword(camera.HardwarePath), token)
                    .ConfigureAwait(false);

                if (string.IsNullOrEmpty(password))
                {
                    // Almost always a rights problem rather than a wrong password, so say so.
                    result.Problems.Add(
                        $"{camera.Name}: could not read the camera password. The Smart Client role " +
                        "needs permission to read hardware passwords.");
                    continue;
                }

                var supported = await ReadSupportedResolutionsAsync(camera, password, token).ConfigureAwait(false);

                result.Panes.Add(new WallPane(camera, password, supported, definition));
            }

            return result;
        }

        private static List<WallCamera> SafeDiscover(BuildResult result)
        {
            try
            {
                return CameraDiscovery.Discover() ?? new List<WallCamera>();
            }
            catch (Exception ex)
            {
                result.Problems.Add($"Camera discovery failed: {ex.Message}");
                LowLatencyVideoWallDefinition.Log.Error($"Camera discovery failed: {ex}");
                return new List<WallCamera>();
            }
        }

        /// <summary>
        /// The camera's supported resolution list, which is what lets the renderer ask for a
        /// small stream for a small pane. A failure here is not fatal: without the list the
        /// pane simply streams at the camera's default.
        /// </summary>
        private static async Task<string> ReadSupportedResolutionsAsync(WallCamera camera,
            string password, CancellationToken token)
        {
            try
            {
                var status = await CameraStatusService.ReadAsync(camera, password, token).ConfigureAwait(false);
                return status?.Resolution;
            }
            catch (Exception ex)
            {
                LowLatencyVideoWallDefinition.Log.Info(
                    $"Could not read supported resolutions for {camera.Name}, using the camera default: {ex.Message}");
                return null;
            }
        }

        private static string Describe(PaneDefinition definition)
            => string.IsNullOrWhiteSpace(definition.CameraName)
                ? definition.CameraId.ToString()
                : definition.CameraName;

        /// <summary>
        /// The view a client should show at startup: whatever a rule already asked for,
        /// otherwise the profile assigned to the logged in user.
        /// </summary>
        internal static ViewProfile ResolveStartupProfile(string userName, Guid? pendingFromRule)
        {
            if (pendingFromRule.HasValue && pendingFromRule.Value != Guid.Empty)
            {
                var requested = WallConfigStore.LoadViewProfile(pendingFromRule.Value);
                if (requested != null) return requested;
            }

            return WallConfigStore.ResolveForUser(userName);
        }
    }
}
