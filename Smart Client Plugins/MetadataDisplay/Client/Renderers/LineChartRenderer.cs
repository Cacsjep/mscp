using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.Drawing;
using LiveChartsCore.Measure;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using LiveChartsCore.SkiaSharpView.Painting.Effects;
using LiveChartsCore.SkiaSharpView.WPF;
using SkiaSharp;
using WpfColor = System.Windows.Media.Color;

namespace MetadataDisplay.Client.Renderers
{
    internal enum LineChartType { Straight, Smooth, Step }

    internal enum LineAggregation { Mean, Min, Max }

    internal sealed class LineChartConfig
    {
        public int WindowSeconds;
        public double? YMin;
        public double? YMax;
        public WpfColor LineColor;
        public bool FillEnabled;
        public bool ShowMarker;
        public LineChartType LineType;
        public double LineThickness;
        public bool ZoomEnabled;
        public LineAggregation Aggregation;
        public bool EnvelopeEnabled;
        // Set by the view-item when entering playback. Suppresses the pause
        // overlay because there is no live tail to "pause" — the cursor anchors
        // the visible window. Zoom/pan still work; they simply don't trigger the
        // "click to resume live" badge.
        public bool PlaybackMode;
        public NumericConfig Numeric;

        public static LineChartConfig FromManager(MetadataDisplayViewItemManager m)
        {
            int win = 60;
            if (int.TryParse(m.LineWindowSeconds, NumberStyles.Integer, CultureInfo.InvariantCulture, out var w) && w > 0)
                win = w;

            var lt = LineChartType.Straight;
            if (string.Equals(m.LineSmoothing, "true", StringComparison.OrdinalIgnoreCase)) lt = LineChartType.Smooth;
            if (string.Equals(m.LineType, "Smooth", StringComparison.OrdinalIgnoreCase)) lt = LineChartType.Smooth;
            else if (string.Equals(m.LineType, "Step", StringComparison.OrdinalIgnoreCase)) lt = LineChartType.Step;
            else if (string.Equals(m.LineType, "Straight", StringComparison.OrdinalIgnoreCase)) lt = LineChartType.Straight;

            double thickness = 2;
            if (double.TryParse(m.LineThickness, NumberStyles.Float, CultureInfo.InvariantCulture, out var t) && t > 0) thickness = t;

            return new LineChartConfig
            {
                WindowSeconds = win,
                YMin = ParseNullable(m.LineYMin),
                YMax = ParseNullable(m.LineYMax),
                LineColor = ColorUtil.Parse(m.LineColor, WpfColor.FromRgb(0x4F, 0xC3, 0xF7)),
                FillEnabled = string.Equals(m.LineFill, "true", StringComparison.OrdinalIgnoreCase),
                ShowMarker = string.Equals(m.LineShowMarker, "true", StringComparison.OrdinalIgnoreCase),
                LineType = lt,
                LineThickness = thickness,
                ZoomEnabled = !string.Equals(m.LineZoomEnabled, "false", StringComparison.OrdinalIgnoreCase),
                Aggregation = ParseAgg(m.LineAggregation),
                EnvelopeEnabled = string.Equals(m.LineEnvelope, "true", StringComparison.OrdinalIgnoreCase),
                Numeric = NumericConfig.FromManager(m),
            };
        }

        private static LineAggregation ParseAgg(string s)
        {
            if (string.Equals(s, "Min", StringComparison.OrdinalIgnoreCase)) return LineAggregation.Min;
            if (string.Equals(s, "Max", StringComparison.OrdinalIgnoreCase)) return LineAggregation.Max;
            return LineAggregation.Mean;
        }

        private static double? ParseNullable(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? (double?)v : null;
        }
    }

    // One time bucket holding the aggregates needed to render mean / last / min /
    // max / count without keeping the underlying samples around.
    internal sealed class Bucket
    {
        public DateTime StartUtc;
        public double Sum;
        public double Min = double.PositiveInfinity;
        public double Max = double.NegativeInfinity;
        public int Count;

        public void Add(double v)
        {
            Sum += v;
            if (v < Min) Min = v;
            if (v > Max) Max = v;
            Count++;
        }

        public double Pick(LineAggregation agg)
        {
            switch (agg)
            {
                case LineAggregation.Min: return Count > 0 ? Min : 0;
                case LineAggregation.Max: return Count > 0 ? Max : 0;
                case LineAggregation.Mean:
                default:                  return Count > 0 ? Sum / Count : 0;
            }
        }
    }

    // Time-series line chart backed by LiveCharts2 / SkiaSharp.
    //
    // Storage: instead of raw samples we keep fixed-width buckets sized by the
    // window length (target ~300 visible buckets). This means a 60s window draws
    // 1s buckets, an hour window draws 12s buckets, a day window draws 5min
    // buckets — the chart cost is constant regardless of how long the user wants
    // to look back. Each bucket carries Sum/Min/Max/Last/Count so we can switch
    // aggregation modes without re-fetching.
    //
    // Threshold model: dashed horizontal lines at warn (Min) and critical (Max)
    // value levels. (Filled bands hid the line in the original design.)
    //
    // Playback support: ResetForPlaybackBackfill() wipes existing buckets so the
    // ViewItem can replay a range scan into the chart with cursor anchoring.
    internal sealed class LineChartRenderer
    {
        // Aim for this many buckets across the window — chart-render cost is
        // proportional to point count, so we cap at a stable budget regardless
        // of how long the user's window is.
        private const int TargetBucketCount = 300;

        private readonly Grid _root;
        private CartesianChart _chart;
        private ISeries _meanSeries;
        private LineSeries<DateTimePoint> _meanLine;
        private StepLineSeries<DateTimePoint> _meanStep;
        private LineSeries<DateTimePoint> _envelopeMin;
        private LineSeries<DateTimePoint> _envelopeMax;
        private readonly ObservableCollection<DateTimePoint> _meanPoints = new ObservableCollection<DateTimePoint>();
        private readonly ObservableCollection<DateTimePoint> _envMinPoints = new ObservableCollection<DateTimePoint>();
        private readonly ObservableCollection<DateTimePoint> _envMaxPoints = new ObservableCollection<DateTimePoint>();
        private readonly List<Bucket> _buckets = new List<Bucket>(TargetBucketCount * 2);
        private readonly Axis _xAxis;
        private readonly Axis _yAxis;

        private LineChartConfig _cfg;
        private LineChartType _activeType = LineChartType.Straight;
        private TimeSpan _bucketSize = TimeSpan.FromSeconds(1);

        private readonly RectangularSection _cursorSection;
        private DateTime? _cursorTime;

        private bool _paused;
        private readonly Border _pauseOverlay;
        private readonly TextBlock _pauseText;

        public LineChartRenderer()
        {
            _xAxis = new Axis
            {
                Labeler = ticks =>
                {
                    var t = new DateTime((long)ticks, DateTimeKind.Utc).ToLocalTime();
                    // Use a wider label format when the visible span is > 1h so we
                    // get HH:mm-only ticks instead of HH:mm:ss every label.
                    var span = TimeSpan.FromTicks((long)((_xAxis?.MaxLimit ?? 0) - (_xAxis?.MinLimit ?? 0)));
                    return span.TotalHours >= 1 ? t.ToString("HH:mm") : t.ToString("HH:mm:ss");
                },
                LabelsPaint = new SolidColorPaint(SKColors.Gray),
                TextSize = 10,
                MinStep = TimeSpan.FromSeconds(1).Ticks,
                SeparatorsPaint = new SolidColorPaint(new SKColor(0x33, 0x3B, 0x40)) { StrokeThickness = 0.5f },
            };

            _yAxis = new Axis
            {
                LabelsPaint = new SolidColorPaint(SKColors.Gray),
                TextSize = 10,
                SeparatorsPaint = new SolidColorPaint(new SKColor(0x33, 0x3B, 0x40)) { StrokeThickness = 0.5f },
            };

            _cursorSection = new RectangularSection
            {
                Stroke = new SolidColorPaint(SKColors.White) { StrokeThickness = 1.5f },
                Fill = null,
            };

            BuildSeries(LineChartType.Straight);
            BuildEnvelopeSeries();
            BuildChart();

            _pauseText = new TextBlock
            {
                Text = "Paused (click to resume live)",
                Foreground = new SolidColorBrush(WpfColor.FromRgb(0xF5, 0xF7, 0xF8)),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(8, 3, 8, 3),
            };
            _pauseOverlay = new Border
            {
                Background = new SolidColorBrush(WpfColor.FromArgb(0xCC, 0x1C, 0x23, 0x26)),
                BorderBrush = new SolidColorBrush(WpfColor.FromRgb(0xE6, 0x95, 0x00)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(2),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 6, 0, 0),
                Visibility = Visibility.Collapsed,
                Cursor = Cursors.Hand,
                Child = _pauseText,
            };
            _pauseOverlay.MouseLeftButtonUp += (s, e) => Resume();

            _root = new Grid();
            _root.Children.Add(_chart);
            _root.Children.Add(_pauseOverlay);
        }

        public UIElement Visual => _root;

        public void Configure(LineChartConfig cfg)
        {
            var prevWindow = _cfg?.WindowSeconds ?? -1;
            var prevAgg = _cfg?.Aggregation;
            _cfg = cfg;

            // Recompute bucket size from the window — keeps point count near the
            // TargetBucketCount budget so render cost stays flat.
            _bucketSize = ChooseBucketSize(cfg.WindowSeconds);

            // If the window length changed, the existing buckets are the wrong
            // width. Drop them and start fresh — re-bucketing the live raw stream
            // isn't possible because we never kept the raw samples.
            if (prevWindow > 0 && prevWindow != cfg.WindowSeconds)
            {
                _buckets.Clear();
            }

            if (cfg.LineType != _activeType)
            {
                BuildSeries(cfg.LineType);
                BuildChart();
                _root.Children.Clear();
                _root.Children.Add(_chart);
                _root.Children.Add(_pauseOverlay);
            }

            // Mode-flip cleanup: never let the pause overlay linger into playback.
            if (cfg.PlaybackMode && _paused) Resume();
            if (cfg.PlaybackMode) _pauseOverlay.Visibility = Visibility.Collapsed;
            else if (prevAgg.HasValue && prevAgg.Value != cfg.Aggregation)
            {
                // Aggregation switched — repopulate the displayed points from the
                // existing bucket aggregates without re-bucketing.
                RebuildSeriesFromBuckets();
            }
            ApplyConfig();
        }

        // Add a single sample (used by live streaming).
        public void AddSample(double value, DateTime utc)
        {
            if (_cfg == null) return;
            if (utc.Kind != DateTimeKind.Utc) utc = utc.ToUniversalTime();
            BucketAppend(value, utc);
            PruneAndRescale();
        }

        // Bulk-load samples from a playback range scan. Wipes existing buckets so
        // the chart shows exactly the requested range and nothing carried over
        // from a previous live session.
        public void ResetWithSamples(IReadOnlyList<(double Value, DateTime Utc)> samples)
        {
            if (_cfg == null) return;
            _buckets.Clear();
            foreach (var s in samples)
            {
                var u = s.Utc;
                if (u.Kind != DateTimeKind.Utc) u = u.ToUniversalTime();
                BucketAppend(s.Value, u);
            }
            RebuildSeriesFromBuckets();
            PruneAndRescale();
        }

        public void SetCursor(DateTime? utc)
        {
            _cursorTime = utc;
            if (_cfg != null) PruneAndRescale();
        }

        public void Clear()
        {
            _buckets.Clear();
            RebuildSeriesFromBuckets();
            _cursorTime = null;
            ApplySections();
        }

        public void Pause()
        {
            if (_paused) return;
            _paused = true;
            _pauseOverlay.Visibility = Visibility.Visible;
        }

        public void Resume()
        {
            if (!_paused) return;
            _paused = false;
            _pauseOverlay.Visibility = Visibility.Collapsed;
            PruneAndRescale();
        }

        private static TimeSpan ChooseBucketSize(int windowSeconds)
        {
            // Floor at 1s — the metadata cadence is rarely below that and going
            // sub-second creates lots of zero-count buckets.
            double secs = Math.Max(1.0, (double)windowSeconds / TargetBucketCount);
            // Snap to humane bucket sizes so axis labels / grid lines align nicely.
            double[] snaps = { 1, 2, 5, 10, 15, 30, 60, 120, 300, 600, 900, 1800, 3600, 7200, 21600, 43200, 86400 };
            foreach (var s in snaps)
            {
                if (secs <= s) return TimeSpan.FromSeconds(s);
            }
            return TimeSpan.FromSeconds(86400);
        }

        private DateTime BucketStart(DateTime utc)
        {
            var ticks = _bucketSize.Ticks;
            return new DateTime((utc.Ticks / ticks) * ticks, DateTimeKind.Utc);
        }

        private void BucketAppend(double value, DateTime utc)
        {
            var start = BucketStart(utc);
            Bucket b = null;
            if (_buckets.Count > 0 && _buckets[_buckets.Count - 1].StartUtc == start)
                b = _buckets[_buckets.Count - 1];
            else if (_buckets.Count > 0 && _buckets[_buckets.Count - 1].StartUtc > start)
            {
                // Out-of-order sample (rare) — find or insert.
                int idx = _buckets.FindLastIndex(x => x.StartUtc <= start);
                if (idx >= 0 && _buckets[idx].StartUtc == start) b = _buckets[idx];
                else
                {
                    b = new Bucket { StartUtc = start };
                    _buckets.Insert(idx + 1, b);
                }
            }
            else
            {
                b = new Bucket { StartUtc = start };
                _buckets.Add(b);
            }

            b.Add(value);

            // Update series points incrementally instead of rebuilding everything
            // for every new sample. Last-bucket-grow is the common case.
            UpdatePointForBucket(b, _buckets.Count - 1);
        }

        private void UpdatePointForBucket(Bucket b, int bucketIndex)
        {
            if (_cfg == null) return;
            var meanPoint = new DateTimePoint(b.StartUtc, b.Pick(_cfg.Aggregation));
            // The series point at bucketIndex is either present (update) or new (add).
            if (bucketIndex < _meanPoints.Count) _meanPoints[bucketIndex] = meanPoint;
            else                                  _meanPoints.Add(meanPoint);

            if (_cfg.EnvelopeEnabled)
            {
                var minPt = new DateTimePoint(b.StartUtc, b.Count > 0 ? b.Min : 0);
                var maxPt = new DateTimePoint(b.StartUtc, b.Count > 0 ? b.Max : 0);
                if (bucketIndex < _envMinPoints.Count) _envMinPoints[bucketIndex] = minPt; else _envMinPoints.Add(minPt);
                if (bucketIndex < _envMaxPoints.Count) _envMaxPoints[bucketIndex] = maxPt; else _envMaxPoints.Add(maxPt);
            }
        }

        private void RebuildSeriesFromBuckets()
        {
            _meanPoints.Clear();
            _envMinPoints.Clear();
            _envMaxPoints.Clear();
            if (_cfg == null) return;
            for (int i = 0; i < _buckets.Count; i++)
                UpdatePointForBucket(_buckets[i], i);
        }

        private void BuildSeries(LineChartType type)
        {
            _activeType = type;
            if (type == LineChartType.Step)
            {
                _meanStep = new StepLineSeries<DateTimePoint>
                {
                    Values = _meanPoints,
                    YToolTipLabelFormatter = FormatTooltip,
                };
                _meanLine = null;
                _meanSeries = _meanStep;
            }
            else
            {
                _meanLine = new LineSeries<DateTimePoint>
                {
                    Values = _meanPoints,
                    YToolTipLabelFormatter = FormatTooltip,
                };
                _meanStep = null;
                _meanSeries = _meanLine;
            }
        }

        private void BuildEnvelopeSeries()
        {
            _envelopeMin = new LineSeries<DateTimePoint>
            {
                Values = _envMinPoints,
                Fill = null,
                GeometrySize = 0,
                LineSmoothness = 0,
            };
            _envelopeMax = new LineSeries<DateTimePoint>
            {
                Values = _envMaxPoints,
                Fill = null,
                GeometrySize = 0,
                LineSmoothness = 0,
            };
        }

        private void BuildChart()
        {
            var seriesList = new List<ISeries>(3);
            if (_cfg != null && _cfg.EnvelopeEnabled)
            {
                seriesList.Add(_envelopeMin);
                seriesList.Add(_envelopeMax);
            }
            seriesList.Add(_meanSeries);

            _chart = new CartesianChart
            {
                Series = seriesList,
                XAxes = new[] { _xAxis },
                YAxes = new[] { _yAxis },
                Sections = new RectangularSection[0],
                Background = System.Windows.Media.Brushes.Transparent,
                LegendPosition = LegendPosition.Hidden,
                TooltipPosition = TooltipPosition.Top,
                TooltipBackgroundPaint = new SolidColorPaint(new SKColor(0x2A, 0x32, 0x36, 0xF2)),
                TooltipTextPaint = new SolidColorPaint(new SKColor(0xE6, 0xEA, 0xEC)),
                TooltipTextSize = 12,
                AnimationsSpeed = TimeSpan.FromMilliseconds(0),
                MinHeight = 80,
                MinWidth = 120,
            };
            // Auto-pause on user interaction is only meaningful in live mode —
            // in playback the cursor anchors the visible window, so the "Paused
            // (click to resume live)" overlay would be misleading.
            _chart.PreviewMouseWheel += (s, e) =>
            {
                if (_cfg != null && _cfg.ZoomEnabled && !_cfg.PlaybackMode) Pause();
            };
            _chart.PreviewMouseDown  += (s, e) =>
            {
                if (_cfg == null || !_cfg.ZoomEnabled || _cfg.PlaybackMode) return;
                if (e.ChangedButton == MouseButton.Left || e.ChangedButton == MouseButton.Middle) Pause();
            };
        }

        private static string FormatTooltip(LiveChartsCore.Kernel.ChartPoint point)
            => point.Coordinate.PrimaryValue.ToString("0.##", CultureInfo.InvariantCulture);

        private void ApplyConfig()
        {
            var sk = ToSk(_cfg.LineColor);
            var stroke = new SolidColorPaint(sk) { StrokeThickness = (float)_cfg.LineThickness };
            var fill = _cfg.FillEnabled
                ? new SolidColorPaint(new SKColor(sk.Red, sk.Green, sk.Blue, 0x40))
                : null;

            if (_meanLine != null)
            {
                _meanLine.Stroke = stroke;
                _meanLine.Fill = fill;
                _meanLine.LineSmoothness = _cfg.LineType == LineChartType.Smooth ? 0.65 : 0;
                _meanLine.GeometrySize = _cfg.ShowMarker ? 4 : 0;
                _meanLine.GeometryStroke = new SolidColorPaint(sk) { StrokeThickness = 1.5f };
                _meanLine.GeometryFill = new SolidColorPaint(sk);
            }
            if (_meanStep != null)
            {
                _meanStep.Stroke = stroke;
                _meanStep.Fill = fill;
                _meanStep.GeometrySize = _cfg.ShowMarker ? 4 : 0;
                _meanStep.GeometryStroke = new SolidColorPaint(sk) { StrokeThickness = 1.5f };
                _meanStep.GeometryFill = new SolidColorPaint(sk);
            }

            // Envelope lines — same hue, low opacity, thin stroke. Visible only
            // when EnvelopeEnabled AND the series is present in the chart.
            var envStroke = new SolidColorPaint(new SKColor(sk.Red, sk.Green, sk.Blue, 0x80))
            {
                StrokeThickness = 1f,
                PathEffect = new DashEffect(new float[] { 3, 3 }),
            };
            _envelopeMin.Stroke = envStroke;
            _envelopeMax.Stroke = envStroke;

            // The envelope series are listed in the chart only when enabled, so
            // toggling the flag means rebuilding the chart's Series list.
            EnsureChartSeriesMatchesEnvelopeFlag();

            _yAxis.MinLimit = _cfg.YMin;
            _yAxis.MaxLimit = _cfg.YMax;
            _chart.ZoomMode = _cfg.ZoomEnabled ? ZoomAndPanMode.X : ZoomAndPanMode.None;

            // Aggregation may have changed independently of LineType — re-emit
            // displayed points from the cached buckets so the user sees the
            // change without waiting for fresh samples.
            RebuildSeriesFromBuckets();

            ApplySections();
            PruneAndRescale();
        }

        private void EnsureChartSeriesMatchesEnvelopeFlag()
        {
            bool wantEnv = _cfg != null && _cfg.EnvelopeEnabled;
            // Cheaper to just rebuild the Series array — these are tiny lists.
            var list = new List<ISeries>(3);
            if (wantEnv)
            {
                list.Add(_envelopeMin);
                list.Add(_envelopeMax);
            }
            list.Add(_meanSeries);
            _chart.Series = list;
        }

        private void ApplySections()
        {
            var sections = new List<RectangularSection>();
            if (_cfg != null && _cfg.Numeric != null && _cfg.Numeric.Enabled)
            {
                var n = _cfg.Numeric;
                if (n.Min.HasValue) sections.Add(MakeThresholdLine(n.Min.Value, n.HighIsBad ? n.ColorWarn : n.ColorBad));
                if (n.Max.HasValue) sections.Add(MakeThresholdLine(n.Max.Value, n.HighIsBad ? n.ColorBad : n.ColorWarn));
            }
            if (_cursorTime.HasValue)
            {
                _cursorSection.Xi = _cursorTime.Value.Ticks;
                _cursorSection.Xj = _cursorTime.Value.Ticks;
                sections.Add(_cursorSection);
            }
            _chart.Sections = sections;
        }

        private static RectangularSection MakeThresholdLine(double y, WpfColor color)
        {
            var sk = ToSk(color);
            return new RectangularSection
            {
                Yi = y,
                Yj = y,
                Stroke = new SolidColorPaint(sk)
                {
                    StrokeThickness = 1.4f,
                    PathEffect = new DashEffect(new float[] { 6, 4 }),
                },
                Fill = null,
            };
        }

        private void PruneAndRescale()
        {
            if (_buckets.Count == 0)
            {
                _xAxis.MinLimit = null;
                _xAxis.MaxLimit = null;
                ApplySections();
                return;
            }
            if (_paused)
            {
                ApplySections();
                return;
            }

            var latest = _cursorTime ?? _buckets[_buckets.Count - 1].StartUtc + _bucketSize;
            var cutoff = latest.AddSeconds(-_cfg.WindowSeconds);

            // Drop stale buckets and their points.
            int drop = 0;
            while (drop < _buckets.Count && _buckets[drop].StartUtc + _bucketSize < cutoff) drop++;
            if (drop > 0)
            {
                _buckets.RemoveRange(0, drop);
                for (int i = 0; i < drop && _meanPoints.Count > 0; i++) _meanPoints.RemoveAt(0);
                if (_cfg != null && _cfg.EnvelopeEnabled)
                {
                    for (int i = 0; i < drop && _envMinPoints.Count > 0; i++) _envMinPoints.RemoveAt(0);
                    for (int i = 0; i < drop && _envMaxPoints.Count > 0; i++) _envMaxPoints.RemoveAt(0);
                }
            }

            _xAxis.MinLimit = cutoff.Ticks;
            _xAxis.MaxLimit = latest.Ticks;
            ApplySections();
        }

        private static SKColor ToSk(WpfColor c) => new SKColor(c.R, c.G, c.B, c.A);
    }
}
