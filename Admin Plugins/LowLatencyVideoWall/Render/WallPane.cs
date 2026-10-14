using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using FFmpeg.AutoGen;
using LowLatencyVideoWall.Model;
using SharpDX.Direct3D11;

namespace LowLatencyVideoWall.Render
{
    /// <summary>
    /// One camera on the wall: where it goes, the decoder feeding it, the slot the decoder
    /// hands frames through, and its own counters.
    ///
    /// Everything that is per camera lives here rather than in the renderer, and the input
    /// views are the reason it has to. Each decoder allocates its own NV12 texture array, so a
    /// VideoProcessorInputView is only valid for the pane whose decoder owns that array: there
    /// is no wall-wide view cache to be had. Creating a view per frame is a measurable cost, so
    /// each pane caches its own, one per array slice, and throws them away when the decoder
    /// reallocates its pool or when the video processor behind them is replaced.
    ///
    /// Thread ownership:
    ///   decode thread  - pushes into Slot, writes Stats and the session info
    ///   render thread  - everything to do with the current frame, the pool texture, the views
    ///   UI thread      - ReadStats, and reading Rect and State for display
    /// </summary>
    public sealed unsafe class WallPane : IDisposable
    {
        /// <summary>
        /// How long a pane may go on showing its last frame once it has stopped streaming.
        ///
        /// A wall full of frozen pictures is the worst failure this plugin can produce: it
        /// looks exactly like a working wall, so nobody notices the camera is gone. 1500 ms is
        /// long enough to ride out a late frame from a 5 fps stream or a GOP boundary, and
        /// short enough that an operator watching the wall sees the pane go black and spin
        /// while the reconnect is still in its first attempt.
        /// </summary>
        private const int StalePictureMs = 1500;

        /// <summary>
        /// The RTSP path. Nothing about the encoding is overridden here: the stream is taken
        /// as the camera is configured, and what a pane asks for is only its resolution.
        /// </summary>
        private const string MediaPath = "/axis-media/media.amp";

        private static readonly long StalePictureTicks =
            (long)(StalePictureMs / 1000.0 * Stopwatch.Frequency);

        private readonly PaneStats _stats = new PaneStats();
        private readonly List<StreamResolution> _supported;
        private readonly string _password;
        private readonly string _transport;
        private readonly Action<string> _log;

        /// <summary>
        /// Serialises stopping the session. Two things can ask for it at once: the renderer
        /// restarting the pane at a new resolution, and the renderer retiring the pane because
        /// it left the wall. Whichever gets here second finds the session already gone.
        /// </summary>
        private readonly object _lifetimeGate = new object();

        private FrameSlot _slot;
        private CameraVideoSource _source;
        private volatile bool _disposed;

        // Render thread only, all of it.
        private DecodedFrame _current;
        private bool _currentIsNew;
        private long _lastStreamingTicks;
        private long _lastFrameTicks;
        private IntPtr _poolTexturePtr;
        private Texture2D _poolTexture;
        private VideoProcessorInputView[] _inputViews;
        private VideoProcessorEnumerator _viewEnumerator;

        public WallPane(WallCamera camera, string password, string supportedResolutions,
            PaneDefinition geometry, string transport = "tcp")
        {
            Camera = camera ?? throw new ArgumentNullException(nameof(camera));
            Geometry = geometry ?? new PaneDefinition { CameraId = camera.CameraId };
            _password = password ?? "";
            _transport = string.IsNullOrWhiteSpace(transport) ? "tcp" : transport;
            _supported = StreamResolutionPicker.Parse(supportedResolutions);

            _log = message => LowLatencyVideoWallDefinition.Log.Info($"[{DisplayName}] {message}");
        }

        public WallCamera Camera { get; }

        /// <summary>Normalised 0 to 1000 placement. The renderer converts it per wall size.</summary>
        public PaneDefinition Geometry { get; }

        public Guid CameraId => Camera.CameraId;

        public string DisplayName => string.IsNullOrEmpty(Camera.Name)
            ? Camera.Host
            : Camera.Name;

        /// <summary>
        /// Where this pane sits on the wall, in backbuffer pixels. Written by the render
        /// thread when the layout changes and read by the UI for display only.
        /// </summary>
        public PixelRect Rect { get; internal set; }

        /// <summary>What the running stream was asked for. Empty means the camera's default.</summary>
        public StreamResolution StreamingResolution { get; private set; }

        public PaneState State => _source == null ? PaneState.Idle : _source.State;

        public bool IsStreaming => State == PaneState.Streaming;

        /// <summary>True while a session exists, whether or not it is currently connected.</summary>
        internal bool HasSession => _source != null;

        /// <summary>
        /// Set by the renderer while this pane's session is being torn down and replaced. A
        /// suspended pane is skipped everywhere in the render loop, which is what makes it safe
        /// for the decoder to be freeing its surface pool on another thread.
        /// </summary>
        internal bool Suspended { get; set; }

        public string LastError => _source?.LastError;

        /// <summary>
        /// The URL of the running session with its credentials masked, for the pane overlay to
        /// show. Built when the session starts rather than on demand, because the overlay would
        /// otherwise rebuild and escape it per pane per present.
        /// </summary>
        public string DisplayUrl { get; private set; } = "";

        /// <summary>The resolution list this pane can choose from, as the camera reported it.</summary>
        public IReadOnlyList<StreamResolution> SupportedResolutions => _supported;

        /// <summary>
        /// Stopwatch timestamp of the last time <see cref="Rect"/> changed. The renderer waits
        /// for this to settle before it restarts a stream at a new resolution, so dragging a
        /// wall across a monitor boundary does not reconnect 48 cameras on the way.
        /// </summary>
        internal long RectChangedTicks { get; set; }

        internal FrameSlot Slot => _slot;

        internal PaneStats Stats => _stats;

        /// <summary>The frame the render thread is currently drawing, or null.</summary>
        internal AVFrame* CurrentFrame => _current.Frame;

        /// <summary>True once this pane has a picture to repeat on every present.</summary>
        internal bool HasPicture => _current.Frame != null;

        public PaneStatsSnapshot ReadStats() => Decorate(_stats.Read());

        /// <summary>
        /// The same snapshot for the pane overlay, which samples from the render thread. It has
        /// its own rate meters, so the two readers cannot spoil each other's smoothing.
        /// </summary>
        internal PaneStatsSnapshot ReadOverlayStats() => Decorate(_stats.ReadOverlay());

        private PaneStatsSnapshot Decorate(PaneStatsSnapshot snapshot)
        {
            snapshot.CameraId = CameraId;
            snapshot.CameraName = DisplayName;
            snapshot.State = State;
            snapshot.Error = _source?.LastError;
            snapshot.Rect = Rect;
            snapshot.RequestedResolution = StreamingResolution.ToString();

            var info = _source?.Info;
            if (info != null)
            {
                snapshot.SourceWidth = info.Width;
                snapshot.SourceHeight = info.Height;
                snapshot.IsHardwareDecoded = info.IsHardware;
                snapshot.DecoderName = info.DecoderName;
                snapshot.Codec = info.Codec;
            }

            snapshot.TimeToFirstFrameMs = _source?.Timing.FirstBlitMs ?? 0;
            return snapshot;
        }

        /// <summary>
        /// The resolution this pane should be asking the camera for at its current size.
        /// Empty when the camera published no list, or the pane has no size yet.
        /// </summary>
        public StreamResolution DesiredResolution()
            => DesiredResolution(Rect);

        /// <summary>
        /// The resolution for a given rectangle rather than the current one. The renderer uses
        /// this for a pane hidden behind a maximised one, which has no rectangle on the wall
        /// but should still stream at the size it will come back to.
        /// </summary>
        internal StreamResolution DesiredResolution(PixelRect rect)
            => StreamResolutionPicker.Pick(rect.Width, rect.Height, _supported);

        /// <summary>
        /// The URL for one stream. Credentials are escaped because a Milestone camera password
        /// is allowed to contain the characters that would otherwise end the user info part.
        /// </summary>
        internal string BuildUrl(StreamResolution resolution)
        {
            var host = Camera.Host;
            var credentials = "";

            if (!string.IsNullOrEmpty(Camera.UserName))
                credentials = Uri.EscapeDataString(Camera.UserName) + ":" +
                              Uri.EscapeDataString(_password) + "@";

            var query = new List<string>();

            if (!resolution.IsEmpty)
                query.Add("resolution=" + resolution);

            // media.amp numbers channels from one, the Milestone configuration from zero.
            if (Camera.Channel > 0)
                query.Add("camera=" + (Camera.Channel + 1));

            var url = "rtsp://" + credentials + host + MediaPath;
            return query.Count == 0 ? url : url + "?" + string.Join("&", query);
        }

        /// <summary>
        /// Opens a session at the given resolution. Called on the render thread, which is
        /// cheap: all this does is spawn the decode thread.
        /// </summary>
        internal void StartStream(StreamResolution resolution, Func<IntPtr> acquireNativeDevice,
            ManualResetEventSlim wallWake)
        {
            if (_source != null || _disposed) return;

            if (_slot == null)
                _slot = new FrameSlot(_stats, wallWake);

            StreamingResolution = resolution;

            var url = BuildUrl(resolution);
            DisplayUrl = CameraVideoSource.MaskUrl(url);

            // The jitter offset is derived from the camera id, so it is stable across restarts
            // and two cameras keep the same relative offset for the life of the wall.
            var jitter = (CameraId.GetHashCode() & 0x7FFFFFFF) % 1500;

            _source = new CameraVideoSource(url, _transport, acquireNativeDevice,
                _slot, _stats, _log, jitter);

            _log($"Starting {CameraVideoSource.MaskUrl(url)} " +
                 $"({(resolution.IsEmpty ? "camera default resolution" : resolution.ToString())})");
            _source.Start("LLVW-Decode-" + Shorten(DisplayName));
        }

        /// <summary>
        /// Tells the decoder to wind up without waiting for it. Safe to call from the render
        /// thread: the session's interrupt callback unblocks it, so nothing here blocks on a
        /// socket timeout.
        /// </summary>
        internal void RequestStopStream() => _source?.RequestStop();

        /// <summary>
        /// Waits for the decode thread and drops the session. Must not be called before
        /// <see cref="ReleaseGpuResources"/>: the input views are views over surfaces the
        /// decoder owns, so they have to be gone before the decoder frees its pool.
        /// </summary>
        internal void JoinStopStream(int timeoutMs)
        {
            CameraVideoSource source;
            lock (_lifetimeGate)
            {
                source = _source;
                if (source == null) return;
                _source = null;
            }

            source.RequestStop();
            source.Join(timeoutMs);
            source.Dispose();
            StreamingResolution = StreamResolution.Empty;
        }

        /// <summary>
        /// Notes that this pane is alive. Called once per pass from the render thread with the
        /// pass timestamp, which is cheaper than a clock read per pane.
        /// </summary>
        internal void MarkStreaming(long nowTicks) => _lastStreamingTicks = nowTicks;

        /// <summary>
        /// True when the frame this pane is holding is no longer worth showing: the stream has
        /// been down for longer than <see cref="StalePictureMs"/>, so the picture on screen is
        /// a lie. The renderer stops blitting it and the chrome draws over the black instead.
        /// </summary>
        /// <remarks>
        /// Both halves are needed. The state alone misses a camera that holds its RTSP session
        /// open and stops sending, which is how a PoE switch under load fails; the frame clock
        /// alone would call a pane stale during the moment a reconnect is already fixing.
        /// </remarks>
        internal bool PictureIsStale(long nowTicks)
            => _lastStreamingTicks == 0 || _lastFrameTicks == 0
               || nowTicks - _lastStreamingTicks > StalePictureTicks
               || nowTicks - _lastFrameTicks > StalePictureTicks;

        /// <summary>Takes the newest decoded frame, if there is one. Render thread.</summary>
        internal bool TakeNewestFrame()
        {
            if (_slot == null) return false;
            if (!_slot.TryTake(out var incoming)) return false;

            ReleaseCurrentFrame();
            _current = incoming;
            _currentIsNew = true;
            _lastFrameTicks = Stopwatch.GetTimestamp();
            return true;
        }

        /// <summary>
        /// Books a successful blit. Only a blit that carried a frame this pane had not shown
        /// before counts as a displayed frame or contributes to latency: on a wall most blits
        /// are repeats, because every present has to redraw every pane whether or not that
        /// pane changed.
        /// </summary>
        internal void MarkBlitted()
        {
            if (!_currentIsNew) return;
            _currentIsNew = false;

            _stats.AddUniqueBlit();
            _stats.AddLatency(Stopwatch.GetTimestamp() - _current.ArrivalTicks);

            var timing = _source?.Timing;
            if (timing != null && timing.FirstBlitMs == 0 && timing.SessionStartTicks != 0)
            {
                timing.FirstBlitMs = (Stopwatch.GetTimestamp() - timing.SessionStartTicks) /
                                     (Stopwatch.Frequency / 1000.0);
                _log($"First frame on screen after {timing.FirstBlitMs:0} ms");
            }
        }

        /// <summary>
        /// Wraps the decoder's surface pool, once. The pool is reallocated if the stream
        /// changes format, which shows up here as a different texture pointer.
        /// Returns null when there is nothing to wrap.
        /// </summary>
        internal Texture2D EnsurePoolTexture(IntPtr texturePtr)
        {
            if (texturePtr == _poolTexturePtr && _poolTexture != null) return _poolTexture;

            DisposeInputViews();
            _poolTexture?.Dispose();
            _poolTexture = null;
            _poolTexturePtr = IntPtr.Zero;

            if (texturePtr == IntPtr.Zero) return null;

            // The decoder owns this texture. QueryInterface gives us our own reference, and
            // detaching the temporary wrapper stops it releasing the decoder's.
            var borrowed = new Texture2D(texturePtr);
            try
            {
                _poolTexture = borrowed.QueryInterface<Texture2D>();
                _poolTexturePtr = texturePtr;
            }
            finally
            {
                borrowed.NativePointer = IntPtr.Zero;
                borrowed.Dispose();
            }

            return _poolTexture;
        }

        /// <summary>
        /// The cached input view for one array slice of this pane's decoder pool.
        ///
        /// Views belong to the enumerator they were created against, so when the pane lands on
        /// a different video processor (its source geometry changed, or the wall was resized)
        /// the whole cache has to go.
        /// </summary>
        internal VideoProcessorInputView EnsureInputView(VideoDevice videoDevice,
            VideoProcessorEnumerator enumerator, int arraySlice)
        {
            if (arraySlice < 0 || _poolTexture == null || enumerator == null) return null;

            if (!ReferenceEquals(_viewEnumerator, enumerator))
            {
                DisposeInputViews();
                _viewEnumerator = enumerator;
            }

            if (_inputViews == null || arraySlice >= _inputViews.Length)
                Array.Resize(ref _inputViews, arraySlice + 8);

            if (_inputViews[arraySlice] == null)
            {
                var description = new VideoProcessorInputViewDescription
                {
                    FourCC = 0,
                    Dimension = VpivDimension.Texture2D,
                    Texture2D = new Texture2DVpiv { MipSlice = 0, ArraySlice = arraySlice }
                };

                videoDevice.CreateVideoProcessorInputView(_poolTexture, enumerator, description,
                    out var view);
                _inputViews[arraySlice] = view;
            }

            return _inputViews[arraySlice];
        }

        /// <summary>
        /// Drops everything that is a view over the decoder's surfaces, plus the frame in
        /// flight. Render thread, and before the decoder is stopped.
        /// </summary>
        internal void ReleaseGpuResources()
        {
            DisposeInputViews();
            _viewEnumerator = null;
            _poolTexture?.Dispose();
            _poolTexture = null;
            _poolTexturePtr = IntPtr.Zero;
            ReleaseCurrentFrame();

            // A pane coming back from a restart must not count as fresh until it has actually
            // decoded something, or the chrome would stop spinning over a black pane.
            _lastStreamingTicks = 0;
            _lastFrameTicks = 0;
        }

        internal void ReleaseCurrentFrame()
        {
            if (_current.Frame != null)
            {
                var frame = _current.Frame;
                ffmpeg.av_frame_free(&frame);
            }

            _current = default(DecodedFrame);
            _currentIsNew = false;
        }

        private void DisposeInputViews()
        {
            if (_inputViews == null) return;
            for (var i = 0; i < _inputViews.Length; i++)
            {
                _inputViews[i]?.Dispose();
                _inputViews[i] = null;
            }
        }

        /// <summary>Thread names are read in a debugger, so keep them short and legal.</summary>
        private static string Shorten(string name)
        {
            if (string.IsNullOrEmpty(name)) return "camera";
            return name.Length <= 24 ? name : name.Substring(0, 24);
        }

        /// <summary>
        /// Stops the session and releases everything.
        ///
        /// Must not run while a renderer still has this pane: the renderer owns the GPU side
        /// from its own thread, and it drops a pane by taking it out of the wall first. Safe
        /// to call twice, and safe on a pane that was built but never handed to a renderer.
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            // ReleaseGpuResources is normally the renderer's last act on the render thread.
            // Doing it again is harmless, and it is the only path for a pane that never got
            // as far as being rendered.
            ReleaseGpuResources();
            JoinStopStream(4000);

            _slot?.Dispose();
            _slot = null;
        }
    }
}
