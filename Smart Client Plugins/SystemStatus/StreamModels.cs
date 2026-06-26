using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace SystemStatus
{
    /// <summary>
    /// One video stream of one camera. Display strings are precomputed so the XAML needs no value
    /// converters. Live fields are updated in place on each refresh via <see cref="ApplyFrom"/> so
    /// the grids update without rebinding (preserves selection/scroll/sort).
    /// Identity across refreshes is <see cref="StreamId"/>.
    /// </summary>
    public sealed class StreamStatRow : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        public Guid StreamId { get; set; }
        public string CameraName { get; set; }
        public string RecorderName { get; set; }

        public string StreamName { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public string Codec { get; set; }          // VideoFormat, e.g. "H.264", "H.265", "JPEG"
        public double Fps { get; set; }            // actual frames/sec
        public double FpsRequested { get; set; }   // configured target frames/sec
        public ulong Bps { get; set; }             // bytes per second
        public ulong FrameSizeBytes { get; set; }  // average frame size
        public bool Recording { get; set; }
        public bool Live { get; set; }

        // ── Display-ready ────────────────────────────────────────────────────
        public string Resolution => Width > 0 && Height > 0 ? $"{Width}×{Height}" : "-";
        public string FpsText => Fps.ToString("0.0");
        public string FpsRequestedText => FpsRequested > 0 ? FpsRequested.ToString("0.0") : "-";

        // Recorder reports BPS as bytes/sec; shown as a byte rate that scales its unit (kB/s, MB/s...).
        // Same formatter as the per-camera aggregate (CameraHealthRow.Bitrate) so they stay in sync.
        public string BitrateText => ByteFormat.BitrateScaled(Bps);
        public string FrameSizeText => FrameSizeBytes > 0 ? (FrameSizeBytes / 1024.0).ToString("#,0.0") + " kB" : "-";

        public string RoleText
        {
            get
            {
                var parts = new List<string>(2);
                if (Recording) parts.Add("Rec");
                if (Live) parts.Add("Live");
                return parts.Count > 0 ? string.Join(" + ", parts) : "-";
            }
        }

        /// <summary>Copy live values from a freshly-fetched row and notify the UI.</summary>
        public void ApplyFrom(StreamStatRow f)
        {
            StreamName = f.StreamName;
            Width = f.Width; Height = f.Height;
            Codec = f.Codec;
            Fps = f.Fps; FpsRequested = f.FpsRequested;
            Bps = f.Bps; FrameSizeBytes = f.FrameSizeBytes;
            Recording = f.Recording; Live = f.Live;
            Raise(nameof(StreamName), nameof(Resolution), nameof(Codec), nameof(FpsText),
                  nameof(FpsRequestedText), nameof(BitrateText), nameof(FrameSizeText), nameof(RoleText));
        }

        private void Raise(params string[] names)
        {
            var h = PropertyChanged;
            if (h == null) return;
            foreach (var n in names) h(this, new PropertyChangedEventArgs(n));
        }
    }
}
