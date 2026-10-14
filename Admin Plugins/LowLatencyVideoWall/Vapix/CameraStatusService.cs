using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using LowLatencyVideoWall.Model;

namespace LowLatencyVideoWall.Vapix
{
    /// <summary>
    /// The VAPIX parameters this plugin cares about, in one place, because the status reader
    /// and the bulk configurator have to agree on them to the letter.
    /// </summary>
    internal static class VapixKeys
    {
        internal const string BrandGroup = "Brand";
        internal const string SystemGroup = "Properties.System";
        internal const string FirmwareGroup = "Properties.Firmware";
        internal const string ImagePropertiesGroup = "Properties.Image";

        internal const string SensorGroup = "ImageSource.I0.Sensor";
        internal const string CaptureMode = SensorGroup + ".CaptureMode";
        internal const string Wdr = SensorGroup + ".WDR";
        internal const string LowLatencyMode = SensorGroup + ".LowLatencyMode";

        internal const string AppearanceGroup = "Image.I0.Appearance";
        internal const string Rotation = AppearanceGroup + ".Rotation";
        internal const string Resolution = AppearanceGroup + ".Resolution";

        internal const string StreamGroup = "Image.I0.Stream";
        internal const string Fps = StreamGroup + ".FPS";

        internal const string MpegGroup = "Image.I0.MPEG";
        internal const string H264Profile = MpegGroup + ".H264.Profile";
    }

    /// <summary>
    /// Asks a camera what it is actually doing, and says where that hurts latency.
    ///
    /// The camera is the only authority here. Milestone knows what it asked for, and the
    /// camera's own web pages show what was last typed, but neither of them knows whether a
    /// capture mode change ever took effect. Everything in CameraStatus except DeliveredFps
    /// comes from param.cgi; DeliveredFps is measured off the running stream by the renderer
    /// and left untouched by this class.
    /// </summary>
    internal static class CameraStatusService
    {
        /// <summary>
        /// The groups one status read needs. Properties.Firmware is in here although it was
        /// not asked for, because the firmware version moved out of Properties.System on
        /// current Axis firmware and that column would otherwise stay empty.
        /// </summary>
        private static readonly string[] Groups =
        {
            VapixKeys.BrandGroup,
            VapixKeys.SystemGroup,
            VapixKeys.FirmwareGroup,
            VapixKeys.ImagePropertiesGroup,
            VapixKeys.SensorGroup,
            VapixKeys.AppearanceGroup,
            VapixKeys.StreamGroup,
            VapixKeys.MpegGroup
        };

        /// <summary>What a parameter the current capture mode does not have is reported as.</summary>
        internal const string NotAvailable = "n/a";

        /// <summary>Rotations that cost frame rate on the sensors measured here.</summary>
        private const int QuarterTurn = 90;
        private const int ThreeQuarterTurn = 270;

        internal static async Task<CameraStatus> ReadAsync(WallCamera camera, string password,
            CancellationToken token)
        {
            var status = new CameraStatus { ReadAtUtc = DateTime.UtcNow };

            if (camera == null)
            {
                status.Error = "No camera was given";
                return status;
            }

            status.CameraId = camera.CameraId;

            var client = VapixClient.For(camera.Address, camera.UserName, password);
            if (client == null)
            {
                status.Error = "'" + (camera.Address ?? "") + "' is not an address this plugin can reach";
                return status;
            }

            var parameters = await ReadGroupsAsync(client, token).ConfigureAwait(false);

            if (!parameters.Ok)
            {
                status.Error = parameters.Error;
                return status;
            }

            if (parameters.Values.Count == 0)
            {
                status.Error = string.IsNullOrEmpty(parameters.ParameterError)
                    ? "The camera answered but returned no parameters"
                    : "The camera returned no parameters: " + parameters.ParameterError;
                return status;
            }

            status.Reachable = true;
            Fill(status, parameters.Values);
            await ResolveCaptureModeNameAsync(client, status, parameters.Values, token).ConfigureAwait(false);
            AddWarnings(status);

            return status;
        }

        /// <summary>
        /// param.cgi takes a comma separated group list, so a whole status read is one round
        /// trip instead of eight. That matters because the overview reads every camera on the
        /// wall, one after another, on a timer.
        /// </summary>
        private static async Task<VapixResult> ReadGroupsAsync(VapixClient client, CancellationToken token)
        {
            var combined = await client.ListResultAsync(string.Join(",", Groups), token).ConfigureAwait(false);
            if (combined.Ok && combined.Values.Count > 0) return combined;
            if (token.IsCancellationRequested) return combined;

            // Some firmware answers a multi group list with a single error rather than with
            // the groups it does know. Falling back to one call per group costs round trips
            // but keeps a working camera from being reported as broken.
            var merged = new VapixResult { Ok = true, Body = "" };
            var failures = new List<string>();

            foreach (var group in Groups)
            {
                if (token.IsCancellationRequested) break;

                var one = await client.ListResultAsync(group, token).ConfigureAwait(false);
                if (!one.Ok)
                {
                    failures.Add(group + ": " + one.Error);
                    continue;
                }

                foreach (var pair in one.Values) merged.Values[pair.Key] = pair.Value;
                if (!string.IsNullOrEmpty(one.ParameterError)) merged.ParameterError = one.ParameterError;
            }

            if (merged.Values.Count == 0)
            {
                merged.Ok = false;
                merged.Error = failures.Count > 0
                    ? string.Join("; ", failures)
                    : (combined.Error ?? "The camera returned no parameters");
            }

            return merged;
        }

        private static void Fill(CameraStatus status, Dictionary<string, string> values)
        {
            status.ProductName = FirstOf(values, "Brand.ProdFullName", "Brand.ProdNbr", "Brand.ProdShortName");
            status.FirmwareVersion = FirstOf(values,
                "Properties.Firmware.Version", "Properties.System.Version", "Properties.Firmware.BuildNumber");

            // WDR has no value in a 50/60 fps capture mode: the camera drops the parameter
            // altogether and answers "# Error: Error -1 getting param" for it. Absence is
            // therefore information, not a read failure, and is reported as such.
            status.Wdr = Optional(values, VapixKeys.Wdr);

            // Same treatment for low latency mode, which only exists on models that have it.
            status.LowLatencyMode = Optional(values, VapixKeys.LowLatencyMode);

            status.CaptureMode = Get(values, VapixKeys.CaptureMode);
            status.ConfiguredFps = GetInt(values, VapixKeys.Fps);
            status.Rotation = GetInt(values, VapixKeys.Rotation);
            status.Resolution = Get(values, VapixKeys.Resolution);
            status.H264Profile = Get(values, VapixKeys.H264Profile);
        }

        /// <summary>
        /// Turns the capture mode number into something a human can act on. The cheap source
        /// is tried first: several firmware generations list the mode names as ordinary
        /// parameters, and only the rest need the extra listdefinitions round trip.
        /// </summary>
        private static async Task ResolveCaptureModeNameAsync(VapixClient client, CameraStatus status,
            Dictionary<string, string> values, CancellationToken token)
        {
            if (string.IsNullOrEmpty(status.CaptureMode)) return;

            var local = Get(values, VapixKeys.SensorGroup + ".CaptureMode" + status.CaptureMode + ".Name");
            if (!string.IsNullOrEmpty(local))
            {
                status.CaptureModeName = local;
                return;
            }

            var definitions = await client.ListDefinitionsAsync(VapixKeys.CaptureMode, token).ConfigureAwait(false);
            if (definitions == null) return;

            string name;
            if (definitions.TryGetValue(status.CaptureMode, out name))
                status.CaptureModeName = name;
        }

        /// <summary>
        /// The actionable half of the status. Every number quoted here was measured on the
        /// cameras this plugin was built against, so the text says what the operator buys by
        /// changing the setting rather than just that the setting is unusual.
        /// </summary>
        private static void AddWarnings(CameraStatus status)
        {
            if (IsOff(status.LowLatencyMode))
            {
                status.Warnings.Add(
                    "Low latency mode is off. Turning it on took about 165 ms off the glass to glass " +
                    "delay on the cameras measured for this project, which is the single biggest win " +
                    "available here.");
            }
            else if (IsNotAvailable(status.LowLatencyMode))
            {
                status.Warnings.Add(
                    "This camera does not expose " + VapixKeys.LowLatencyMode + ", so the roughly 165 ms " +
                    "that setting saves cannot be had on this model or firmware.");
            }

            if (IsOn(status.Wdr))
            {
                status.Warnings.Add(
                    "WDR is on, which costs about 35 ms of latency. Leave it on only where the scene " +
                    "really needs the dynamic range.");
            }

            if (status.Rotation == QuarterTurn || status.Rotation == ThreeQuarterTurn)
            {
                status.Warnings.Add(
                    "The image is rotated " + status.Rotation + " degrees. A 90 or 270 degree rotation " +
                    "can halve the frame rate the camera actually delivers, whatever the configured " +
                    "FPS says.");
            }

            if (status.ConfiguredFps > 0 && status.ConfiguredFps <= 30)
            {
                status.Warnings.Add(
                    "The stream is capped at " + status.ConfiguredFps + " fps, so every frame waits up " +
                    "to " + (1000 / status.ConfiguredFps) + " ms to be captured before anything else " +
                    "happens. A 50 or 60 fps capture mode with a matching FPS cap halves that wait.");
            }
        }

        private static bool IsOn(string value)
        {
            return string.Equals(value, "on", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsOff(string value)
        {
            return string.Equals(value, "off", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "no", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "false", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsNotAvailable(string value)
        {
            return string.Equals(value, NotAvailable, StringComparison.OrdinalIgnoreCase);
        }

        private static string Get(Dictionary<string, string> values, string key)
        {
            string value;
            return values.TryGetValue(key, out value) ? (value ?? "") : "";
        }

        private static string Optional(Dictionary<string, string> values, string key)
        {
            string value;
            return values.TryGetValue(key, out value) && !string.IsNullOrEmpty(value) ? value : NotAvailable;
        }

        private static string FirstOf(Dictionary<string, string> values, params string[] keys)
        {
            foreach (var key in keys)
            {
                var value = Get(values, key);
                if (!string.IsNullOrEmpty(value)) return value;
            }
            return "";
        }

        private static int GetInt(Dictionary<string, string> values, string key)
        {
            int parsed;
            return int.TryParse(Get(values, key), NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed)
                ? parsed
                : 0;
        }
    }
}
