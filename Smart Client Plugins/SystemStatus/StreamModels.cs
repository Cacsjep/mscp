using System;
using System.Collections.Generic;
using System.Linq;

namespace SystemStatus
{
    /// <summary>
    /// One video stream of one camera, as reported by the recording server's
    /// RecorderStatusService2.GetVideoDeviceStatistics. A camera can have several
    /// streams (e.g. a high-res recording stream and a low-res live stream), so a
    /// single camera may produce multiple rows. All display strings are precomputed
    /// so the XAML needs no value converters (same pattern as CameraRow).
    /// </summary>
    public sealed class StreamStatRow
    {
        public string CameraName { get; set; }
        public string RecorderName { get; set; }
        public string StreamName { get; set; }

        public int Width { get; set; }
        public int Height { get; set; }
        public string Codec { get; set; }     // VideoFormat, e.g. "H.264", "H.265", "JPEG"

        public double Fps { get; set; }            // actual frames/sec
        public double FpsRequested { get; set; }   // configured target frames/sec
        public ulong Bps { get; set; }             // bytes per second
        public ulong FrameSizeBytes { get; set; }  // average frame size

        public bool Recording { get; set; }
        public bool Live { get; set; }

        // ── Display-ready ────────────────────────────────────────────────────
        public string Resolution => Width > 0 && Height > 0 ? $"{Width}×{Height}" : "—";
        public string FpsText => Fps.ToString("0.0");
        public string FpsRequestedText => FpsRequested > 0 ? FpsRequested.ToString("0.0") : "—";

        // Recorder statistics report BPS as bytes/sec; the Smart Client video-diagnostics
        // overlay shows kB/s, so we match that unit.
        public string BitrateText => (Bps / 1024.0).ToString("#,0") + " kB/s";
        public string FrameSizeText => FrameSizeBytes > 0 ? (FrameSizeBytes / 1024.0).ToString("#,0.0") + " kB" : "—";

        public string RoleText
        {
            get
            {
                var parts = new List<string>(2);
                if (Recording) parts.Add("Rec");
                if (Live) parts.Add("Live");
                return parts.Count > 0 ? string.Join(" + ", parts) : "—";
            }
        }
    }
}
