using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LowLatencyVideoWall.Model;

namespace LowLatencyVideoWall.Vapix
{
    /// <summary>
    /// What happened to one camera in one bulk change. Old and new value are both kept
    /// because the operator needs to see what was actually touched, and because a change that
    /// was a no-op is worth knowing about.
    /// </summary>
    internal sealed class BulkResult
    {
        internal Guid CameraId { get; set; }
        internal string CameraName { get; set; } = "";
        internal string Address { get; set; } = "";

        /// <summary>Human name of the setting that was applied, for the result list header.</summary>
        internal string Setting { get; set; } = "";

        internal string OldValue { get; set; } = "";
        internal string NewValue { get; set; } = "";

        internal bool Success { get; set; }
        internal string Error { get; set; }

        public override string ToString()
        {
            return Success
                ? CameraName + ": " + Setting + " " + OldValue + " to " + NewValue
                : CameraName + ": " + (Error ?? "failed");
        }
    }

    /// <summary>
    /// Applies one setting to many cameras at once.
    ///
    /// Two things shape this class. The first is that the slow part is the network, so the
    /// HTTP work fans out while the Milestone configuration reads stay on one thread. The
    /// second is that an Axis capture mode change cannot be verified by reading the parameter
    /// back; see ApplyCaptureModeAsync for what has to be done instead.
    /// </summary>
    internal static class BulkConfigurator
    {
        /// <summary>
        /// Eight at a time. High enough that a wall of cameras is done in a couple of
        /// seconds, low enough that a camera change storm does not swamp the network the wall
        /// is streaming video over at the same time.
        /// </summary>
        internal const int MaxConcurrency = 8;

        /// <summary>
        /// How long a capture mode change is given to become live. The sensor pipeline
        /// restarts, which on the measured hardware takes several seconds and can drop HTTP
        /// connections while it happens.
        /// </summary>
        private const int CaptureModeSettleSeconds = 45;

        private const int CaptureModePollMs = 1000;

        /// <summary>An update that restarts the sensor needs more than the usual 5 s.</summary>
        private const int CaptureModeUpdateTimeoutSeconds = 15;

        // ----- public operations -------------------------------------------------------

        /// <summary>
        /// ImageSource.I0.Sensor.LowLatencyMode. The most valuable of these operations: on the
        /// hardware measured here, off cost about 165 ms of glass to glass delay.
        /// </summary>
        internal static Task<List<BulkResult>> SetLowLatencyModeAsync(IEnumerable<WallCamera> cameras,
            bool on, CancellationToken token,
            IProgress<BulkResult> progress = null, Func<WallCamera, string> passwordFor = null)
        {
            var value = on ? "on" : "off";
            return RunAsync(cameras, "Low latency mode",
                (client, result, cancel) => ApplySimpleAsync(client, result,
                    VapixKeys.SensorGroup, VapixKeys.LowLatencyMode, value, cancel),
                token, progress, passwordFor);
        }

        /// <summary>
        /// ImageSource.I0.Sensor.WDR. Costs about 35 ms when on, and does not exist at all in
        /// a 50/60 fps capture mode, which is reported rather than treated as a failure.
        /// </summary>
        internal static Task<List<BulkResult>> SetWdrAsync(IEnumerable<WallCamera> cameras,
            bool on, CancellationToken token,
            IProgress<BulkResult> progress = null, Func<WallCamera, string> passwordFor = null)
        {
            var value = on ? "on" : "off";
            return RunAsync(cameras, "WDR",
                (client, result, cancel) => ApplySimpleAsync(client, result,
                    VapixKeys.SensorGroup, VapixKeys.Wdr, value, cancel),
                token, progress, passwordFor);
        }

        /// <summary>
        /// ImageSource.I0.Sensor.CaptureMode, verified the hard way, and then the stream FPS
        /// that only sticks once the new mode is live.
        /// </summary>
        /// <param name="captureMode">The capture mode number as the camera reports it.</param>
        /// <param name="highFrameRateMode">
        /// True when the target mode is a 50/60 fps mode. Those modes have no WDR parameter,
        /// and that absence is the only trustworthy sign the sensor has really switched.
        /// </param>
        /// <param name="streamFps">
        /// FPS to write to Image.I0.Stream.FPS after the mode is live, or 0 to leave it alone.
        /// </param>
        internal static Task<List<BulkResult>> SetCaptureModeAsync(IEnumerable<WallCamera> cameras,
            string captureMode, bool highFrameRateMode, int streamFps, CancellationToken token,
            IProgress<BulkResult> progress = null, Func<WallCamera, string> passwordFor = null)
        {
            return RunAsync(cameras, "Capture mode",
                (client, result, cancel) => ApplyCaptureModeAsync(client, result,
                    captureMode, highFrameRateMode, streamFps, cancel),
                token, progress, passwordFor);
        }

        // ----- the one that needs care -------------------------------------------------

        /// <summary>
        /// Changes the capture mode and only reports success once the sensor is genuinely
        /// running it.
        ///
        /// This cannot be done by reading the parameter back. CaptureMode reads back as the
        /// new value the moment param.cgi accepts the update, while the sensor is still in
        /// the old mode, so a read back always says yes and means nothing. Two measured facts
        /// give a way out:
        ///
        ///   - a 50/60 fps mode has no WDR, so ImageSource.I0.Sensor.WDR disappears from the
        ///     parameter list (the camera answers "# Error: Error -1 getting param" for it)
        ///     once the new mode is live, and reappears on the way back to a 25/30 mode;
        ///   - Image.I0.Stream.FPS is silently clamped to 25 until the new mode is live, so
        ///     writing the FPS first looks like it worked and leaves the camera at 25.
        ///
        /// So: change the mode, poll until the WDR parameter matches what the target mode
        /// should have, and only then write the FPS.
        /// </summary>
        private static async Task ApplyCaptureModeAsync(VapixClient client, BulkResult result,
            string captureMode, bool highFrameRateMode, int streamFps, CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(captureMode))
            {
                result.Error = "No capture mode was given";
                return;
            }

            var before = await client.ListResultAsync(VapixKeys.SensorGroup, token).ConfigureAwait(false);
            if (!before.Ok)
            {
                result.Error = before.Error;
                return;
            }

            result.OldValue = Value(before, VapixKeys.CaptureMode);
            result.NewValue = captureMode;

            var update = await client.UpdateResultAsync(
                new Dictionary<string, string> { { VapixKeys.CaptureMode, captureMode } },
                token, CaptureModeUpdateTimeoutSeconds).ConfigureAwait(false);

            if (!update.Ok)
            {
                result.Error = update.Error;
                return;
            }

            var wdrShouldBePresent = !highFrameRateMode;
            var live = await WaitForCaptureModeAsync(client, wdrShouldBePresent, token).ConfigureAwait(false);
            if (!live)
            {
                result.Error = token.IsCancellationRequested
                    ? "Cancelled while waiting for capture mode " + captureMode + " to become active"
                    : "The camera accepted capture mode " + captureMode + " but was still reporting the " +
                      "old mode after " + CaptureModeSettleSeconds + " s. The parameter now reads " +
                      captureMode + ", which it does whether or not the sensor switched, so check the " +
                      "camera before trusting it.";
                return;
            }

            if (streamFps > 0)
            {
                var fps = streamFps.ToString(CultureInfo.InvariantCulture);

                var fpsUpdate = await client.UpdateResultAsync(
                    new Dictionary<string, string> { { VapixKeys.Fps, fps } }, token).ConfigureAwait(false);

                if (!fpsUpdate.Ok)
                {
                    result.Error = "Capture mode " + captureMode + " is active but the FPS change failed: "
                                   + fpsUpdate.Error;
                    return;
                }

                var readBack = await client.ListResultAsync(VapixKeys.StreamGroup, token).ConfigureAwait(false);
                var applied = readBack.Ok ? Value(readBack, VapixKeys.Fps) : "";

                if (readBack.Ok && !string.Equals(applied, fps, StringComparison.OrdinalIgnoreCase))
                {
                    // Almost always the clamp to 25 described above, which means the mode was
                    // reported live a moment too early.
                    result.Error = "Capture mode " + captureMode + " is active but the camera kept FPS at "
                                   + applied + " instead of " + fps +
                                   ". Retry the FPS change in a few seconds.";
                    return;
                }

                result.NewValue = captureMode + ", " + fps + " fps";
            }

            result.Success = true;
        }

        private static async Task<bool> WaitForCaptureModeAsync(VapixClient client,
            bool wdrShouldBePresent, CancellationToken token)
        {
            var deadline = DateTime.UtcNow.AddSeconds(CaptureModeSettleSeconds);

            while (DateTime.UtcNow < deadline)
            {
                if (token.IsCancellationRequested) return false;

                var probe = await client.ListResultAsync(VapixKeys.SensorGroup, token).ConfigureAwait(false);

                // A failed probe is expected while the sensor pipeline restarts, so it counts
                // as "not yet" rather than as a broken change.
                if (probe.Ok && probe.Values.ContainsKey(VapixKeys.Wdr) == wdrShouldBePresent)
                    return true;

                try
                {
                    await Task.Delay(CaptureModePollMs, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return false;
                }
            }

            return false;
        }

        // ----- the straightforward ones ------------------------------------------------

        /// <summary>
        /// Writes one parameter and reads it back. Unlike the capture mode, these parameters
        /// are honest about their value the moment they are accepted, so a read back is worth
        /// doing: it catches a camera that silently rounded or ignored the value.
        /// </summary>
        private static async Task ApplySimpleAsync(VapixClient client, BulkResult result,
            string group, string key, string value, CancellationToken token)
        {
            var before = await client.ListResultAsync(group, token).ConfigureAwait(false);
            if (!before.Ok)
            {
                result.Error = before.Error;
                return;
            }

            if (!before.Values.ContainsKey(key))
            {
                // Either the model does not have the parameter or the current capture mode
                // has taken it away, which is what happens to WDR in a 50/60 fps mode.
                result.OldValue = CameraStatusService.NotAvailable;
                result.NewValue = value;
                result.Error = "This camera does not currently have " + key +
                               (key == VapixKeys.Wdr
                                   ? ". On a 50/60 fps capture mode the WDR parameter is gone by design."
                                   : ", so the model or the firmware does not support it.");
                return;
            }

            result.OldValue = Value(before, key);
            result.NewValue = value;

            var update = await client.UpdateResultAsync(
                new Dictionary<string, string> { { key, value } }, token).ConfigureAwait(false);

            if (!update.Ok)
            {
                result.Error = update.Error;
                return;
            }

            var after = await client.ListResultAsync(group, token).ConfigureAwait(false);
            if (after.Ok && after.Values.ContainsKey(key))
            {
                var applied = Value(after, key);
                if (!string.Equals(applied, value, StringComparison.OrdinalIgnoreCase))
                {
                    result.NewValue = applied;
                    result.Error = "The camera took the change but reports " + applied +
                                   " instead of " + value;
                    return;
                }
            }

            result.Success = true;
        }

        // ----- fan out -----------------------------------------------------------------

        private static async Task<List<BulkResult>> RunAsync(IEnumerable<WallCamera> cameras,
            string setting, Func<VapixClient, BulkResult, CancellationToken, Task> apply,
            CancellationToken token, IProgress<BulkResult> progress, Func<WallCamera, string> passwordFor)
        {
            var targets = (cameras ?? Enumerable.Empty<WallCamera>()).Where(c => c != null).ToList();
            if (targets.Count == 0) return new List<BulkResult>();

            var passwords = await ReadPasswordsAsync(targets, passwordFor, token).ConfigureAwait(false);

            List<BulkResult> results;
            using (var gate = new SemaphoreSlim(MaxConcurrency))
            {
                var running = targets
                    .Select(camera => ApplyOneAsync(camera, setting, apply, gate, passwords, token, progress))
                    .ToList();

                results = (await Task.WhenAll(running).ConfigureAwait(false)).ToList();
            }

            var succeeded = results.Count(r => r.Success);
            LowLatencyVideoWallDefinition.Log.Info(
                setting + ": " + succeeded + " of " + results.Count + " camera(s) changed");

            foreach (var failed in results.Where(r => !r.Success))
            {
                LowLatencyVideoWallDefinition.Log.Info(
                    setting + " failed on '" + failed.CameraName + "' (" + failed.Address + "): " + failed.Error);
            }

            return results;
        }

        /// <summary>
        /// Passwords come from the Milestone configuration API, which is a blocking call to
        /// the management server. They are read once per hardware, off the calling thread and
        /// one at a time: eight threads into the configuration API buys nothing and can stall
        /// the client, while the HTTP work afterwards is what actually benefits from fanning
        /// out.
        /// </summary>
        private static Task<Dictionary<string, string>> ReadPasswordsAsync(List<WallCamera> targets,
            Func<WallCamera, string> passwordFor, CancellationToken token)
        {
            return Task.Run(() =>
            {
                var passwords = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                foreach (var camera in targets)
                {
                    if (token.IsCancellationRequested) break;

                    var path = camera.HardwarePath ?? "";
                    if (passwords.ContainsKey(path)) continue;

                    try
                    {
                        passwords[path] = passwordFor != null
                            ? passwordFor(camera)
                            : CameraDiscovery.ReadPassword(path);
                    }
                    catch (Exception ex)
                    {
                        LowLatencyVideoWallDefinition.Log.Error(
                            "Could not read the password for '" + camera.Name + "'", ex);
                        passwords[path] = null;
                    }
                }

                return passwords;
            }, token);
        }

        private static async Task<BulkResult> ApplyOneAsync(WallCamera camera, string setting,
            Func<VapixClient, BulkResult, CancellationToken, Task> apply, SemaphoreSlim gate,
            Dictionary<string, string> passwords, CancellationToken token, IProgress<BulkResult> progress)
        {
            var result = new BulkResult
            {
                CameraId = camera.CameraId,
                CameraName = camera.Name ?? "",
                Address = camera.Address ?? "",
                Setting = setting
            };

            try
            {
                await gate.WaitAsync(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                result.Error = "Cancelled before the change was sent";
                return result;
            }

            try
            {
                string password;
                passwords.TryGetValue(camera.HardwarePath ?? "", out password);

                var client = VapixClient.For(camera.Address, camera.UserName, password);
                if (client == null)
                {
                    result.Error = "'" + result.Address + "' is not an address this plugin can reach";
                }
                else
                {
                    await apply(client, result, token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                result.Success = false;
                result.Error = "Cancelled";
            }
            catch (Exception ex)
            {
                // A bulk change over dozens of cameras must come back with a row per camera,
                // never with a single exception that loses the rest of the work.
                result.Success = false;
                result.Error = ex.GetType().Name + ": " + ex.Message;
            }
            finally
            {
                try
                {
                    gate.Release();
                }
                catch
                {
                }
            }

            if (progress != null)
            {
                try
                {
                    progress.Report(result);
                }
                catch (Exception ex)
                {
                    LowLatencyVideoWallDefinition.Log.Error("Bulk progress callback threw", ex);
                }
            }

            return result;
        }

        private static string Value(VapixResult result, string key)
        {
            string value;
            return result.Values.TryGetValue(key, out value) ? (value ?? "") : "";
        }
    }
}
