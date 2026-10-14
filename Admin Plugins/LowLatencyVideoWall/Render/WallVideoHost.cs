using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace LowLatencyVideoWall.Render
{
    /// <summary>
    /// A bare child HWND covering the whole wall, for the one swapchain to present into.
    ///
    /// One window for the entire wall, not one per pane. A swapchain per camera does not scale
    /// past a handful: every one of them is its own DWM composition surface and its own
    /// present queue, and 48 of them would spend more time in the compositor than in the
    /// decoder. The panes are sub-rectangles of this window's single backbuffer instead.
    ///
    /// This is also the fastest surface available in a WPF host: presents go straight to the
    /// window and never pass through WPF composition, so the present rate is ours to set
    /// rather than the compositor's. The cost is airspace - this window always draws on top of
    /// WPF content in the same window, so pane labels, borders and stats cannot be WPF
    /// elements. They are drawn with Direct2D onto the same backbuffer instead, by
    /// <see cref="PaneChrome"/>.
    ///
    /// The same airspace rule applies to input: WPF never sees a click that lands on this
    /// window, so the hover highlight and the double click to maximise come from the window
    /// messages handled below rather than from WPF events on the host.
    /// </summary>
    public sealed class WallVideoHost : HwndHost
    {
        private const int WsChild = 0x40000000;
        private const int WsVisible = 0x10000000;
        private const int WsClipSiblings = 0x04000000;
        private const int WsClipChildren = 0x02000000;

        /// <summary>
        /// Without this a static control answers WM_NCHITTEST with HTTRANSPARENT, so the mouse
        /// passes straight through it and no WM_MOUSEMOVE or WM_LBUTTONDOWN ever arrives.
        /// </summary>
        private const int SsNotify = 0x00000100;

        private const int WmMouseMove = 0x0200;
        private const int WmLButtonDown = 0x0201;
        private const int WmLButtonDblClk = 0x0203;
        private const int WmMouseLeave = 0x02A3;
        private const int TmeLeave = 0x00000002;

        /// <summary>
        /// How far apart two clicks may land and still be one double click. The stock window
        /// classes use the system drag thresholds for this; a wall is driven with a mouse on a
        /// desk and four pixels is enough to forgive a shaky hand.
        /// </summary>
        private const int DoubleClickSlopPixels = 4;

        private IntPtr _hwnd;
        private bool _trackingLeave;

        private int _lastClickTicks;
        private int _lastClickX;
        private int _lastClickY;

        /// <summary>Raised on the UI thread once the child window exists.</summary>
        public event Action<IntPtr> WindowCreated;

        /// <summary>Raised with the host size in physical pixels, which is what the swapchain wants.</summary>
        public event Action<int, int> PixelSizeChanged;

        /// <summary>
        /// The mouse moved, in client pixels. The window covers the whole wall and has no
        /// border, so these are already backbuffer pixels and need no DPI scaling.
        /// </summary>
        public event Action<int, int> MouseMovedPixel;

        /// <summary>The mouse left the wall, so whatever was hovered no longer is.</summary>
        public event Action MouseLeftHost;

        /// <summary>A double click, in the same client pixels as <see cref="MouseMovedPixel"/>.</summary>
        public event Action<int, int> DoubleClickedPixel;

        public IntPtr WindowHandle => _hwnd;

        protected override HandleRef BuildWindowCore(HandleRef hwndParent)
        {
            var size = GetPixelSize();

            // "static" is a stock window class, so there is nothing to register and no
            // WM_PAINT to service: every pixel comes from Present.
            _hwnd = CreateWindowEx(0, "static", null,
                WsChild | WsVisible | WsClipSiblings | WsClipChildren | SsNotify,
                0, 0, Math.Max(1, size.Width), Math.Max(1, size.Height),
                hwndParent.Handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);

            if (_hwnd == IntPtr.Zero)
                throw new InvalidOperationException(
                    $"CreateWindowEx failed with Win32 error {Marshal.GetLastWin32Error()}");

            WindowCreated?.Invoke(_hwnd);
            PixelSizeChanged?.Invoke(Math.Max(1, size.Width), Math.Max(1, size.Height));

            return new HandleRef(this, _hwnd);
        }

        protected override void DestroyWindowCore(HandleRef hwnd)
        {
            if (hwnd.Handle != IntPtr.Zero)
                DestroyWindow(hwnd.Handle);
            _hwnd = IntPtr.Zero;
            _trackingLeave = false;
        }

        /// <summary>
        /// HwndHost subclasses the window it created, so this is where the wall's mouse input
        /// arrives. Nothing is marked handled: these messages are only being observed, and a
        /// static control does nothing useful with them anyway.
        /// </summary>
        protected override IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam,
            ref bool handled)
        {
            switch (msg)
            {
                case WmMouseMove:
                    TrackMouseLeave(hwnd);
                    Raise(MouseMovedPixel, lParam);
                    break;

                case WmMouseLeave:
                    // The request is one shot, so it has to be made again on the next move.
                    _trackingLeave = false;
                    Invoke(MouseLeftHost);
                    break;

                case WmLButtonDblClk:
                    // The real path. See HandleButtonDown for why the manual pairing below
                    // is kept anyway.
                    _lastClickTicks = 0;
                    Raise(DoubleClickedPixel, lParam);
                    break;

                case WmLButtonDown:
                    HandleButtonDown(lParam);
                    break;
            }

            return base.WndProc(hwnd, msg, wParam, lParam, ref handled);
        }

        /// <summary>
        /// A fallback, not the main path. The "static" class turns out to be registered with
        /// CS_DBLCLKS already (its class style reads 0x4088), so Windows delivers the second
        /// click of a double click as WM_LBUTTONDBLCLK and this never sees a second
        /// WM_LBUTTONDOWN to pair with. Relying on the pairing alone made a double click work
        /// roughly once in a hundred: only when a stray earlier click happened to sit inside
        /// the double click time of the real one.
        ///
        /// It stays because the class style is not ours to guarantee, and pairing costs three
        /// fields. If CS_DBLCLKS is ever absent, this is what keeps maximising alive.
        /// </summary>
        private void HandleButtonDown(IntPtr lParam)
        {
            var x = LoWord(lParam);
            var y = HiWord(lParam);

            var now = Environment.TickCount;
            var isDouble = _lastClickTicks != 0
                           && unchecked(now - _lastClickTicks) <= GetDoubleClickTime()
                           && Math.Abs(x - _lastClickX) <= DoubleClickSlopPixels
                           && Math.Abs(y - _lastClickY) <= DoubleClickSlopPixels;

            if (isDouble)
            {
                // Cleared, so a third click starts a new pair rather than firing again.
                _lastClickTicks = 0;
                try { DoubleClickedPixel?.Invoke(x, y); }
                catch (Exception ex) { LogHandlerFailure(ex); }
                return;
            }

            _lastClickTicks = now == 0 ? 1 : now;
            _lastClickX = x;
            _lastClickY = y;
        }

        /// <summary>
        /// WM_MOUSELEAVE is only sent to a window that asked for it, and only once per
        /// request. Without it a pane the mouse left while the wall was busy keeps its
        /// highlight until the mouse comes back.
        /// </summary>
        private void TrackMouseLeave(IntPtr hwnd)
        {
            if (_trackingLeave || hwnd == IntPtr.Zero) return;

            var request = new TrackMouseEvent
            {
                Size = (uint)Marshal.SizeOf(typeof(TrackMouseEvent)),
                Flags = TmeLeave,
                TrackWindow = hwnd,
                HoverTime = 0
            };

            _trackingLeave = TrackMouseEventNative(ref request);
        }

        private void Raise(Action<int, int> handler, IntPtr lParam)
        {
            if (handler == null) return;
            try { handler(LoWord(lParam), HiWord(lParam)); }
            catch (Exception ex) { LogHandlerFailure(ex); }
        }

        private static void Invoke(Action handler)
        {
            if (handler == null) return;
            try { handler(); }
            catch (Exception ex) { LogHandlerFailure(ex); }
        }

        /// <summary>
        /// An exception escaping a handler here would travel out through the window procedure,
        /// which is fatal to the Smart Client rather than to the wall.
        /// </summary>
        private static void LogHandlerFailure(Exception ex)
            => LowLatencyVideoWallDefinition.Log.Error(
                $"Wall mouse handler failed: {ex.GetType().Name}: {ex.Message}");

        // Signed, because the coordinates go negative while a button is held outside the window.
        private static int LoWord(IntPtr value) => (short)(value.ToInt64() & 0xFFFF);

        private static int HiWord(IntPtr value) => (short)((value.ToInt64() >> 16) & 0xFFFF);

        protected override void OnRenderSizeChanged(SizeChangedInfo info)
        {
            base.OnRenderSizeChanged(info);

            var size = GetPixelSize();
            if (size.Width > 0 && size.Height > 0)
                PixelSizeChanged?.Invoke(size.Width, size.Height);
        }

        /// <summary>
        /// WPF measures in device independent units. The swapchain needs real pixels, so the
        /// DPI scale of whichever monitor the Smart Client is on has to be applied. On a wall
        /// this is load bearing twice over: it sizes the backbuffer, and it is what the pane
        /// rectangles and therefore the per camera stream resolutions are derived from. Getting
        /// it wrong at 150% DPI would ask every camera for two thirds of the resolution the
        /// panes actually show.
        /// </summary>
        private (int Width, int Height) GetPixelSize()
        {
            var scaleX = 1.0;
            var scaleY = 1.0;

            var source = PresentationSource.FromVisual(this);
            if (source?.CompositionTarget != null)
            {
                var transform = source.CompositionTarget.TransformToDevice;
                if (transform.M11 > 0) scaleX = transform.M11;
                if (transform.M22 > 0) scaleY = transform.M22;
            }

            return ((int)Math.Round(ActualWidth * scaleX), (int)Math.Round(ActualHeight * scaleY));
        }

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateWindowEx(
            int exStyle, string className, string windowName, int style,
            int x, int y, int width, int height,
            IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyWindow(IntPtr hwnd);

        [DllImport("user32.dll")]
        private static extern int GetDoubleClickTime();

        [DllImport("user32.dll", EntryPoint = "TrackMouseEvent", SetLastError = true)]
        private static extern bool TrackMouseEventNative(ref TrackMouseEvent request);

        [StructLayout(LayoutKind.Sequential)]
        private struct TrackMouseEvent
        {
            public uint Size;
            public uint Flags;
            public IntPtr TrackWindow;
            public uint HoverTime;
        }
    }
}
