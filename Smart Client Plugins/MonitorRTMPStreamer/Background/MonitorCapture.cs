using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using SharpDX;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using Device = SharpDX.Direct3D11.Device;
using MapFlags = SharpDX.Direct3D11.MapFlags;
using Resource = SharpDX.DXGI.Resource;

namespace MonitorRTMPStreamer.Background
{
    /// <summary>
    /// Fast screen capture using DXGI Desktop Duplication with cached resources.
    /// Falls back to GDI CopyFromScreen when DXGI is unavailable (e.g. RDP).
    /// </summary>
    public sealed class MonitorCapture : IDisposable
    {
        // DXGI_ERROR_ACCESS_LOST
        private const int ACCESS_LOST = unchecked((int)0x887A0026);

        private struct OutputSlot
        {
            public Device Device;
            public OutputDuplication Duplication;
            public Texture2D StagingTexture;
            public int Width;
            public int Height;
            public int OffsetX;
            public bool HasFrame; // staging texture has valid data
        }

        private OutputSlot[] _slots;
        private int _totalWidth;
        private int _totalHeight;
        private bool _dxgiReady;
        private Screen[] _screens;
        private Action<string> _log;

        // Reusable bitmap to avoid GC pressure at high FPS
        private Bitmap _reusableBmp;

        public string CaptureMethod { get; private set; } = "";

        /// <summary>
        /// Initialise (or re-initialise) for the given set of screens.
        /// Returns true when DXGI is available.
        /// </summary>
        public bool Init(Screen[] screens, Action<string> log)
        {
            Cleanup();
            _log = log;
            _screens = screens.OrderBy(s => s.Bounds.X).ThenBy(s => s.Bounds.Y).ToArray();
            _totalWidth = _screens.Sum(s => s.Bounds.Width);
            _totalHeight = _screens.Max(s => s.Bounds.Height);

            try
            {
                InitDxgi(log);
                _dxgiReady = true;
                CaptureMethod = "DXGI (GPU)";
                log?.Invoke("DXGI capture initialised OK");
            }
            catch (Exception ex)
            {
                log?.Invoke($"DXGI init failed, using GDI fallback: {ex.Message}");
                CleanupDxgi();
                _dxgiReady = false;
                CaptureMethod = "GDI (CPU)";
            }

            return _dxgiReady;
        }

        private void InitDxgi(Action<string> log)
        {
            _slots = new OutputSlot[_screens.Length];

            using (var factory = new Factory1())
            {
                for (int i = 0; i < _screens.Length; i++)
                {
                    var screen = _screens[i];
                    bool found = false;

                    for (int ai = 0; !found; ai++)
                    {
                        Adapter1 adapter;
                        try { adapter = factory.GetAdapter1(ai); }
                        catch (SharpDXException) { break; }

                        using (adapter)
                        {
                            for (int oi = 0; !found; oi++)
                            {
                                Output output;
                                try { output = adapter.GetOutput(oi); }
                                catch (SharpDXException) { break; }

                                using (output)
                                {
                                    var db = output.Description.DesktopBounds;
                                    if (db.Left != screen.Bounds.X || db.Top != screen.Bounds.Y ||
                                        (db.Right - db.Left) != screen.Bounds.Width ||
                                        (db.Bottom - db.Top) != screen.Bounds.Height)
                                        continue;

                                    // Create device + duplication inside try so leaks are impossible
                                    Device device = null;
                                    Output1 output1 = null;
                                    OutputDuplication dup = null;
                                    Texture2D staging = null;
                                    try
                                    {
                                        device = new Device(adapter);
                                        output1 = output.QueryInterface<Output1>();
                                        dup = output1.DuplicateOutput(device);

                                        var stagingDesc = new Texture2DDescription
                                        {
                                            Width = screen.Bounds.Width,
                                            Height = screen.Bounds.Height,
                                            MipLevels = 1,
                                            ArraySize = 1,
                                            Format = Format.B8G8R8A8_UNorm,
                                            SampleDescription = new SampleDescription(1, 0),
                                            Usage = ResourceUsage.Staging,
                                            CpuAccessFlags = CpuAccessFlags.Read,
                                            BindFlags = BindFlags.None,
                                            OptionFlags = ResourceOptionFlags.None,
                                        };
                                        staging = new Texture2D(device, stagingDesc);

                                        var offsetX = 0;
                                        for (int j = 0; j < i; j++)
                                            offsetX += _screens[j].Bounds.Width;

                                        _slots[i] = new OutputSlot
                                        {
                                            Device = device,
                                            Duplication = dup,
                                            StagingTexture = staging,
                                            Width = screen.Bounds.Width,
                                            Height = screen.Bounds.Height,
                                            OffsetX = offsetX,
                                            HasFrame = false,
                                        };

                                        found = true;
                                        log?.Invoke($"DXGI: mapped {screen.DeviceName} ({screen.Bounds.Width}x{screen.Bounds.Height}) adapter={ai} output={oi}");
                                    }
                                    catch
                                    {
                                        // Clean up partially created resources
                                        staging?.Dispose();
                                        dup?.Dispose();
                                        device?.Dispose();
                                        throw;
                                    }
                                    finally
                                    {
                                        output1?.Dispose();
                                    }
                                }
                            }
                        }
                    }

                    if (!found)
                        throw new Exception($"No DXGI output for {screen.DeviceName}");
                }
            }
        }

        /// <summary>
        /// Capture and stitch all screens into a single bitmap.
        /// The returned bitmap is owned by this instance - do NOT dispose it.
        /// It is valid until the next call to Capture() or Dispose().
        /// </summary>
        public Bitmap Capture()
        {
            EnsureBitmap();

            if (_dxgiReady)
            {
                try
                {
                    FillDxgi();
                    return _reusableBmp;
                }
                catch (SharpDXException ex) when (ex.HResult == ACCESS_LOST)
                {
                    _log?.Invoke($"DXGI access lost, reinitialising: {ex.Message}");
                    try
                    {
                        CleanupDxgi();
                        InitDxgi(_log);
                        _dxgiReady = true;
                        FillDxgi();
                        return _reusableBmp;
                    }
                    catch (Exception reinitEx)
                    {
                        _log?.Invoke($"DXGI reinit failed, falling back to GDI: {reinitEx.Message}");
                        CleanupDxgi();
                        _dxgiReady = false;
                        CaptureMethod = "GDI (CPU)";
                    }
                }
                catch (Exception ex)
                {
                    _log?.Invoke($"DXGI capture error, falling back to GDI: {ex.Message}");
                    CleanupDxgi();
                    _dxgiReady = false;
                    CaptureMethod = "GDI (CPU)";
                }
            }

            FillGdi();
            return _reusableBmp;
        }

        private void EnsureBitmap()
        {
            if (_reusableBmp != null && _reusableBmp.Width == _totalWidth && _reusableBmp.Height == _totalHeight)
                return;
            _reusableBmp?.Dispose();
            _reusableBmp = new Bitmap(_totalWidth, _totalHeight, PixelFormat.Format32bppArgb);
        }

        private void FillDxgi()
        {
            var bmpData = _reusableBmp.LockBits(
                new Rectangle(0, 0, _totalWidth, _totalHeight),
                ImageLockMode.WriteOnly,
                PixelFormat.Format32bppArgb);

            try
            {
                var dstStride = bmpData.Stride;
                var dstBase = bmpData.Scan0;

                for (int i = 0; i < _slots.Length; i++)
                {
                    ref var slot = ref _slots[i];

                    // Use blocking timeout for first frame, non-blocking thereafter
                    var timeout = slot.HasFrame ? 0 : 1000;
                    Resource screenResource = null;
                    bool acquired = false;
                    try
                    {
                        var hr = slot.Duplication.TryAcquireNextFrame(timeout, out _, out screenResource);

                        // ACCESS_LOST must trigger reinit, not a silent fallback
                        if (hr.Code == ACCESS_LOST)
                            throw new SharpDXException(hr);

                        if (hr.Success && screenResource != null)
                        {
                            using (var tex = screenResource.QueryInterface<Texture2D>())
                            {
                                slot.Device.ImmediateContext.CopyResource(tex, slot.StagingTexture);
                            }
                            slot.HasFrame = true;
                            acquired = true;
                        }
                    }
                    finally
                    {
                        screenResource?.Dispose();
                        if (acquired)
                        {
                            try { slot.Duplication.ReleaseFrame(); } catch { }
                        }
                    }

                    if (!slot.HasFrame)
                        throw new Exception($"DXGI: no frame available for slot {i} (timeout={timeout}ms)");

                    // Read from staging texture (current or previous frame)
                    var mapped = slot.Device.ImmediateContext.MapSubresource(
                        slot.StagingTexture, 0, MapMode.Read, MapFlags.None);

                    try
                    {
                        var srcPitch = mapped.RowPitch;
                        var srcPtr = mapped.DataPointer;
                        var bytesPerRow = slot.Width * 4;
                        var dstOffsetBytes = slot.OffsetX * 4;

                        for (int y = 0; y < slot.Height; y++)
                        {
                            var src = IntPtr.Add(srcPtr, y * srcPitch);
                            var dst = IntPtr.Add(dstBase, y * dstStride + dstOffsetBytes);
                            Utilities.CopyMemory(dst, src, bytesPerRow);
                        }
                    }
                    finally
                    {
                        slot.Device.ImmediateContext.UnmapSubresource(slot.StagingTexture, 0);
                    }
                }
            }
            finally
            {
                _reusableBmp.UnlockBits(bmpData);
            }
        }

        private void FillGdi()
        {
            using (var g = Graphics.FromImage(_reusableBmp))
            {
                g.Clear(Color.Black);
                var offsetX = 0;
                foreach (var screen in _screens)
                {
                    var b = screen.Bounds;
                    g.CopyFromScreen(b.X, b.Y, offsetX, 0, b.Size);
                    offsetX += b.Width;
                }
            }
        }

        private void CleanupDxgi()
        {
            if (_slots == null) return;
            foreach (var slot in _slots)
            {
                try { slot.Duplication?.Dispose(); } catch { }
                try { slot.StagingTexture?.Dispose(); } catch { }
                try { slot.Device?.Dispose(); } catch { }
            }
            _slots = null;
        }

        private void Cleanup()
        {
            CleanupDxgi();
            _reusableBmp?.Dispose();
            _reusableBmp = null;
        }

        public void Dispose() => Cleanup();
    }
}
