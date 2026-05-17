using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using VideoOS.Platform;
using VideoOS.Platform.Client;

namespace SCRemoteControl.Overlay
{
    /// <summary>
    /// Tracks the live ImageViewerAddOns in the Smart Client and applies registered
    /// overlays to each viewport that displays a matching cameraId. Overlays are
    /// addressed by a caller-supplied string key; POST is upsert.
    /// </summary>
    class OverlayManager
    {
        public const int MaxShapesPerOverlay = 500;
        public const int MaxOverlaysPerCamera = 32;
        public const int MaxSvgBytes = 50 * 1024;

        private static readonly Lazy<OverlayManager> _instance = new Lazy<OverlayManager>(() => new OverlayManager());
        public static OverlayManager Instance => _instance.Value;

        private readonly object _lock = new object();
        private readonly Dictionary<string, OverlayRecord> _overlays = new Dictionary<string, OverlayRecord>(StringComparer.Ordinal);
        private readonly List<ImageViewerAddOn> _activeAddOns = new List<ImageViewerAddOn>();

        private DispatcherTimer _timer;
        private ClientControl.NewImageViewerControlHandler _newViewerHandler;
        private bool _started;

        public void Start()
        {
            if (_started) return;
            _started = true;

            _newViewerHandler = OnNewImageViewerControl;
            ClientControl.Instance.NewImageViewerControlEvent += _newViewerHandler;

            // Run the redraw + TTL pass on the UI thread. 3 Hz keeps the
            // overlay aligned through window resizes without measurable cost.
            Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                _timer = new DispatcherTimer(DispatcherPriority.Background)
                {
                    Interval = TimeSpan.FromMilliseconds(333)
                };
                _timer.Tick += OnTick;
                _timer.Start();
            }));

            SCRemoteControlDefinition.Log.Info("OverlayManager started");
        }

        public void Stop()
        {
            if (!_started) return;
            _started = false;

            try { ClientControl.Instance.NewImageViewerControlEvent -= _newViewerHandler; } catch { }

            Application.Current?.Dispatcher.Invoke(new Action(() =>
            {
                _timer?.Stop();
                _timer = null;
                // Best-effort clear of existing overlays from every AddOn.
                lock (_lock)
                {
                    foreach (var rec in _overlays.Values)
                        foreach (var kv in rec.ShapeIds.ToList())
                            TryRemoveFromAddOn(kv.Key, kv.Value);
                    _overlays.Clear();
                    _activeAddOns.Clear();
                }
            }));

            SCRemoteControlDefinition.Log.Info("OverlayManager stopped");
        }

        // --- Public API ---

        public UpsertResult Upsert(string overlayId, Guid cameraId, string svg, int? ttlSeconds, int zOrder)
        {
            if (string.IsNullOrWhiteSpace(overlayId))
                throw new ArgumentException("overlayId is required");
            if (overlayId.Length > 128)
                throw new ArgumentException("overlayId must be 128 chars or less");
            if (cameraId == Guid.Empty)
                throw new ArgumentException("cameraId is required");
            if (svg == null || svg.Length > MaxSvgBytes)
                throw new ArgumentException("svg body too large (max " + MaxSvgBytes + " bytes)");

            var parsed = SvgParser.Parse(svg); // throws SvgParseException on bad input
            if (parsed.Shapes.Count > MaxShapesPerOverlay)
                throw new ArgumentException("overlay has " + parsed.Shapes.Count + " shapes, max " + MaxShapesPerOverlay);

            DateTime? expiresAt = null;
            if (ttlSeconds.HasValue && ttlSeconds.Value > 0)
                expiresAt = DateTime.UtcNow.AddSeconds(ttlSeconds.Value);

            OverlayRecord previous;
            OverlayRecord record;
            lock (_lock)
            {
                _overlays.TryGetValue(overlayId, out previous);

                if (previous == null)
                {
                    int countForCamera = _overlays.Values.Count(o => o.CameraId == cameraId);
                    if (countForCamera >= MaxOverlaysPerCamera)
                        throw new InvalidOperationException("camera has " + countForCamera + " overlays, max " + MaxOverlaysPerCamera);
                }

                record = new OverlayRecord
                {
                    OverlayId = overlayId,
                    CameraId = cameraId,
                    Svg = svg,
                    Parsed = parsed,
                    ZOrder = zOrder,
                    ExpiresAt = expiresAt,
                    CreatedAt = DateTime.UtcNow,
                };
                // Inherit existing shape IDs so the next tick can ShapesOverlayUpdate
                // in place. If camera changed, clear them so we re-add cleanly.
                if (previous != null && previous.CameraId == cameraId)
                    record.ShapeIds = previous.ShapeIds;

                _overlays[overlayId] = record;
            }

            // If the camera moved (upsert that changed cameraId), drop prior shapes
            // from the old AddOns.
            if (previous != null && previous.CameraId != cameraId)
            {
                Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                {
                    foreach (var kv in previous.ShapeIds)
                        TryRemoveFromAddOn(kv.Key, kv.Value);
                }));
            }

            // Trigger an immediate draw pass so the new overlay is visible
            // before the next timer tick.
            Application.Current?.Dispatcher.BeginInvoke(new Action(ApplyAll));

            return new UpsertResult
            {
                ExpiresAt = expiresAt,
                Displayed = AnyAddOnShowsCamera(cameraId),
                ShapeCount = parsed.Shapes.Count,
                Replaced = previous != null,
            };
        }

        public bool Remove(string overlayId)
        {
            OverlayRecord rec;
            lock (_lock)
            {
                if (!_overlays.TryGetValue(overlayId, out rec)) return false;
                _overlays.Remove(overlayId);
            }
            Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                foreach (var kv in rec.ShapeIds) TryRemoveFromAddOn(kv.Key, kv.Value);
            }));
            return true;
        }

        public int RemoveByCamera(Guid cameraId)
        {
            List<OverlayRecord> removed;
            lock (_lock)
            {
                removed = _overlays.Values.Where(o => o.CameraId == cameraId).ToList();
                foreach (var r in removed) _overlays.Remove(r.OverlayId);
            }
            Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                foreach (var rec in removed)
                    foreach (var kv in rec.ShapeIds) TryRemoveFromAddOn(kv.Key, kv.Value);
            }));
            return removed.Count;
        }

        public int RemoveAll()
        {
            List<OverlayRecord> removed;
            lock (_lock)
            {
                removed = _overlays.Values.ToList();
                _overlays.Clear();
            }
            Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                foreach (var rec in removed)
                    foreach (var kv in rec.ShapeIds) TryRemoveFromAddOn(kv.Key, kv.Value);
            }));
            return removed.Count;
        }

        public OverlayRecord Get(string overlayId)
        {
            lock (_lock)
            {
                _overlays.TryGetValue(overlayId, out var rec);
                return rec;
            }
        }

        public List<OverlayRecord> List()
        {
            lock (_lock) { return _overlays.Values.ToList(); }
        }

        public bool AnyAddOnShowsCamera(Guid cameraId)
        {
            lock (_activeAddOns)
            {
                return _activeAddOns.Any(a => a.CameraFQID != null && a.CameraFQID.ObjectId == cameraId);
            }
        }

        // --- AddOn lifecycle ---

        private void OnNewImageViewerControl(ImageViewerAddOn addOn)
        {
            lock (_activeAddOns) { _activeAddOns.Add(addOn); }
            addOn.CloseEvent += AddOn_CloseEvent;
            addOn.PropertyChangedEvent += AddOn_PropertyChangedEvent;
            // Apply any registered overlays that match the camera in this slot.
            Application.Current?.Dispatcher.BeginInvoke(new Action(ApplyAll));
        }

        private void AddOn_CloseEvent(object sender, EventArgs e)
        {
            var addOn = sender as ImageViewerAddOn;
            if (addOn == null) return;
            addOn.CloseEvent -= AddOn_CloseEvent;
            addOn.PropertyChangedEvent -= AddOn_PropertyChangedEvent;
            lock (_activeAddOns) { _activeAddOns.Remove(addOn); }
            lock (_lock)
            {
                foreach (var rec in _overlays.Values)
                    rec.ShapeIds.Remove(addOn); // shapes go away with the AddOn itself
            }
        }

        private void AddOn_PropertyChangedEvent(object sender, EventArgs e)
        {
            var addOn = sender as ImageViewerAddOn;
            if (addOn == null) return;

            // Camera in slot may have changed. Remove any shapes that no longer
            // match, then trigger a reapply pass for whatever camera is now shown.
            var currentCamera = addOn.CameraFQID?.ObjectId ?? Guid.Empty;
            List<KeyValuePair<OverlayRecord, Guid>> toRemove = new List<KeyValuePair<OverlayRecord, Guid>>();
            lock (_lock)
            {
                foreach (var rec in _overlays.Values)
                {
                    if (rec.ShapeIds.TryGetValue(addOn, out var shapeId) && rec.CameraId != currentCamera)
                    {
                        toRemove.Add(new KeyValuePair<OverlayRecord, Guid>(rec, shapeId));
                        rec.ShapeIds.Remove(addOn);
                    }
                }
            }
            foreach (var kv in toRemove) TryRemoveFromAddOn(addOn, kv.Value);

            Application.Current?.Dispatcher.BeginInvoke(new Action(ApplyAll));
        }

        // --- Tick / drawing ---

        private void OnTick(object sender, EventArgs e)
        {
            // Expire first so stale overlays don't redraw.
            PruneExpired();
            ApplyAll();
        }

        private void PruneExpired()
        {
            var now = DateTime.UtcNow;
            List<OverlayRecord> expired;
            lock (_lock)
            {
                expired = _overlays.Values
                    .Where(o => o.ExpiresAt.HasValue && o.ExpiresAt.Value <= now)
                    .ToList();
                foreach (var r in expired) _overlays.Remove(r.OverlayId);
            }
            foreach (var rec in expired)
                foreach (var kv in rec.ShapeIds.ToList())
                    TryRemoveFromAddOn(kv.Key, kv.Value);
        }

        private void ApplyAll()
        {
            OverlayRecord[] overlaysSnapshot;
            ImageViewerAddOn[] addOnsSnapshot;
            lock (_lock) { overlaysSnapshot = _overlays.Values.ToArray(); }
            lock (_activeAddOns) { addOnsSnapshot = _activeAddOns.ToArray(); }

            foreach (var addOn in addOnsSnapshot)
            {
                if (addOn == null || addOn.CameraFQID == null) continue;
                var paint = addOn.PaintSizeWpf;
                if (paint.Width <= 0 || paint.Height <= 0) continue;

                var cameraId = addOn.CameraFQID.ObjectId;
                foreach (var rec in overlaysSnapshot)
                {
                    if (rec.CameraId != cameraId) continue;

                    var shapes = BuildShapes(rec, paint);
                    if (shapes.Count == 0) continue;

                    var renderParams = new ShapesOverlayRenderParameters { ZOrder = rec.ZOrder };
                    try
                    {
                        if (rec.ShapeIds.TryGetValue(addOn, out var existingId))
                        {
                            addOn.ShapesOverlayUpdate(existingId, shapes, renderParams);
                        }
                        else
                        {
                            var id = addOn.ShapesOverlayAdd(shapes, renderParams);
                            rec.ShapeIds[addOn] = id;
                        }
                    }
                    catch (Exception ex)
                    {
                        SCRemoteControlDefinition.Log.Error("ShapesOverlayUpdate failed for overlay " + rec.OverlayId, ex);
                    }
                }
            }
        }

        private static List<Shape> BuildShapes(OverlayRecord rec, Size paint)
        {
            var vb = rec.Parsed.ViewBox;
            var sx = vb.Width > 0 ? paint.Width / vb.Width : 1;
            var sy = vb.Height > 0 ? paint.Height / vb.Height : 1;
            var scale = Math.Min(sx, sy);
            var dx = (paint.Width - vb.Width * scale) / 2.0;
            var dy = (paint.Height - vb.Height * scale) / 2.0;
            var m = new Matrix(scale, 0, 0, scale, dx - vb.X * scale, dy - vb.Y * scale);

            var list = new List<Shape>(rec.Parsed.Shapes.Count);
            foreach (var s in rec.Parsed.Shapes)
            {
                var shape = s.Render(m);
                if (shape != null) list.Add(shape);
            }
            return list;
        }

        private static void TryRemoveFromAddOn(ImageViewerAddOn addOn, Guid shapeId)
        {
            try { addOn.ShapesOverlayRemove(shapeId); }
            catch { /* AddOn may already be closed */ }
        }
    }

    class OverlayRecord
    {
        public string OverlayId;
        public Guid CameraId;
        public string Svg;
        public ParsedOverlay Parsed;
        public int ZOrder = 100;
        public DateTime? ExpiresAt;
        public DateTime CreatedAt;
        public Dictionary<ImageViewerAddOn, Guid> ShapeIds = new Dictionary<ImageViewerAddOn, Guid>();
    }

    class UpsertResult
    {
        public DateTime? ExpiresAt;
        public bool Displayed;
        public int ShapeCount;
        public bool Replaced;
    }
}
