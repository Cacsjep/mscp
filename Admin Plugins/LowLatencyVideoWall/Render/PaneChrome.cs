using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using LowLatencyVideoWall.Config;
using SharpDX.Direct2D1;
using SharpDX.DirectWrite;
using SharpDX.Mathematics.Interop;
using D2DFactory = SharpDX.Direct2D1.Factory;
using DWriteFactory = SharpDX.DirectWrite.Factory;
using Texture2D = SharpDX.Direct3D11.Texture2D;

namespace LowLatencyVideoWall.Render
{
    /// <summary>
    /// Everything on the wall that is not video: the pane title bars, the borders, the
    /// progress rings, the error panels and the diagnostics boxes.
    ///
    /// It has to be Direct2D rather than WPF. The wall is a child HWND with its own swapchain,
    /// so it owns every pixel inside itself and paints over any WPF sibling in the same window
    /// regardless of z-order. The only way to put a label on a pane is to draw it into the
    /// same backbuffer the video went into, which is what this does: after the blits, before
    /// the present, on the render thread, through a Direct2D render target built over the
    /// swapchain's DXGI surface.
    ///
    /// That render target is why <see cref="WallRenderer"/> creates the device with
    /// BgraSupport: a DXGI surface render target is the Direct2D 1.0 interop path and it
    /// refuses to be created on a device without it.
    ///
    /// Two costs are worth knowing about. The render target holds a reference to the
    /// backbuffer surface, so it has to be destroyed before ResizeBuffers and rebuilt after:
    /// see <see cref="SetBackBuffer"/>. And the text is sampled, formatted and cached at
    /// <see cref="SampleIntervalMs"/> rather than per present, because formatting sixteen
    /// lines of diagnostics for 48 panes 60 times a second would cost more than the video.
    /// </summary>
    internal sealed class PaneChrome : IDisposable
    {
        /// <summary>Matched against the Smart Client's own view item title bar.</summary>
        private const float TitleBarHeight = 22f;
        private const float TitlePad = 7f;
        private const float TitleFontSize = 12f;

        private const float RingRadius = 14f;
        private const float RingStrokeWidth = 3f;

        /// <summary>Turns per second of the progress ring.</summary>
        private const double RingTurnsPerSecond = 0.9;

        private const float DiagFontSize = 11f;
        private const float DiagLineHeight = 14f;
        private const float DiagPad = 5f;

        /// <summary>
        /// Advance width of one Consolas glyph as a fraction of the em size. The diagnostics
        /// box is sized from this rather than from a DirectWrite measurement: the font is
        /// monospaced, so multiplying by the longest line is exact, and it saves building a
        /// text layout per pane.
        /// </summary>
        private const float MonospaceAdvance = 0.55f;

        private const float MessageFontSize = 13f;
        private const float DetailFontSize = 11f;
        private const float MessageLineHeight = 18f;
        private const float DetailLineHeight = 15f;
        private const float PanelPad = 14f;
        private const float PanelMargin = 10f;
        private const float PanelMaxWidth = 560f;

        /// <summary>
        /// Rough advance width of one Segoe UI glyph as a fraction of the em size, used to
        /// wrap the error panel's technical message. Deliberately on the wide side: wrapping
        /// one line earlier than DirectWrite would only makes the panel a little taller, while
        /// underestimating it would push the last line out of the panel.
        /// </summary>
        private const float ProportionalAdvance = 0.52f;

        /// <summary>
        /// How often the overlay text is rebuilt. Also the interval the overlay's own rate
        /// meters smooth over, which is why it is not shorter: at 60 presents a second the
        /// frame counters barely move between passes and the numbers would be noise.
        /// </summary>
        private const int SampleIntervalMs = 500;

        private static readonly RawColor4 TitleBackColor = new RawColor4(0.07f, 0.07f, 0.07f, 0.72f);
        private static readonly RawColor4 WhiteColor = new RawColor4(1f, 1f, 1f, 1f);
        private static readonly RawColor4 BorderColor = Rgb(0x3C, 0x3C, 0x3C);
        private static readonly RawColor4 HoverColor = Rgb(0x00, 0x99, 0xDA);
        private static readonly RawColor4 DiagBackColor = new RawColor4(0f, 0f, 0f, 0.62f);
        private static readonly RawColor4 PanelColor = Rgb(0x2B, 0x2B, 0x2B);
        private static readonly RawColor4 PanelTextColor = Rgb(0xE6, 0xE6, 0xE6);
        private static readonly RawColor4 PanelDimColor = Rgb(0x9A, 0x9A, 0x9A);

        private static readonly RawMatrix3x2 Identity =
            new RawMatrix3x2(1f, 0f, 0f, 1f, 0f, 0f);

        private readonly Dictionary<Guid, PaneOverlay> _overlays = new Dictionary<Guid, PaneOverlay>();
        private readonly List<string> _lineBuffer = new List<string>(24);
        private readonly StringBuilder _text = new StringBuilder(96);
        private readonly Stopwatch _clock = Stopwatch.StartNew();

        private D2DFactory _d2dFactory;
        private DWriteFactory _dwriteFactory;

        private TextFormat _titleFormat;
        private TextFormat _diagFormat;
        private TextFormat _centredFormat;
        private TextFormat _messageFormat;
        private TextFormat _detailFormat;
        private EllipsisTrimming _ellipsis;
        private PathGeometry _ring;
        private StrokeStyle _ringStroke;

        private SharpDX.DXGI.Surface _surface;
        private RenderTarget _target;

        private SolidColorBrush _titleBackBrush;
        private SolidColorBrush _whiteBrush;
        private SolidColorBrush _borderBrush;
        private SolidColorBrush _hoverBrush;
        private SolidColorBrush _diagBackBrush;
        private SolidColorBrush _panelBrush;
        private SolidColorBrush _panelTextBrush;
        private SolidColorBrush _panelDimBrush;

        /// <summary>
        /// Set once anything in here throws. The wall keeps presenting video without chrome
        /// rather than dying for the sake of a label, and the log says so once instead of 60
        /// times a second.
        /// </summary>
        private bool _failed;

        internal PaneChrome()
        {
            try
            {
                // Single threaded: every call into Direct2D from here happens on the one
                // render thread, same as the blits.
                _d2dFactory = new D2DFactory(SharpDX.Direct2D1.FactoryType.SingleThreaded);
                _dwriteFactory = new DWriteFactory(SharpDX.DirectWrite.FactoryType.Shared);

                CreateTextFormats();
                CreateRingGeometry();
            }
            catch (Exception ex)
            {
                Fail("Direct2D setup failed", ex);
            }
        }

        /// <summary>
        /// Points the chrome at the current swapchain backbuffer, or at nothing when passed
        /// null. The render target keeps a reference to the surface, so DXGI refuses to resize
        /// the swapchain while one exists: the renderer calls this with null before
        /// ResizeBuffers and with the new backbuffer afterwards.
        /// </summary>
        internal void SetBackBuffer(Texture2D backBuffer)
        {
            ReleaseTarget();

            if (_failed || backBuffer == null) return;

            try
            {
                _surface = backBuffer.QueryInterface<SharpDX.DXGI.Surface>();

                var properties = new RenderTargetProperties
                {
                    Type = RenderTargetType.Default,
                    PixelFormat = new PixelFormat(SharpDX.DXGI.Format.B8G8R8A8_UNorm,
                        SharpDX.Direct2D1.AlphaMode.Ignore),

                    // 96 dpi on purpose. The pane rectangles are already real backbuffer
                    // pixels, computed by PaneLayout precisely so that nothing rounds them
                    // twice, and at 96 dpi one Direct2D unit is one of those pixels.
                    DpiX = 96f,
                    DpiY = 96f,
                    Usage = RenderTargetUsage.None,
                    MinLevel = SharpDX.Direct2D1.FeatureLevel.Level_DEFAULT
                };

                _target = new RenderTarget(_d2dFactory, _surface, properties);

                // Everything here but the ring is an axis aligned rectangle on a pixel
                // boundary, so antialiasing it would only blur the one pixel borders.
                _target.AntialiasMode = AntialiasMode.Aliased;
                _target.TextAntialiasMode = SharpDX.Direct2D1.TextAntialiasMode.Grayscale;

                CreateBrushes();
            }
            catch (Exception ex)
            {
                Fail("Direct2D render target over the backbuffer failed", ex);
                ReleaseTarget();
            }
        }

        /// <summary>
        /// Draws the chrome for every pane. Returns true when something on screen is animating
        /// and the wall therefore has to keep presenting even though no frame arrived.
        /// </summary>
        internal bool Draw(IReadOnlyList<WallPane> panes, WallPane hovered, long nowTicks)
        {
            if (_failed || _target == null || panes == null || panes.Count == 0) return false;

            var showTitles = WallClientSettings.ShowTitleBar;
            var showDiagnostics = WallClientSettings.ShowDiagnostics;
            var animating = false;
            var drawing = false;

            try
            {
                _target.BeginDraw();
                drawing = true;

                for (var i = 0; i < panes.Count; i++)
                {
                    var pane = panes[i];
                    var rect = pane.Rect;
                    if (rect.IsEmpty) continue;

                    // One clip per pane, so no overlay can spill into a neighbour: titles that
                    // do not fit, long error messages and the diagnostics box on a small pane
                    // are all cut off at the pane edge instead.
                    _target.PushAxisAlignedClip(
                        new RawRectangleF(rect.Left, rect.Top, rect.Right, rect.Bottom),
                        AntialiasMode.Aliased);

                    try
                    {
                        var overlay = EnsureOverlay(pane, nowTicks, showDiagnostics);

                        switch (overlay.Kind)
                        {
                            case OverlayKind.Progress:
                                DrawProgress(rect, overlay.ProgressText);
                                animating = true;
                                break;

                            case OverlayKind.Error:
                                DrawErrorPanel(rect, overlay);
                                break;
                        }

                        if (showTitles) DrawTitleBar(rect, pane.DisplayName);
                        if (showDiagnostics) DrawDiagnostics(rect, overlay, showTitles);

                        DrawBorder(rect, ReferenceEquals(pane, hovered));
                    }
                    finally
                    {
                        _target.PopAxisAlignedClip();
                    }
                }
            }
            catch (Exception ex)
            {
                Fail("Drawing the pane chrome failed", ex);
            }
            finally
            {
                if (drawing)
                {
                    try { _target.EndDraw(); }
                    catch (Exception ex) { Fail("Direct2D EndDraw failed", ex); }
                }
            }

            PruneOverlays(panes);
            return animating;
        }

        // ----------------------------------------------------------------- the pieces

        private void DrawTitleBar(PixelRect rect, string name)
        {
            // Nothing legible fits on a pane shorter than two title bars, and a strip over
            // half the picture is worse than no label.
            if (rect.Height < TitleBarHeight * 2f || rect.Width < 40) return;

            _target.FillRectangle(
                new RawRectangleF(rect.Left, rect.Top, rect.Right, rect.Top + TitleBarHeight),
                _titleBackBrush);

            _target.DrawText(name ?? "", _titleFormat,
                new RawRectangleF(rect.Left + TitlePad, rect.Top,
                    rect.Right - TitlePad, rect.Top + TitleBarHeight),
                _whiteBrush);
        }

        private void DrawBorder(PixelRect rect, bool isHovered)
        {
            // Half a pixel in, because a one pixel stroke straddles the path: on the integer
            // pane edge half of it would land outside the pane and be clipped away.
            _target.DrawRectangle(
                new RawRectangleF(rect.Left + 0.5f, rect.Top + 0.5f,
                    rect.Right - 0.5f, rect.Bottom - 0.5f),
                isHovered ? _hoverBrush : _borderBrush, 1f);
        }

        private void DrawProgress(PixelRect rect, string message)
        {
            var centreX = rect.Left + rect.Width / 2f;
            var centreY = rect.Top + rect.Height / 2f;

            var hasRing = rect.Width >= 60 && rect.Height >= 60;

            if (hasRing)
            {
                var angle = _clock.Elapsed.TotalSeconds * RingTurnsPerSecond % 1.0 * 2.0 * Math.PI;
                var cos = (float)Math.Cos(angle);
                var sin = (float)Math.Sin(angle);

                // Rotate and translate only. Scaling the geometry would scale the stroke with
                // it, and the ring is meant to be the same size on every pane of the wall.
                _target.Transform = new RawMatrix3x2(cos, sin, -sin, cos, centreX, centreY);
                _target.AntialiasMode = AntialiasMode.PerPrimitive;

                _target.DrawGeometry(_ring, _whiteBrush, RingStrokeWidth, _ringStroke);

                _target.AntialiasMode = AntialiasMode.Aliased;
                _target.Transform = Identity;
            }

            if (string.IsNullOrEmpty(message) || rect.Width < 110 || rect.Height < 30) return;

            var top = hasRing ? centreY + RingRadius + 12f : centreY - 9f;
            _target.DrawText(message, _centredFormat,
                new RawRectangleF(rect.Left + 4, top, rect.Right - 4, top + 20f),
                _panelDimBrush);
        }

        /// <summary>
        /// The Milestone camera error look: a plain grey panel, no red, a human sentence, then
        /// the identity of what failed. The technical message is ours to add, because unlike
        /// the Smart Client this plugin is the thing talking to the camera, so the operator is
        /// looking at the only report of that conversation there is.
        /// </summary>
        private void DrawErrorPanel(PixelRect rect, PaneOverlay overlay)
        {
            var width = Math.Min(PanelMaxWidth, rect.Width - 2f * PanelMargin);
            if (width < 80f) return;

            var detailCount = overlay.ErrorDetail == null ? 0 : overlay.ErrorDetail.Length;
            var height = 2f * PanelPad
                         + 2f * MessageLineHeight
                         + MessageLineHeight / 2f
                         + detailCount * DetailLineHeight;

            height = Math.Min(height, rect.Height - 2f * PanelMargin);
            if (height < 40f) return;

            var left = rect.Left + (rect.Width - width) / 2f;
            var top = rect.Top + (rect.Height - height) / 2f;

            _target.FillRectangle(new RawRectangleF(left, top, left + width, top + height),
                _panelBrush);

            var textLeft = left + PanelPad;
            var textRight = left + width - PanelPad;
            var y = top + PanelPad;

            _target.DrawText(overlay.ErrorHeadline, _messageFormat,
                new RawRectangleF(textLeft, y, textRight, y + MessageLineHeight), _panelTextBrush);
            y += MessageLineHeight;

            _target.DrawText(overlay.ErrorContext, _messageFormat,
                new RawRectangleF(textLeft, y, textRight, y + MessageLineHeight), _panelTextBrush);

            // The blank line of the original, which is what makes the identity read as a
            // footnote rather than as part of the sentence.
            y += MessageLineHeight + MessageLineHeight / 2f;

            if (overlay.ErrorDetail == null) return;

            for (var i = 0; i < overlay.ErrorDetail.Length; i++)
            {
                _target.DrawText(overlay.ErrorDetail[i], _detailFormat,
                    new RawRectangleF(textLeft, y, textRight, y + DetailLineHeight),
                    _panelDimBrush);
                y += DetailLineHeight;
            }
        }

        private void DrawDiagnostics(PixelRect rect, PaneOverlay overlay, bool belowTitle)
        {
            var lines = overlay.DiagnosticLines;
            if (lines == null || lines.Length == 0) return;

            var width = overlay.DiagnosticWidth;
            var height = lines.Length * DiagLineHeight + 2f * DiagPad;

            // The box is only useful if it leaves some picture around it.
            if (rect.Width < width + 16f || rect.Height < height + 16f) return;

            var left = rect.Left + 6f;
            var top = rect.Top + (belowTitle ? TitleBarHeight + 6f : 6f);

            _target.FillRectangle(new RawRectangleF(left, top, left + width, top + height),
                _diagBackBrush);

            var y = top + DiagPad;
            for (var i = 0; i < lines.Length; i++)
            {
                _target.DrawText(lines[i], _diagFormat,
                    new RawRectangleF(left + DiagPad, y, left + width - DiagPad, y + DiagLineHeight),
                    _whiteBrush);
                y += DiagLineHeight;
            }
        }

        // ----------------------------------------------------------------- per pane state

        /// <summary>
        /// The cached text for one pane, refreshed on <see cref="SampleIntervalMs"/>. What the
        /// pane is doing is read every pass, because that has to be instant; only the strings
        /// and the statistics behind them are on the slower clock.
        /// </summary>
        private PaneOverlay EnsureOverlay(WallPane pane, long nowTicks, bool showDiagnostics)
        {
            PaneOverlay overlay;
            if (!_overlays.TryGetValue(pane.CameraId, out overlay))
            {
                overlay = new PaneOverlay();
                _overlays[pane.CameraId] = overlay;
            }

            overlay.Seen = true;

            var kind = DecideKind(pane, nowTicks);
            var paneWidth = pane.Rect.Width;

            var due = nowTicks >= overlay.NextSampleTicks
                      || kind != overlay.Kind
                      || (kind == OverlayKind.Error && paneWidth != overlay.BuiltForWidth);

            if (!due) return overlay;

            overlay.NextSampleTicks = nowTicks + SampleIntervalMs * Stopwatch.Frequency / 1000;
            overlay.Kind = kind;
            overlay.BuiltForWidth = paneWidth;

            // One sample feeds both the diagnostics box and the error panel, so a pane that is
            // showing neither costs nothing beyond this check.
            var sampled = showDiagnostics || kind == OverlayKind.Error;
            var stats = sampled ? pane.ReadOverlayStats() : default(PaneStatsSnapshot);

            overlay.ProgressText = kind == OverlayKind.Progress ? ProgressText(pane) : null;
            overlay.DiagnosticLines = showDiagnostics ? BuildDiagnostics(stats) : null;
            overlay.DiagnosticWidth = MonospaceWidth(overlay.DiagnosticLines);

            if (kind == OverlayKind.Error)
                BuildErrorText(pane, stats, overlay);
            else
                overlay.ErrorDetail = null;

            return overlay;
        }

        /// <summary>
        /// What this pane should be showing over the video. Nothing, while it has a picture
        /// the watchdog still trusts: an overlay only ever appears over black.
        /// </summary>
        private static OverlayKind DecideKind(WallPane pane, long nowTicks)
        {
            if (pane.HasPicture && !pane.Suspended && !pane.PictureIsStale(nowTicks))
                return OverlayKind.None;

            // Suspended means the renderer is restarting this stream at a new resolution, and
            // it has already dropped the pane's views and its frame.
            if (pane.Suspended) return OverlayKind.Progress;

            switch (pane.State)
            {
                case PaneState.Error:
                    return OverlayKind.Error;

                case PaneState.Streaming:
                    // The session claims to be up but nothing has arrived for long enough that
                    // the watchdog took the picture away, so this is a silent stall rather
                    // than something that is being retried.
                    return OverlayKind.Error;

                default:
                    return OverlayKind.Progress;
            }
        }

        private static string ProgressText(WallPane pane)
        {
            if (pane.Suspended) return "Changing stream resolution";

            switch (pane.State)
            {
                case PaneState.Connecting: return "Connecting to the camera";
                case PaneState.AwaitingKeyFrame: return "Waiting for a key frame";
                case PaneState.Reconnecting: return "Reconnecting to the camera";
                default: return "Starting";
            }
        }

        private void BuildErrorText(WallPane pane, PaneStatsSnapshot stats, PaneOverlay overlay)
        {
            overlay.ErrorHeadline = pane.State == PaneState.Streaming
                ? "The camera stopped sending video."
                : "No video from this camera.";

            overlay.ErrorContext =
                "The wall reads the camera directly, not through the recording server.";

            var host = pane.Camera.Host;
            var identity = string.IsNullOrEmpty(host)
                ? pane.DisplayName
                : pane.DisplayName + " (" + host + ")";
            if (pane.Camera.Channel > 0)
                identity += " - channel " + (pane.Camera.Channel + 1);

            var width = Math.Min(PanelMaxWidth, Math.Max(80f, pane.Rect.Width - 2f * PanelMargin));
            var characters = (int)((width - 2f * PanelPad) / (DetailFontSize * ProportionalAdvance));
            if (characters < 16) characters = 16;

            _lineBuffer.Clear();
            _lineBuffer.Add(identity);
            if (!string.IsNullOrEmpty(pane.DisplayUrl)) _lineBuffer.Add(pane.DisplayUrl);

            // The real message, which is the whole reason this panel is worth drawing: an
            // RTSP 401 and a routing failure look identical without it.
            var detail = stats.Error ?? pane.LastError;
            if (!string.IsNullOrEmpty(detail)) Wrap(detail, characters, _lineBuffer);

            overlay.ErrorDetail = _lineBuffer.ToArray();
        }

        /// <summary>
        /// The classic black box, with the lines the wall can actually answer for. A field
        /// with no value behind it is left out rather than printed as a zero: a wall is read
        /// at a glance and a confident 0 fps is worse than a missing line.
        /// </summary>
        private string[] BuildDiagnostics(PaneStatsSnapshot stats)
        {
            _lineBuffer.Clear();

            Add("FPS", Number(stats.UniqueFps, 1));
            Add("Decoded", Number(stats.DecodeFps, 1) + " /s");

            if (!string.IsNullOrEmpty(stats.Codec)) Add("Video codec", stats.Codec);

            if (stats.SourceWidth > 0 && stats.SourceHeight > 0)
                Add("Video resolution", stats.SourceWidth + "x" + stats.SourceHeight);

            if (!string.IsNullOrEmpty(stats.RequestedResolution))
                Add("Requested", stats.RequestedResolution);

            Add("Hardware acceleration", stats.IsHardwareDecoded ? "d3d11va" : "none");

            if (!string.IsNullOrEmpty(stats.DecoderName)) Add("Decoder", stats.DecoderName);

            Add("Source state", stats.State.ToString());
            Add("Frames received", stats.VideoPackets.ToString());
            Add("Decode time", Number(stats.AvgDecodeMs, 2) + " ms");
            Add("Latency", Number(stats.AvgLatencyMs, 1) + " ms, peak " +
                           Number(stats.PeakLatencyMs, 1) + " ms");
            Add("Dropped frames", stats.FramesDropped.ToString());
            Add("Decode errors", stats.DecodeErrors.ToString());

            if (stats.LastGopFrames > 0)
            {
                Add("Last GOP", stats.LastGopFrames + " frames");
                Add("Last GOP bitrate", Number(stats.LastGopMbitPerSecond, 2) + " Mbit/s");
            }

            Add("Bitrate", Number(stats.MbitPerSecond, 2) + " Mbit/s");

            if (stats.TimeToFirstFrameMs > 0)
                Add("Time to first frame", Number(stats.TimeToFirstFrameMs, 0) + " ms");

            return _lineBuffer.ToArray();
        }

        private void Add(string label, string value)
        {
            _text.Clear();
            _text.Append(label).Append(": ").Append(value);
            _lineBuffer.Add(_text.ToString());
        }

        private static string Number(double value, int decimals)
            => value.ToString("F" + decimals);

        private static float MonospaceWidth(string[] lines)
        {
            if (lines == null || lines.Length == 0) return 0f;

            var longest = 0;
            for (var i = 0; i < lines.Length; i++)
                if (lines[i].Length > longest) longest = lines[i].Length;

            return longest * DiagFontSize * MonospaceAdvance + 2f * DiagPad;
        }

        /// <summary>
        /// Greedy word wrap, done here rather than by DirectWrite because the panel's height
        /// has to be known before the text is drawn and this way the two cannot disagree.
        /// A token longer than the line, which an RTSP URL or an FFmpeg error usually is, is
        /// broken rather than allowed to run past the panel.
        /// </summary>
        private static void Wrap(string text, int maxCharacters, List<string> into)
        {
            var words = text.Split(' ');
            var line = new StringBuilder(maxCharacters);

            foreach (var word in words)
            {
                var token = word;

                while (token.Length > maxCharacters)
                {
                    if (line.Length > 0)
                    {
                        into.Add(line.ToString());
                        line.Clear();
                    }

                    into.Add(token.Substring(0, maxCharacters));
                    token = token.Substring(maxCharacters);
                }

                if (token.Length == 0) continue;

                if (line.Length > 0 && line.Length + 1 + token.Length > maxCharacters)
                {
                    into.Add(line.ToString());
                    line.Clear();
                }

                if (line.Length > 0) line.Append(' ');
                line.Append(token);
            }

            if (line.Length > 0) into.Add(line.ToString());
        }

        private void PruneOverlays(IReadOnlyList<WallPane> panes)
        {
            // Only when there is something to prune, which is when a camera has left the wall.
            if (_overlays.Count <= panes.Count) return;

            var dead = new List<Guid>();
            foreach (var pair in _overlays)
            {
                if (pair.Value.Seen) pair.Value.Seen = false;
                else dead.Add(pair.Key);
            }

            foreach (var id in dead)
                _overlays.Remove(id);
        }

        // ----------------------------------------------------------------- resources

        private void CreateTextFormats()
        {
            _titleFormat = new TextFormat(_dwriteFactory, "Segoe UI", TitleFontSize)
            {
                TextAlignment = TextAlignment.Leading,
                ParagraphAlignment = ParagraphAlignment.Center,
                WordWrapping = WordWrapping.NoWrap
            };

            // A camera name is as long as whoever named it felt like, and the pane is as wide
            // as the wall made it, so the title has to end in an ellipsis rather than in a
            // hard clip half way through a glyph.
            _ellipsis = new EllipsisTrimming(_dwriteFactory, _titleFormat);
            _titleFormat.SetTrimming(
                new Trimming { Granularity = TrimmingGranularity.Character }, _ellipsis);

            _diagFormat = new TextFormat(_dwriteFactory, "Consolas", DiagFontSize)
            {
                TextAlignment = TextAlignment.Leading,
                ParagraphAlignment = ParagraphAlignment.Near,
                WordWrapping = WordWrapping.NoWrap
            };

            _centredFormat = new TextFormat(_dwriteFactory, "Segoe UI", MessageFontSize)
            {
                TextAlignment = TextAlignment.Center,
                ParagraphAlignment = ParagraphAlignment.Near,
                WordWrapping = WordWrapping.NoWrap
            };

            _messageFormat = new TextFormat(_dwriteFactory, "Segoe UI", MessageFontSize)
            {
                TextAlignment = TextAlignment.Leading,
                ParagraphAlignment = ParagraphAlignment.Near,
                WordWrapping = WordWrapping.NoWrap
            };

            _detailFormat = new TextFormat(_dwriteFactory, "Segoe UI", DetailFontSize)
            {
                TextAlignment = TextAlignment.Leading,
                ParagraphAlignment = ParagraphAlignment.Near,
                WordWrapping = WordWrapping.NoWrap
            };
        }

        /// <summary>
        /// Three quarters of a circle around the origin, drawn once. The animation is a
        /// rotation of this one geometry, so nothing is rebuilt per present.
        /// </summary>
        private void CreateRingGeometry()
        {
            _ring = new PathGeometry(_d2dFactory);

            using (var sink = _ring.Open())
            {
                sink.BeginFigure(new RawVector2(RingRadius, 0f), FigureBegin.Hollow);
                sink.AddArc(new ArcSegment
                {
                    Point = new RawVector2(0f, -RingRadius),
                    Size = new SharpDX.Size2F(RingRadius, RingRadius),
                    RotationAngle = 0f,
                    SweepDirection = SweepDirection.Clockwise,
                    ArcSize = ArcSize.Large
                });
                sink.EndFigure(FigureEnd.Open);
                sink.Close();
            }

            _ringStroke = new StrokeStyle(_d2dFactory, new StrokeStyleProperties
            {
                StartCap = CapStyle.Round,
                EndCap = CapStyle.Round,
                DashCap = CapStyle.Flat,
                LineJoin = LineJoin.Round,
                MiterLimit = 1f,
                DashStyle = DashStyle.Solid,
                DashOffset = 0f
            });
        }

        private void CreateBrushes()
        {
            _titleBackBrush = new SolidColorBrush(_target, TitleBackColor);
            _whiteBrush = new SolidColorBrush(_target, WhiteColor);
            _borderBrush = new SolidColorBrush(_target, BorderColor);
            _hoverBrush = new SolidColorBrush(_target, HoverColor);
            _diagBackBrush = new SolidColorBrush(_target, DiagBackColor);
            _panelBrush = new SolidColorBrush(_target, PanelColor);
            _panelTextBrush = new SolidColorBrush(_target, PanelTextColor);
            _panelDimBrush = new SolidColorBrush(_target, PanelDimColor);
        }

        private void ReleaseTarget()
        {
            // Brushes belong to the render target they were created on, so they go first.
            Release(ref _titleBackBrush);
            Release(ref _whiteBrush);
            Release(ref _borderBrush);
            Release(ref _hoverBrush);
            Release(ref _diagBackBrush);
            Release(ref _panelBrush);
            Release(ref _panelTextBrush);
            Release(ref _panelDimBrush);

            _target?.Dispose();
            _target = null;

            _surface?.Dispose();
            _surface = null;
        }

        private static void Release<T>(ref T resource) where T : class, IDisposable
        {
            resource?.Dispose();
            resource = null;
        }

        private static RawColor4 Rgb(int red, int green, int blue)
            => new RawColor4(red / 255f, green / 255f, blue / 255f, 1f);

        private void Fail(string what, Exception ex)
        {
            if (_failed) return;
            _failed = true;
            LowLatencyVideoWallDefinition.Log.Error(
                $"{what}: {ex.GetType().Name}: {ex.Message}. " +
                "The wall will keep presenting video without pane chrome.");
        }

        public void Dispose()
        {
            ReleaseTarget();

            Release(ref _ringStroke);
            Release(ref _ring);
            Release(ref _ellipsis);
            Release(ref _titleFormat);
            Release(ref _diagFormat);
            Release(ref _centredFormat);
            Release(ref _messageFormat);
            Release(ref _detailFormat);
            Release(ref _dwriteFactory);
            Release(ref _d2dFactory);
        }

        private enum OverlayKind
        {
            None,
            Progress,
            Error
        }

        private sealed class PaneOverlay
        {
            internal OverlayKind Kind;
            internal long NextSampleTicks;
            internal int BuiltForWidth;
            internal bool Seen;

            internal string ProgressText;
            internal string[] DiagnosticLines;
            internal float DiagnosticWidth;

            internal string ErrorHeadline = "";
            internal string ErrorContext = "";
            internal string[] ErrorDetail;
        }
    }
}
