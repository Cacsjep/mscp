using System;
using System.Collections.Generic;
using System.Globalization;

namespace LowLatencyVideoWall.Render
{
    /// <summary>One resolution the camera says it can encode. ToString is what the URL wants.</summary>
    public readonly struct StreamResolution : IEquatable<StreamResolution>
    {
        public StreamResolution(int width, int height)
        {
            Width = width;
            Height = height;
        }

        public int Width { get; }
        public int Height { get; }

        public bool IsEmpty => Width <= 0 || Height <= 0;
        public long Pixels => (long)Width * Height;

        public static StreamResolution Empty => default(StreamResolution);

        public bool Equals(StreamResolution other) => Width == other.Width && Height == other.Height;
        public override bool Equals(object obj) => obj is StreamResolution other && Equals(other);
        public override int GetHashCode() => (Width * 397) ^ Height;

        /// <summary>The exact form <c>resolution=</c> takes in an Axis media.amp URL.</summary>
        public override string ToString()
            => IsEmpty ? "" : Width.ToString(CultureInfo.InvariantCulture) + "x" +
                              Height.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Picks the stream resolution a pane should ask the camera for.
    ///
    /// This is the single biggest lever on whether 48 panes are affordable. A 48 pane wall on
    /// a 3840x2160 display gives each pane roughly 640x360, and decoding 1080p to draw 640x360
    /// costs about nine times the pixels for nothing: the video processor would only throw
    /// them away on the way into the backbuffer. Asking the camera to encode what the pane can
    /// actually show moves that saving all the way back to the encoder, so it also cuts the
    /// bitrate on the wire and the decoder surface memory, not just the scaling.
    ///
    /// The rule is the smallest supported resolution that is at least the pane size on both
    /// axes, so a pane never shows an upscaled picture. If nothing on offer is big enough the
    /// largest one wins, which is the best the camera can do.
    /// </summary>
    public static class StreamResolutionPicker
    {
        /// <summary>
        /// Parses the comma separated list <c>CameraStatus.Resolution</c> carries, for example
        /// "1920x1080,1280x720,640x360". Garbage entries are dropped rather than thrown on:
        /// the list comes from a camera over VAPIX and a single odd token must not cost us the
        /// whole wall.
        /// </summary>
        public static List<StreamResolution> Parse(string supportedList)
        {
            var result = new List<StreamResolution>();
            if (string.IsNullOrWhiteSpace(supportedList)) return result;

            var seen = new HashSet<long>();

            foreach (var token in supportedList.Split(',', ';', ' '))
            {
                var text = token.Trim();
                if (text.Length == 0) continue;

                var split = text.IndexOfAny(new[] { 'x', 'X' });
                if (split <= 0 || split == text.Length - 1) continue;

                if (!int.TryParse(text.Substring(0, split), NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out var width)) continue;
                if (!int.TryParse(text.Substring(split + 1), NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out var height)) continue;

                if (width <= 0 || height <= 0) continue;

                // Cameras list the same mode more than once across capture modes.
                if (!seen.Add(((long)width << 32) | (uint)height)) continue;

                result.Add(new StreamResolution(width, height));
            }

            return result;
        }

        /// <summary>
        /// The resolution to put in the RTSP URL for a pane this many pixels across.
        /// Empty when there is nothing to choose from, in which case the caller leaves
        /// <c>resolution=</c> off the URL entirely and takes the camera's default.
        /// </summary>
        public static StreamResolution Pick(int paneWidth, int paneHeight, string supportedList)
            => Pick(paneWidth, paneHeight, Parse(supportedList));

        public static StreamResolution Pick(int paneWidth, int paneHeight,
            IReadOnlyList<StreamResolution> supported)
        {
            if (supported == null || supported.Count == 0) return StreamResolution.Empty;

            // No usable pane size yet (the host has not been laid out). Asking for the
            // largest stream now would mean restarting it a moment later, so say nothing and
            // let the caller wait for a real rectangle.
            if (paneWidth <= 0 || paneHeight <= 0) return StreamResolution.Empty;

            var best = StreamResolution.Empty;
            var largest = StreamResolution.Empty;

            foreach (var candidate in supported)
            {
                if (candidate.IsEmpty) continue;

                if (largest.IsEmpty || candidate.Pixels > largest.Pixels)
                    largest = candidate;

                if (candidate.Width < paneWidth || candidate.Height < paneHeight) continue;

                // Smallest by pixel count, and on a tie the narrower one: two modes with the
                // same area differ in aspect, and the narrower one wastes fewer pixels on a
                // pane that is already wider than it is tall.
                if (best.IsEmpty || candidate.Pixels < best.Pixels ||
                    (candidate.Pixels == best.Pixels && candidate.Width < best.Width))
                    best = candidate;
            }

            return best.IsEmpty ? largest : best;
        }

        /// <summary>
        /// Whether a pane that is currently streaming <paramref name="current"/> should be
        /// restarted to get <paramref name="desired"/>.
        ///
        /// Restarting costs a reconnect, so this is deliberately conservative: it only says
        /// yes when the choice actually moved to a different mode. Resizing the wall by a few
        /// pixels does not change the pick, so it does not restart anything.
        /// </summary>
        public static bool IsWorthRestarting(StreamResolution current, StreamResolution desired)
        {
            if (desired.IsEmpty) return false;
            if (current.IsEmpty) return true;
            return !current.Equals(desired);
        }
    }
}
