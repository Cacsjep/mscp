using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using FFmpeg.AutoGen;
using LowLatencyVideoWall.Config;
using SharpDX;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using SharpDX.Mathematics.Interop;
using Device = SharpDX.Direct3D11.Device;

namespace LowLatencyVideoWall.Render
{
    /// <summary>
    /// Renders up to 48 cameras into one window.
    ///
    /// The proof of concept gave each camera its own device, swapchain and render thread. That
    /// is the right shape for one camera and the wrong shape for 48: every swapchain is its own
    /// DWM composition surface, every device is its own driver allocation, and every render
    /// thread competes for the device lock. So the wall inverts it:
    ///
    ///   one ID3D11Device         shared with every decoder, which is what makes decode to
    ///                            screen zero copy
    ///   one swapchain            on one child HWND covering the whole wall
    ///   one VideoProcessor per   source geometry, reused by every pane that decodes at that
    ///                            size, because the content description names the input size
    ///   N decode threads         one per camera, each with its own newest wins slot
    ///   one render thread        drains every slot, blits each pane into its own
    ///                            sub-rectangle of the single backbuffer, draws the chrome
    ///                            over it with Direct2D, presents once
    ///
    /// Two measured facts shape the loop and must not be undone:
    ///
    /// 1. Present is always called with sync interval 0. In a windowed flip model swapchain
    ///    that does not tear, because DWM still composites the most recently presented buffer
    ///    at its own vblank; only the explicit AllowTearing flag tears, and this renderer never
    ///    asks for it. Present(1) blocks inside DXGI until vblank while holding the D3D11
    ///    device lock, which starves every decode thread behind it: measured at 31.9 ms per
    ///    decode against 0.37 ms, and 51 ms of latency against 0.6 ms. With 48 decoders behind
    ///    that lock instead of one it would be far worse.
    ///
    /// 2. The flip model hands back an undefined backbuffer, so every present has to redraw
    ///    every pane, changed or not, plus the gaps between them. That is why the loop
    ///    coalesces: 48 cameras at 25 fps deliver 1200 frames a second between them, and
    ///    presenting on each arrival would mean 1200 full wall passes of 48 blits for no extra
    ///    frames on the glass. One pass per display refresh shows each of those frames just as
    ///    soon, because they arrived inside the same refresh interval.
    /// </summary>
    public sealed unsafe class WallRenderer : IDisposable
    {
        private static readonly RawColor4 Background = new RawColor4(0f, 0f, 0f, 1f);

        private readonly object _paneGate = new object();
        private readonly WallStats _wallStats = new WallStats();

        /// <summary>Every pane's slot signals this one event. See <see cref="FrameSlot"/>.</summary>
        private readonly ManualResetEventSlim _wake = new ManualResetEventSlim(false);

        private Device _device;
        private DeviceContext _context;
        private VideoDevice _videoDevice;
        private VideoContext _videoContext;

        private SwapChain1 _swapChain;
        private Texture2D _backBuffer;
        private RenderTargetView _backBufferRtv;
        private Output _containingOutput;
        private IntPtr _frameLatencyHandle = IntPtr.Zero;

        private readonly List<ProcessorEntry> _processors = new List<ProcessorEntry>();
        private readonly VideoProcessorStream[] _streams = new VideoProcessorStream[1];

        /// <summary>Title bars, borders, progress rings, error panels, diagnostics.</summary>
        private PaneChrome _chrome;

        // Render thread only.
        private readonly List<WallPane> _panes = new List<WallPane>();
        private readonly List<WallPane> _blitted = new List<WallPane>();
        private readonly List<Restart> _restarting = new List<Restart>();
        private bool _redraw;
        private int _layoutWidth, _layoutHeight;
        private long _adaptDueTicks;
        private int _showablePanes = -1;
        private bool _chromeAnimating;

        private volatile bool _chromeDirty;
        private volatile int _mouseX;
        private volatile int _mouseY;
        private volatile bool _mouseInside;

        private List<WallPane> _pendingPanes;
        private volatile WallPane[] _paneView = new WallPane[0];
        private volatile WallPane _maximised;
        private volatile bool _layoutDirty;

        private volatile IntPtr _pendingHwnd;
        private IntPtr _currentHwnd;
        private volatile int _targetWidth = 1;
        private volatile int _targetHeight = 1;
        private volatile int _swapWidth;
        private volatile int _swapHeight;
        private volatile int _processorCount;

        private Thread _thread;
        private CancellationTokenSource _cts;

        /// <summary>
        /// How often the wall is allowed to present. The single most useful tuning knob here:
        /// every present costs one clear plus one blit per visible pane, so the whole GPU cost
        /// of the wall scales with this and not with how many cameras are sending.
        ///
        /// 60 is a deliberate default. Presenting faster than the compositor composites is
        /// wasted work, and on the bench DWM delivered about 100 compositions a second even
        /// with the window on a 240 Hz panel.
        /// </summary>
        public int TargetPresentHz
        {
            get => _targetPresentHz;
            set
            {
                _targetPresentHz = Math.Max(1, Math.Min(240, value));
                _wallStats.ResetAverages();
            }
        }
        private volatile int _targetPresentHz = 60;

        /// <summary>
        /// Align each present to the output's vertical blank. Off by default: it is an
        /// IDXGIOutput wait so it holds no device lock, but it adds up to one refresh of
        /// latency to buy smoothness the wall does not need at 25 fps sources.
        /// </summary>
        public bool PaceOnVBlank { get; set; }

        /// <summary>
        /// Ask each camera for the resolution its pane can actually show. This is what keeps
        /// 48 panes affordable, so it defaults on; turning it off streams every camera at its
        /// own default resolution, which is useful for comparing.
        /// </summary>
        public bool AdaptStreamResolution { get; set; } = true;

        /// <summary>
        /// How long a pane rectangle has to hold still before a resolution change is allowed
        /// to restart its stream. Dragging the Smart Client between monitors resizes the wall
        /// continuously, and without this every camera would reconnect on the way.
        /// </summary>
        public int ResolutionSettleMs { get; set; } = 1500;

        public string AdapterDescription { get; private set; } = "";

        /// <summary>The device was removed or reset. Nothing recovers without a new renderer.</summary>
        public bool DeviceLost { get; private set; }

        public string LastRenderError { get; private set; }

        /// <summary>Whole Hz refresh rate of the output the swapchain is on, 0 when unknown.</summary>
        public int OutputRefreshHz { get; private set; }

        public WallRenderer()
        {
            // VideoSupport is required for ID3D11VideoDevice, which both the decoders and the
            // video processors need. BgraSupport keeps a BGRA swapchain happy.
            _device = new Device(DriverType.Hardware,
                DeviceCreationFlags.VideoSupport | DeviceCreationFlags.BgraSupport);

            // 48 decode threads and one render thread all touch this device. FFmpeg also turns
            // this on in d3d11va_device_init, but it has to be on before any decoding starts.
            using (var multithread = _device.QueryInterface<Multithread>())
                multithread.SetMultithreadProtected(true);

            _context = _device.ImmediateContext;
            _videoDevice = _device.QueryInterface<VideoDevice>();
            _videoContext = _context.QueryInterface<VideoContext>();

            using (var dxgiDevice = _device.QueryInterface<SharpDX.DXGI.Device>())
            using (var adapter = dxgiDevice.Adapter)
            {
                var description = adapter.Description;
                AdapterDescription = $"{description.Description.TrimEnd('\0')} " +
                                     $"({description.DedicatedVideoMemory / (1024 * 1024)} MB)";
            }

            // No device resources yet, so this is only the Direct2D and DirectWrite factories.
            // It takes the backbuffer whenever the swapchain is created or resized.
            _chrome = new PaneChrome();

            Log($"D3D11 device created on {AdapterDescription}");
        }

        private static void Log(string message) => LowLatencyVideoWallDefinition.Log.Info(message);

        private static void LogError(string message) => LowLatencyVideoWallDefinition.Log.Error(message);

        /// <summary>
        /// Returns an ID3D11Device pointer carrying one reference the caller takes ownership
        /// of. FFmpeg releases it when its hardware device context is freed, which is exactly
        /// what av_hwdevice_ctx_init expects. One of these goes to every decoder.
        /// </summary>
        internal IntPtr AcquireNativeDeviceRef()
        {
            var device = _device;
            if (device == null) return IntPtr.Zero;

            // QueryInterface AddRefs. Detaching the wrapper before disposing it stops SharpDX
            // releasing the reference we are handing over.
            var extra = device.QueryInterface<Device>();
            var pointer = extra.NativePointer;
            extra.NativePointer = IntPtr.Zero;
            extra.Dispose();
            return pointer;
        }

        /// <summary>
        /// Brings the GPU video engine and the FFmpeg natives up before any camera needs them.
        ///
        /// The first CreateVideoProcessor on a GPU that has idled down costs around two
        /// seconds. On a single view that was 2 s added to the time to first frame; on a wall
        /// it would be 2 s added to all 48 of them at once, since they are all waiting on the
        /// same driver call. Call this on a background thread as soon as the view loads.
        /// </summary>
        public void Warmup()
        {
            try
            {
                var started = Stopwatch.StartNew();

                var content = new VideoProcessorContentDescription
                {
                    InputFrameFormat = VideoFrameFormat.Progressive,
                    InputWidth = 1920,
                    InputHeight = 1080,
                    OutputWidth = 1920,
                    OutputHeight = 1080,
                    InputFrameRate = new Rational(60, 1),
                    OutputFrameRate = new Rational(60, 1),
                    Usage = VideoUsage.PlaybackNormal
                };

                _videoDevice.CreateVideoProcessorEnumerator(ref content, out var enumerator);
                _videoDevice.CreateVideoProcessor(enumerator, 0, out var processor);
                processor.Dispose();
                enumerator.Dispose();

                var engineMs = started.Elapsed.TotalMilliseconds;

                // Loading the FFmpeg natives is another one off cost that would otherwise land
                // on the first camera to connect.
                CameraVideoSource.SetupFfmpegPath();
                var configs = CameraVideoSource.DescribeHardwareConfigs();

                Log($"Video engine warmed up in {engineMs:0} ms, h264 hardware configs: {configs}");
            }
            catch (Exception ex)
            {
                Log($"Video engine warmup failed (not fatal): {ex.Message}");
            }
        }

        public void SetWindow(IntPtr hwnd)
        {
            _pendingHwnd = hwnd;
            _wake.Set();
        }

        /// <summary>The wall size in real pixels, which is what <see cref="WallVideoHost"/> reports.</summary>
        public void SetTargetSize(int width, int height)
        {
            _targetWidth = Math.Max(1, width);
            _targetHeight = Math.Max(1, height);
            _layoutDirty = true;
            _wake.Set();
        }

        /// <summary>
        /// Replaces the set of panes on the wall.
        ///
        /// Panes are matched by instance, which is how a caller says what to leave alone: a
        /// pane passed in that the renderer already owns keeps its RTSP session, its decoder
        /// and its counters, and only its rectangle is recomputed. A pane that disappears from
        /// the list has its views released on the render thread and its decoder stopped off it.
        /// Nothing here touches the device, so adding or removing cameras costs no more than
        /// the streams themselves.
        /// </summary>
        public void SetPanes(IReadOnlyList<WallPane> panes)
        {
            var copy = new List<WallPane>();
            if (panes != null)
                foreach (var pane in panes)
                    if (pane != null && !copy.Contains(pane)) copy.Add(pane);

            lock (_paneGate)
                _pendingPanes = copy;

            _paneView = copy.ToArray();
            _layoutDirty = true;
            _wake.Set();
        }

        /// <summary>
        /// Gives one pane the whole wall, or null to go back to the stored layout.
        ///
        /// Only the maximised pane restarts its stream, at the wall's resolution: the other
        /// panes keep streaming at their tile size, so coming back out of maximised does not
        /// reconnect 47 cameras.
        /// </summary>
        public void SetMaximisedPane(WallPane pane)
        {
            _maximised = pane;
            _layoutDirty = true;
            _wake.Set();
        }

        public WallPane MaximisedPane => _maximised;

        /// <summary>
        /// Where the mouse is, in backbuffer pixels, so the hovered pane can be highlighted.
        /// Called from the UI thread with what <see cref="WallVideoHost"/> reports: WPF never
        /// sees these clicks, because the wall's child window owns its own pixels.
        ///
        /// A present is only asked for when the pane under the pointer actually changes, which
        /// a mouse dragged across one pane does a few times rather than a few hundred.
        /// </summary>
        public void SetMousePosition(int x, int y)
        {
            var before = _mouseInside ? HitTest(_mouseX, _mouseY) : null;

            _mouseX = x;
            _mouseY = y;
            _mouseInside = true;

            if (ReferenceEquals(HitTest(x, y), before)) return;

            _chromeDirty = true;
            _wake.Set();
        }

        /// <summary>
        /// Asks for one more pass. The chrome reads <see cref="WallClientSettings"/> per pass,
        /// so a settings change only needs this to be seen: on a working wall the next frame
        /// would have forced a pass anyway, but a wall of still pictures presents nothing until
        /// something tells it to.
        /// </summary>
        public void Invalidate()
        {
            _chromeDirty = true;
            _wake.Set();
        }

        /// <summary>The pointer left the wall, so nothing is hovered.</summary>
        public void ClearMousePosition()
        {
            if (!_mouseInside) return;

            _mouseInside = false;
            _chromeDirty = true;
            _wake.Set();
        }

        /// <summary>
        /// The pane at a point in backbuffer pixels, or null. A maximised pane is the only one
        /// with a rectangle, so this answers for maximised walls without a special case.
        /// </summary>
        public WallPane HitTest(int x, int y)
        {
            foreach (var pane in _paneView)
            {
                var rect = pane.Rect;
                if (rect.IsEmpty) continue;
                if (x >= rect.Left && x < rect.Right && y >= rect.Top && y < rect.Bottom)
                    return pane;
            }
            return null;
        }

        /// <summary>Panes in wall order, as last given to <see cref="SetPanes"/>.</summary>
        public IReadOnlyList<WallPane> Panes => _paneView;

        public WallPane FindPane(Guid cameraId)
        {
            foreach (var pane in _paneView)
                if (pane.CameraId == cameraId) return pane;
            return null;
        }

        public void Start()
        {
            if (_thread != null) return;

            _cts = new CancellationTokenSource();
            _thread = new Thread(Loop)
            {
                Name = "LLVW-Wall-Render",

                // Above normal so the one render thread stays ahead of up to 48 decode
                // threads at the same priority.
                IsBackground = true,
                Priority = ThreadPriority.AboveNormal
            };
            _thread.Start();
        }

        /// <summary>
        /// Stops presenting. The cameras keep streaming, so this is cheap to undo: it is for
        /// a view item going off screen, not for a shutdown.
        /// </summary>
        public void Stop()
        {
            _cts?.Cancel();
            _wake.Set();

            var thread = _thread;
            _thread = null;
            if (thread != null && !thread.Join(5000))
                LogError("Wall render thread did not stop within 5 s");
        }

        /// <summary>
        /// A value snapshot of the whole wall, cheap enough for a UI timer. Call it from one
        /// thread on an interval: the rates are smoothed from the previous call.
        /// </summary>
        public WallStatsSnapshot ReadStats()
        {
            var panes = _paneView;

            var snapshot = new WallStatsSnapshot
            {
                AdapterDescription = AdapterDescription,
                DeviceLost = DeviceLost,
                LastError = LastRenderError,
                WallWidth = _swapWidth,
                WallHeight = _swapHeight,
                PaneCount = panes.Length,
                VideoProcessors = _processorCount,
                Panes = new PaneStatsSnapshot[panes.Length]
            };

            for (var i = 0; i < panes.Length; i++)
            {
                var pane = snapshot.Panes[i] = panes[i].ReadStats();
                if (pane.State == PaneState.Streaming) snapshot.PanesStreaming++;
                snapshot.TotalDecodeFps += pane.DecodeFps;
                snapshot.TotalMbitPerSecond += pane.MbitPerSecond;
            }

            _wallStats.Fill(ref snapshot);
            return snapshot;
        }

        // ----------------------------------------------------------------- render thread

        private void Loop()
        {
            var token = _cts.Token;

            // The first pass runs even with nothing decoded yet, so the window starts black
            // rather than showing whatever was in the buffer the compositor handed us.
            var dirty = true;
            var deadline = Stopwatch.GetTimestamp();

            try
            {
                while (!token.IsCancellationRequested)
                {
                    ApplyPendingPanes();
                    CompleteRestarts();

                    if (!EnsureSwapChain())
                    {
                        // No window yet, or the swapchain is broken. Back off rather than spin.
                        WaitForWork(50, token);
                        continue;
                    }

                    ApplyLayout();
                    StartAndAdaptPanes();

                    // Reset before draining, not after. A push that lands during the drain
                    // leaves the event set, so the next iteration runs instead of sleeping on
                    // a frame that has already arrived.
                    _wake.Reset();

                    var now = Stopwatch.GetTimestamp();
                    var showable = 0;

                    foreach (var pane in _panes)
                    {
                        if (pane.Suspended) continue;
                        if (pane.TakeNewestFrame()) dirty = true;

                        // The watchdog's clock, kept here rather than in the pass: a pane that
                        // has stopped sending produces no presents, so if this only ran per
                        // present it would never notice the stream was gone.
                        if (pane.State == PaneState.Streaming) pane.MarkStreaming(now);

                        if (CanShowPicture(pane, now)) showable++;
                    }

                    // A pane losing or regaining its picture changes what the wall looks like
                    // even though no frame arrived, so it has to force a pass.
                    if (showable != _showablePanes)
                    {
                        _showablePanes = showable;
                        dirty = true;
                    }

                    if (_chromeDirty)
                    {
                        _chromeDirty = false;
                        dirty = true;
                    }

                    if (_redraw)
                    {
                        _redraw = false;
                        dirty = true;
                    }

                    if (!dirty)
                    {
                        // Nothing changed, so what is on the glass is already correct: a flip
                        // model swapchain keeps showing the last presented buffer.
                        WaitForWork(100, token);
                        continue;
                    }

                    var remaining = MillisecondsUntil(deadline);
                    if (remaining > 0)
                    {
                        WaitForWork(remaining, token);
                        continue;
                    }

                    if (PaceOnVBlank) WaitForVBlank();

                    if (RenderAndPresent())
                    {
                        // A spinning progress ring is the one thing on the wall that changes
                        // without a frame arriving, so while one is on screen the next pass is
                        // already due.
                        dirty = _chromeAnimating;
                        deadline = Stopwatch.GetTimestamp() + PresentIntervalTicks();
                    }
                    else
                    {
                        _wallStats.AddPresentError();
                        if (DeviceLost) break;
                        WaitForWork(50, token);
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                LastRenderError = $"{ex.GetType().Name}: {ex.Message}";
                LogError($"Wall render loop stopped: {LastRenderError}");
            }
            finally
            {
                // Views over decoder surfaces have to go before the decoders do, and this is
                // the only thread allowed to touch them.
                foreach (var pane in _panes)
                    pane.ReleaseGpuResources();

                DisposeProcessors();
            }
        }

        private long PresentIntervalTicks() => Stopwatch.Frequency / Math.Max(1, _targetPresentHz);

        private static int MillisecondsUntil(long deadlineTicks)
        {
            var remaining = (deadlineTicks - Stopwatch.GetTimestamp()) * 1000 / Stopwatch.Frequency;
            if (remaining <= 0) return 0;
            return remaining > 100 ? 100 : (int)remaining;
        }

        private void WaitForWork(int milliseconds, CancellationToken token)
        {
            if (milliseconds <= 0) return;
            try { _wake.Wait(milliseconds, token); }
            catch (OperationCanceledException) { }
        }

        /// <summary>
        /// Blocks until the output's next vertical blank. An IDXGIOutput call, so unlike
        /// Present(1) it never takes the D3D11 device lock and the decode threads keep running.
        /// </summary>
        private void WaitForVBlank()
        {
            var output = _containingOutput;
            if (output == null)
            {
                Thread.Sleep(1);
                return;
            }

            try { output.WaitForVerticalBlank(); }
            catch (SharpDXException)
            {
                // Monitor topology changed under us. The output is picked up again on the
                // next swapchain rebuild.
                Thread.Sleep(1);
            }
        }

        private void ApplyPendingPanes()
        {
            List<WallPane> incoming;
            lock (_paneGate)
            {
                if (_pendingPanes == null) return;
                incoming = _pendingPanes;
                _pendingPanes = null;
            }

            foreach (var existing in _panes)
                if (!incoming.Contains(existing))
                    RetirePane(existing);

            _panes.Clear();
            foreach (var pane in incoming)
                _panes.Add(pane);

            _layoutDirty = true;
            _redraw = true;
            _adaptDueTicks = 0;

            Log($"Wall has {_panes.Count} pane(s)");
        }

        /// <summary>
        /// Takes a pane off the wall. The views come down here, on the render thread, because
        /// they are views over surfaces the decoder owns. The decoder itself is stopped on a
        /// pool thread: a join can wait on a socket, and the wall cannot stall for that.
        /// </summary>
        private void RetirePane(WallPane pane)
        {
            pane.ReleaseGpuResources();
            pane.RequestStopStream();

            ThreadPool.QueueUserWorkItem(_ =>
            {
                try { pane.Dispose(); }
                catch (Exception ex) { LogError($"Stopping {pane.DisplayName} failed: {ex.Message}"); }
            });
        }

        private void ApplyLayout()
        {
            if (!_layoutDirty && _layoutWidth == _swapWidth && _layoutHeight == _swapHeight)
                return;

            _layoutDirty = false;
            _layoutWidth = _swapWidth;
            _layoutHeight = _swapHeight;

            var maximised = _maximised == null ? -1 : _panes.IndexOf(_maximised);
            var rects = PaneLayout.Compute(_panes, _swapWidth, _swapHeight, maximised);
            var now = Stopwatch.GetTimestamp();

            for (var i = 0; i < _panes.Count; i++)
            {
                if (_panes[i].Rect.Equals(rects[i])) continue;
                _panes[i].Rect = rects[i];
                _panes[i].RectChangedTicks = now;
            }

            _redraw = true;

            // A pane that moved may want a different stream resolution, so do not wait out
            // the throttle before looking.
            _adaptDueTicks = 0;
        }

        /// <summary>
        /// Starts panes that have no session, and restarts the ones whose pane size has moved
        /// far enough to want a different stream resolution.
        /// </summary>
        private void StartAndAdaptPanes()
        {
            if (DeviceLost) return;

            var now = Stopwatch.GetTimestamp();

            // Throttled, because the loop itself runs as often as frames arrive and that is up
            // to 1200 times a second across 48 cameras. Nothing here needs to be reconsidered
            // more than a few times a second, and the layout forces it when it does change.
            if (now < _adaptDueTicks) return;
            _adaptDueTicks = now + Stopwatch.Frequency / 4;

            var settleTicks = (long)(Math.Max(0, ResolutionSettleMs) / 1000.0 * Stopwatch.Frequency);

            foreach (var pane in _panes)
            {
                if (pane.Suspended) continue;

                // The rectangle the stream resolution is chosen from. A pane hidden behind a
                // maximised one keeps its tile sized stream, which is why un-maximising does
                // not reconnect anything but the pane that was maximised.
                var target = pane.Rect.IsEmpty
                    ? PaneLayout.ToPixels(pane.Geometry, _swapWidth, _swapHeight)
                    : pane.Rect;

                if (target.IsEmpty) continue;

                var desired = AdaptStreamResolution
                    ? pane.DesiredResolution(target)
                    : StreamResolution.Empty;

                if (!pane.HasSession)
                {
                    pane.StartStream(desired, AcquireNativeDeviceRef, _wake);
                    continue;
                }

                if (!AdaptStreamResolution) continue;
                if (!StreamResolutionPicker.IsWorthRestarting(pane.StreamingResolution, desired))
                    continue;
                if (now - pane.RectChangedTicks < settleTicks) continue;

                Log($"{pane.DisplayName} pane is now {target.Width}x{target.Height}, " +
                    $"restarting the stream at {desired} (was {pane.StreamingResolution})");
                BeginRestart(pane, desired);
            }
        }

        private void BeginRestart(WallPane pane, StreamResolution target)
        {
            // Suspended panes are skipped everywhere in the loop, so from here until the
            // restart completes the render thread never looks at this pane's frames or views.
            pane.Suspended = true;
            pane.ReleaseGpuResources();
            pane.RequestStopStream();

            var entry = new Restart { Pane = pane, Target = target };
            _restarting.Add(entry);

            ThreadPool.QueueUserWorkItem(_ =>
            {
                try { pane.JoinStopStream(6000); }
                catch (Exception ex) { LogError($"Restarting {pane.DisplayName} failed: {ex.Message}"); }
                finally
                {
                    Interlocked.Exchange(ref entry.Finished, 1);
                    _wake.Set();
                }
            });
        }

        private void CompleteRestarts()
        {
            for (var i = _restarting.Count - 1; i >= 0; i--)
            {
                var entry = _restarting[i];
                if (Volatile.Read(ref entry.Finished) == 0) continue;

                _restarting.RemoveAt(i);

                var pane = entry.Pane;

                // Taken off the wall while it was stopping. RetirePane already queued its
                // disposal, so leave it alone.
                if (!_panes.Contains(pane)) continue;

                pane.Suspended = false;

                // The rectangle may have moved again while the old session was winding up, so
                // ask the picker rather than trusting the target we started with.
                var target = pane.Rect.IsEmpty ? entry.Target : pane.DesiredResolution(pane.Rect);
                if (target.IsEmpty) target = entry.Target;

                pane.StartStream(target, AcquireNativeDeviceRef, _wake);
            }
        }

        /// <summary>
        /// True while this pane's last frame is still worth putting on the glass. The watchdog
        /// in <see cref="WallPane"/> is what stops a dead camera showing a picture forever.
        /// </summary>
        private static bool CanShowPicture(WallPane pane, long nowTicks)
            => !pane.Suspended && pane.HasPicture && !pane.PictureIsStale(nowTicks);

        private bool RenderAndPresent()
        {
            var started = Stopwatch.GetTimestamp();
            _blitted.Clear();

            // Read once per pass, so every pane in one present agrees, and read every pass so
            // the operator's choice takes effect on the next present with nothing rebuilt.
            var letterbox = WallClientSettings.Scaling == PaneScaling.AspectRatio;
            var hovered = _mouseInside ? HitTest(_mouseX, _mouseY) : null;

            try
            {
                // Every present, not only when the layout changed: the flip model discards the
                // buffer, so without this the gaps between panes would show stale pixels and a
                // pane that stopped sending would show whatever was underneath it. It is also
                // what the letterbox bars of an aspect ratio fitted pane are made of.
                _context.ClearRenderTargetView(_backBufferRtv, Background);

                foreach (var pane in _panes)
                {
                    if (pane.Rect.IsEmpty || !CanShowPicture(pane, started)) continue;
                    if (BlitPane(pane, letterbox)) _blitted.Add(pane);
                }

                // After the video and before the present, on this thread, into this backbuffer.
                _chromeAnimating = _chrome != null && _chrome.Draw(_panes, hovered, started);
            }
            catch (SharpDXException ex)
            {
                LastRenderError = $"wall pass failed: {ex.Descriptor.ApiCode} {ex.Message}";
                LogError(LastRenderError);
                return false;
            }

            var blitted = Stopwatch.GetTimestamp();

            var result = _swapChain.TryPresent(0, PresentFlags.None);

            var presentTicks = Stopwatch.GetTimestamp() - blitted;

            if (result.Failure)
            {
                // DXGI_ERROR_DEVICE_REMOVED / _RESET cannot be recovered without rebuilding the
                // device, which also means rebuilding all 48 decoders. Surface it instead.
                if (result.Code == unchecked((int)0x887A0005) || result.Code == unchecked((int)0x887A0007))
                {
                    DeviceLost = true;
                    LastRenderError = "D3D11 device was removed or reset";
                }
                else
                {
                    LastRenderError = $"Present failed: 0x{result.Code:X8}";
                }
                LogError(LastRenderError);
                return false;
            }

            foreach (var pane in _blitted)
                pane.MarkBlitted();

            _wallStats.AddPass(blitted - started, presentTicks, _blitted.Count);
            return true;
        }

        private bool BlitPane(WallPane pane, bool letterbox)
        {
            var frame = pane.CurrentFrame;
            if (frame == null) return false;

            // Only hardware surfaces are drawn. A pane whose decoder fell back to software has
            // its frames in system memory, and converting and uploading 48 of those would cost
            // more than the wall has: the fallback is logged by the source instead.
            if (frame->format != (int)AVPixelFormat.AV_PIX_FMT_D3D11) return false;

            // data[0] is the decoder's NV12 texture array, data[1] the slice index into it.
            var pool = pane.EnsurePoolTexture((IntPtr)frame->data[0]);
            if (pool == null) return false;

            var entry = EnsureProcessor(frame->width, frame->height);
            if (entry == null) return false;

            var view = pane.EnsureInputView(_videoDevice, entry.Enumerator, (int)(long)frame->data[1]);
            if (view == null) return false;

            ApplyColorSpace(entry, frame);

            var rect = new RawRectangle(pane.Rect.Left, pane.Rect.Top, pane.Rect.Right, pane.Rect.Bottom);

            // Where the picture actually goes. Letterboxed it is smaller than the pane, and
            // the bars either side of it are the cleared backbuffer.
            var destination = letterbox
                ? FitToAspectRatio(pane.Rect, frame->width, frame->height)
                : rect;

            // The target rect is the pane, not the wall, and not the destination either. A blt
            // whose output target rect is larger than the stream's dest rect is free to fill
            // the remainder with the processor's background colour, which would wipe out every
            // pane already drawn this pass. Clipping the target to the pane makes that
            // impossible, and it is also what keeps a letterboxed picture from being allowed
            // to paint over its neighbours.
            _videoContext.VideoProcessorSetOutputTargetRect(entry.Processor, true, rect);
            _videoContext.VideoProcessorSetStreamDestRect(entry.Processor, 0, true, destination);

            _streams[0] = new VideoProcessorStream
            {
                Enable = true,
                OutputIndex = 0,
                InputFrameOrField = 0,
                PastFrames = 0,
                FutureFrames = 0,
                PInputSurface = view
            };

            _videoContext.VideoProcessorBlt(entry.Processor, entry.OutputView, 0, 1, _streams);
            return true;
        }

        /// <summary>
        /// The largest rectangle with the decoded frame's shape that fits in the pane, centred.
        ///
        /// Integer arithmetic throughout, for the same reason <see cref="PaneLayout"/> works in
        /// edges: the destination is pixels, and computing it in floats and letting the driver
        /// round would give the two bars either side different widths.
        /// </summary>
        private static RawRectangle FitToAspectRatio(PixelRect pane, int srcWidth, int srcHeight)
        {
            if (srcWidth <= 0 || srcHeight <= 0)
                return new RawRectangle(pane.Left, pane.Top, pane.Right, pane.Bottom);

            var width = pane.Width;
            var height = (int)((long)pane.Width * srcHeight / srcWidth);

            if (height > pane.Height)
            {
                height = pane.Height;
                width = (int)((long)pane.Height * srcWidth / srcHeight);
            }

            // A pane too small to hold a whole pixel of picture still gets one, because a
            // destination rectangle of zero width is an invalid blt rather than a blank pane.
            if (width < 1) width = 1;
            if (height < 1) height = 1;

            var left = pane.Left + (pane.Width - width) / 2;
            var top = pane.Top + (pane.Height - height) / 2;

            return new RawRectangle(left, top, left + width, top + height);
        }

        /// <summary>
        /// One video processor per source geometry, shared by every pane decoding at that size.
        /// The content description names the input size, so a 1280x720 pane and a 640x360 pane
        /// cannot share one, but the four or five distinct sizes a wall actually ends up with
        /// are nothing like 48 processors.
        /// </summary>
        private ProcessorEntry EnsureProcessor(int srcWidth, int srcHeight)
        {
            foreach (var existing in _processors)
                if (existing.SrcWidth == srcWidth && existing.SrcHeight == srcHeight)
                    return existing;

            if (srcWidth <= 0 || srcHeight <= 0 || _backBuffer == null) return null;

            var content = new VideoProcessorContentDescription
            {
                InputFrameFormat = VideoFrameFormat.Progressive,
                InputWidth = srcWidth,
                InputHeight = srcHeight,

                // The output is the whole wall for every processor, because they all write
                // into the one backbuffer. Which part of it they may touch is set per blt.
                OutputWidth = _swapWidth,
                OutputHeight = _swapHeight,
                InputFrameRate = new Rational(60, 1),
                OutputFrameRate = new Rational(60, 1),
                Usage = VideoUsage.PlaybackNormal
            };

            var entry = new ProcessorEntry { SrcWidth = srcWidth, SrcHeight = srcHeight };

            try
            {
                var started = Stopwatch.StartNew();

                _videoDevice.CreateVideoProcessorEnumerator(ref content, out entry.Enumerator);
                _videoDevice.CreateVideoProcessor(entry.Enumerator, 0, out entry.Processor);

                _videoDevice.CreateVideoProcessorOutputView(_backBuffer, entry.Enumerator,
                    new VideoProcessorOutputViewDescription
                    {
                        Dimension = VpovDimension.Texture2D,
                        Texture2D = new Texture2DVpov { MipSlice = 0 }
                    },
                    out entry.OutputView);

                _videoContext.VideoProcessorSetStreamFrameFormat(entry.Processor, 0,
                    VideoFrameFormat.Progressive);
                _videoContext.VideoProcessorSetStreamOutputRate(entry.Processor, 0,
                    VideoProcessorOutputRate.Normal, false, null);
                _videoContext.VideoProcessorSetStreamSourceRect(entry.Processor, 0, true,
                    new RawRectangle(0, 0, srcWidth, srcHeight));

                // Drivers turn on their own scaling and detail filters when auto processing is
                // left enabled. One pane can afford them; 48 blits a pass cannot, and nobody
                // asked for edge enhancement on surveillance video.
                _videoContext.VideoProcessorSetStreamAutoProcessingMode(entry.Processor, 0, false);

                _processors.Add(entry);
                _processorCount = _processors.Count;

                Log($"Video processor {srcWidth}x{srcHeight} to {_swapWidth}x{_swapHeight} " +
                    $"created in {started.Elapsed.TotalMilliseconds:0} ms " +
                    $"({_processors.Count} in use)");

                return entry;
            }
            catch (SharpDXException ex)
            {
                LastRenderError = $"CreateVideoProcessor {srcWidth}x{srcHeight} failed: {ex.Message}";
                LogError(LastRenderError);
                entry.Dispose();
                return null;
            }
        }

        /// <summary>
        /// Colour conversion is driven by what the stream declares rather than assumed, so
        /// limited range BT.709 off a camera does not come out washed out.
        ///
        /// It is a processor state, not a per blt argument, so it is only re-set when a pane
        /// with a different declaration shares this processor. In practice every camera on a
        /// wall declares the same thing and this costs nothing after the first frame.
        /// </summary>
        private void ApplyColorSpace(ProcessorEntry entry, AVFrame* frame)
        {
            var isBt709 = frame->colorspace != AVColorSpace.AVCOL_SPC_BT470BG
                       && frame->colorspace != AVColorSpace.AVCOL_SPC_SMPTE170M;
            var isFullRange = frame->color_range == AVColorRange.AVCOL_RANGE_JPEG;

            var key = (isBt709 ? 1 : 0) | (isFullRange ? 2 : 0);
            if (entry.ColorKey == key) return;
            entry.ColorKey = key;

            _videoContext.VideoProcessorSetStreamColorSpace(entry.Processor, 0,
                new VideoProcessorColorSpace
                {
                    Usage = false,                       // playback, not processing
                    RgbRange = false,
                    YCbCrMatrix = isBt709,
                    YCbCrXvYCC = false,
                    NominalRange = isFullRange ? 2 : 1   // 2 = 0-255, 1 = 16-235
                });

            _videoContext.VideoProcessorSetOutputColorSpace(entry.Processor,
                new VideoProcessorColorSpace
                {
                    Usage = false,
                    RgbRange = false,                    // full range RGB out
                    YCbCrMatrix = isBt709,
                    YCbCrXvYCC = false,
                    NominalRange = 2
                });
        }

        private bool EnsureSwapChain()
        {
            var hwnd = _pendingHwnd;

            if (hwnd == IntPtr.Zero)
            {
                // The host window went away. Presenting into a destroyed HWND fails, and
                // holding a swapchain on it keeps the window alive in DXGI's eyes.
                if (_swapChain != null) DestroySwapChain();
                return false;
            }

            if (DeviceLost) return false;

            var width = Math.Max(1, _targetWidth);
            var height = Math.Max(1, _targetHeight);

            if (_swapChain != null && hwnd == _currentHwnd &&
                width == _swapWidth && height == _swapHeight)
                return true;

            try
            {
                if (_swapChain != null && hwnd != _currentHwnd)
                    DestroySwapChain();

                if (_swapChain == null)
                    CreateSwapChain(hwnd, width, height);
                else
                    ResizeSwapChain(width, height);

                _redraw = true;
                return _swapChain != null;
            }
            catch (SharpDXException ex)
            {
                LastRenderError = $"swapchain setup failed: {ex.Message}";
                LogError(LastRenderError);
                DestroySwapChain();
                return false;
            }
        }

        private void CreateSwapChain(IntPtr hwnd, int width, int height)
        {
            using (var dxgiDevice = _device.QueryInterface<SharpDX.DXGI.Device>())
            using (var adapter = dxgiDevice.Adapter)
            using (var factory = adapter.GetParent<Factory2>())
            {
                // The flip model is what makes this worth doing: it is the low latency
                // presentation path and the only windowed one that reports useful frame
                // statistics. AllowTearing is deliberately not requested - the wall always
                // presents with sync interval 0, and without that flag a windowed flip
                // swapchain still cannot tear because DWM composites at its own vblank.
                var description = new SwapChainDescription1
                {
                    Width = width,
                    Height = height,
                    Format = Format.B8G8R8A8_UNorm,
                    Stereo = false,
                    SampleDescription = new SampleDescription(1, 0),
                    Usage = Usage.RenderTargetOutput,
                    BufferCount = 2,
                    Scaling = Scaling.Stretch,
                    SwapEffect = SwapEffect.FlipDiscard,
                    AlphaMode = AlphaMode.Ignore,
                    Flags = SharpDX.DXGI.SwapChainFlags.FrameLatencyWaitAbleObject
                };

                _swapChain = new SwapChain1(factory, _device, hwnd, ref description);

                // Stop DXGI hijacking Alt+Enter inside the Smart Client.
                factory.MakeWindowAssociation(hwnd, WindowAssociationFlags.IgnoreAll);
            }

            _currentHwnd = hwnd;
            _swapWidth = width;
            _swapHeight = height;

            SetUpFrameLatency();
            ReadOutputRefreshRate();
            AcquireBackBuffer();

            Log($"Wall swapchain created {width}x{height} flip-discard " +
                $"output={OutputRefreshHz} Hz, presenting at up to {_targetPresentHz} Hz");
        }

        private void ResizeSwapChain(int width, int height)
        {
            ReleaseBackBuffer();

            // Output views and the processors' content descriptions both name the output size,
            // so none of them survive a resize.
            DisposeProcessors();

            _swapChain.ResizeBuffers(2, width, height, Format.B8G8R8A8_UNorm,
                SharpDX.DXGI.SwapChainFlags.FrameLatencyWaitAbleObject);

            _swapWidth = width;
            _swapHeight = height;
            AcquireBackBuffer();

            _layoutDirty = true;
        }

        private void SetUpFrameLatency()
        {
            CloseFrameLatencyHandle();

            try
            {
                using (var swapChain2 = _swapChain.QueryInterface<SwapChain2>())
                {
                    // One frame in flight. Anything more just adds queued latency. The waitable
                    // object itself is not waited on: with sync interval 0 nothing ever queues,
                    // so it is permanently signalled and paces nothing. The loop's own deadline
                    // does the pacing instead.
                    swapChain2.MaximumFrameLatency = 1;
                    _frameLatencyHandle = swapChain2.FrameLatencyWaitableObject;
                }
            }
            catch (SharpDXException ex)
            {
                _frameLatencyHandle = IntPtr.Zero;
                Log($"Frame latency waitable object unavailable: {ex.Message}");
            }
        }

        private void CloseFrameLatencyHandle()
        {
            if (_frameLatencyHandle == IntPtr.Zero) return;
            // GetFrameLatencyWaitableObject hands back a handle the caller owns.
            CloseHandle(_frameLatencyHandle);
            _frameLatencyHandle = IntPtr.Zero;
        }

        private void ReadOutputRefreshRate()
        {
            _containingOutput?.Dispose();
            _containingOutput = null;

            try
            {
                // Kept alive: WaitForVerticalBlank on this output is how the optional paced
                // mode paces, and re-acquiring it would be a COM call per present.
                _containingOutput = _swapChain.ContainingOutput;

                var description = _containingOutput.Description;
                OutputRefreshHz = ReadRefreshHz(description.DeviceName);
            }
            catch (SharpDXException)
            {
                OutputRefreshHz = 0;
            }
        }

        private void AcquireBackBuffer()
        {
            _backBuffer = _swapChain.GetBackBuffer<Texture2D>(0);

            // The clear needs a render target view. The video processors write through their
            // own output views, which are created per processor against the same texture.
            _backBufferRtv = new RenderTargetView(_device, _backBuffer);

            // And the chrome through a Direct2D render target over the same surface.
            _chrome?.SetBackBuffer(_backBuffer);
        }

        private void ReleaseBackBuffer()
        {
            // First: the Direct2D render target holds a reference to the backbuffer surface,
            // and ResizeBuffers fails outright while any reference to it is outstanding.
            _chrome?.SetBackBuffer(null);

            _backBufferRtv?.Dispose();
            _backBufferRtv = null;
            _backBuffer?.Dispose();
            _backBuffer = null;
        }

        private void DisposeProcessors()
        {
            foreach (var entry in _processors)
                entry.Dispose();
            _processors.Clear();
            _processorCount = 0;
        }

        private void DestroySwapChain()
        {
            DisposeProcessors();
            ReleaseBackBuffer();
            CloseFrameLatencyHandle();

            _containingOutput?.Dispose();
            _containingOutput = null;

            _swapChain?.Dispose();
            _swapChain = null;
            _currentHwnd = IntPtr.Zero;
            _swapWidth = _swapHeight = 0;
            OutputRefreshHz = 0;
        }

        /// <summary>
        /// Stops every decoder, in parallel. Sequentially this would be 48 joins of up to four
        /// seconds each, which is three minutes of a view switch; each decoder's interrupt
        /// callback means they all actually return in milliseconds.
        /// </summary>
        private void StopAllPanes()
        {
            var panes = _paneView;
            if (panes.Length == 0) return;

            foreach (var pane in panes)
                pane.RequestStopStream();

            var remaining = new CountdownEvent(panes.Length);

            foreach (var pane in panes)
            {
                var captured = pane;
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    try { captured.Dispose(); }
                    catch (Exception ex)
                    {
                        LogError($"Stopping {captured.DisplayName} failed: {ex.Message}");
                    }
                    finally { remaining.Signal(); }
                });
            }

            if (remaining.Wait(10000))
                remaining.Dispose();
            else
                LogError("Not every decoder stopped within 10 s");
        }

        public void Dispose()
        {
            Stop();
            StopAllPanes();

            DisposeProcessors();
            DestroySwapChain();

            // After DestroySwapChain, which has already given back the backbuffer surface.
            _chrome?.Dispose();
            _chrome = null;

            _videoContext?.Dispose();
            _videoContext = null;
            _videoDevice?.Dispose();
            _videoDevice = null;

            // _context is the device's immediate context, owned by the device.
            _context = null;
            _device?.Dispose();
            _device = null;

            _wake.Dispose();
            _cts?.Dispose();
        }

        private sealed class ProcessorEntry : IDisposable
        {
            internal int SrcWidth;
            internal int SrcHeight;
            internal VideoProcessorEnumerator Enumerator;
            internal VideoProcessor Processor;
            internal VideoProcessorOutputView OutputView;

            /// <summary>Colour space currently programmed, -1 when nothing has been set yet.</summary>
            internal int ColorKey = -1;

            public void Dispose()
            {
                OutputView?.Dispose();
                OutputView = null;
                Processor?.Dispose();
                Processor = null;
                Enumerator?.Dispose();
                Enumerator = null;
            }
        }

        private sealed class Restart
        {
            internal WallPane Pane;
            internal StreamResolution Target;
            internal int Finished;
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr handle);

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern bool EnumDisplaySettings(string deviceName, int modeNum, ref DevMode devMode);

        private const int EnumCurrentSettings = -1;

        /// <summary>
        /// Whole Hz refresh rate for a display device. DwmGetCompositionTimingInfo is the
        /// precise source but it fails outright on some machines, and whole Hz is all this is
        /// used for: deciding whether the present rate the wall is configured for is sane.
        /// </summary>
        private static int ReadRefreshHz(string deviceName)
        {
            if (string.IsNullOrEmpty(deviceName)) return 0;

            var mode = new DevMode { dmSize = (short)System.Runtime.InteropServices.Marshal.SizeOf(typeof(DevMode)) };
            return EnumDisplaySettings(deviceName, EnumCurrentSettings, ref mode)
                ? mode.dmDisplayFrequency
                : 0;
        }

        [System.Runtime.InteropServices.StructLayout(
            System.Runtime.InteropServices.LayoutKind.Sequential,
            CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private struct DevMode
        {
            [System.Runtime.InteropServices.MarshalAs(
                System.Runtime.InteropServices.UnmanagedType.ByValTStr, SizeConst = 32)]
            public string dmDeviceName;
            public short dmSpecVersion;
            public short dmDriverVersion;
            public short dmSize;
            public short dmDriverExtra;
            public int dmFields;
            public int dmPositionX;
            public int dmPositionY;
            public int dmDisplayOrientation;
            public int dmDisplayFixedOutput;
            public short dmColor;
            public short dmDuplex;
            public short dmYResolution;
            public short dmTTOption;
            public short dmCollate;
            [System.Runtime.InteropServices.MarshalAs(
                System.Runtime.InteropServices.UnmanagedType.ByValTStr, SizeConst = 32)]
            public string dmFormName;
            public short dmLogPixels;
            public int dmBitsPerPel;
            public int dmPelsWidth;
            public int dmPelsHeight;
            public int dmDisplayFlags;
            public int dmDisplayFrequency;
            public int dmICMMethod;
            public int dmICMIntent;
            public int dmMediaType;
            public int dmDitherType;
            public int dmReserved1;
            public int dmReserved2;
            public int dmPanningWidth;
            public int dmPanningHeight;
        }
    }
}
