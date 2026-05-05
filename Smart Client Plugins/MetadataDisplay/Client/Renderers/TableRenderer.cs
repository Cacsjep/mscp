using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using WpfColor = System.Windows.Media.Color;

namespace MetadataDisplay.Client.Renderers
{
    internal sealed class TableConfig
    {
        public int MaxRows;
        public int WindowSeconds;
        public bool ShowTimestamp;
        public string TimestampFormat;
        public bool ShowDelta;
        public double FontSize;
        public TextAlignment ValueAlignment;
        public string ValueColumnName;
        public bool PlaybackMode;
        public NumericConfig Numeric;

        public static TableConfig FromManager(MetadataDisplayViewItemManager m)
        {
            int maxRows = 200;
            if (int.TryParse(m.TableMaxRows, NumberStyles.Integer, CultureInfo.InvariantCulture, out var mr) && mr > 0)
                maxRows = Math.Min(mr, 5000);

            int win = 300;
            if (int.TryParse(m.TableWindowSeconds, NumberStyles.Integer, CultureInfo.InvariantCulture, out var w) && w > 0)
                win = w;

            double fs = 12;
            if (double.TryParse(m.TableFontSize, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) && f > 0)
                fs = f;

            return new TableConfig
            {
                MaxRows = maxRows,
                WindowSeconds = win,
                ShowTimestamp = !string.Equals(m.TableShowTimestamp, "false", StringComparison.OrdinalIgnoreCase),
                TimestampFormat = string.IsNullOrEmpty(m.TableTimestampFormat) ? "HH:mm:ss" : m.TableTimestampFormat,
                ShowDelta = string.Equals(m.TableShowDelta, "true", StringComparison.OrdinalIgnoreCase),
                FontSize = fs,
                ValueAlignment = ParseAlign(m.TableValueAlignment),
                ValueColumnName = m.TableValueColumnName ?? string.Empty,
                Numeric = NumericConfig.FromManager(m),
            };
        }

        private static TextAlignment ParseAlign(string s)
        {
            if (string.Equals(s, "Right", StringComparison.OrdinalIgnoreCase)) return TextAlignment.Right;
            if (string.Equals(s, "Center", StringComparison.OrdinalIgnoreCase)) return TextAlignment.Center;
            return TextAlignment.Left;
        }
    }

    internal sealed class TableRow
    {
        public DateTime TimestampUtc;
        public string Value;
    }

    // Time-ordered scrolling table. Newest row is rendered at the top so the
    // operator's eye lands on the latest value first; older rows scroll off the
    // bottom. State model:
    //   - List of rows ordered newest-first; capped by MaxRows AND WindowSeconds
    //     (whichever cuts harder — oldest get dropped).
    //   - AddSample inserts at position 0; auto-follow keeps the scroll viewport
    //     pinned to the top unless the user has scrolled down to inspect older
    //     rows (Pause overlay matches LineChart's pattern).
    //   - ResetWithSamples bulk-loads from a backfill scan; SetCursor highlights
    //     the row at-or-before the playback cursor.
    internal sealed class TableRenderer
    {
        private readonly Grid _root;
        private readonly Grid _headerGrid;
        private readonly TextBlock _hdrTime;
        private readonly TextBlock _hdrValue;
        private readonly TextBlock _hdrDelta;
        private readonly ColumnDefinition _colTime;
        private readonly ColumnDefinition _colDelta;
        private readonly ScrollViewer _scroller;
        private readonly StackPanel _rowsPanel;
        private readonly List<(TableRow Row, Grid Visual)> _rows = new List<(TableRow, Grid)>();
        private readonly Border _pauseOverlay;
        private TableConfig _cfg;
        private DateTime? _cursorUtc;
        private bool _autoFollow = true;
        // Suppresses scroll-driven Pause/Resume while we're programmatically
        // rebuilding visuals (Configure / ResetWithSamples) — clearing the panel
        // briefly puts the scroll position at non-top, which would otherwise
        // trip the auto-pause heuristic.
        private bool _suppressScrollEvents;

        public TableRenderer()
        {
            _colTime = new ColumnDefinition { Width = new GridLength(110) };
            var colVal = new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) };
            _colDelta = new ColumnDefinition { Width = new GridLength(0) };

            _hdrTime = new TextBlock
            {
                Text = "Time",
                Foreground = new SolidColorBrush(WidgetTheme.SubtleColor),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(8, 4, 8, 4),
                VerticalAlignment = VerticalAlignment.Center,
            };
            _hdrValue = new TextBlock
            {
                Text = "Value",
                Foreground = new SolidColorBrush(WidgetTheme.SubtleColor),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(8, 4, 8, 4),
                VerticalAlignment = VerticalAlignment.Center,
            };
            _hdrDelta = new TextBlock
            {
                Text = "Δ",
                Foreground = new SolidColorBrush(WidgetTheme.SubtleColor),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(8, 4, 8, 4),
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = Visibility.Collapsed,
            };
            Grid.SetColumn(_hdrTime, 0);
            Grid.SetColumn(_hdrValue, 1);
            Grid.SetColumn(_hdrDelta, 2);

            _headerGrid = new Grid { Background = new SolidColorBrush(WpfColor.FromRgb(0x22, 0x2A, 0x2D)) };
            _headerGrid.ColumnDefinitions.Add(_colTime);
            _headerGrid.ColumnDefinitions.Add(colVal);
            _headerGrid.ColumnDefinitions.Add(_colDelta);
            _headerGrid.Children.Add(_hdrTime);
            _headerGrid.Children.Add(_hdrValue);
            _headerGrid.Children.Add(_hdrDelta);

            var headerHost = new Border
            {
                BorderBrush = new SolidColorBrush(WpfColor.FromRgb(0x33, 0x3B, 0x40)),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Child = _headerGrid,
            };
            Grid.SetRow(headerHost, 0);

            _rowsPanel = new StackPanel { Orientation = Orientation.Vertical };
            _scroller = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = _rowsPanel,
                Background = Brushes.Transparent,
            };
            _scroller.ScrollChanged += OnScrollChanged;
            Grid.SetRow(_scroller, 1);

            _root = new Grid();
            _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            _root.Children.Add(headerHost);
            _root.Children.Add(_scroller);

            var pauseText = new TextBlock
            {
                Text = "Paused (click to jump back to newest)",
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
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 0, 8),
                Visibility = Visibility.Collapsed,
                Cursor = Cursors.Hand,
                Child = pauseText,
            };
            Grid.SetRow(_pauseOverlay, 1);
            _pauseOverlay.MouseLeftButtonUp += (s, e) => Resume();
            _root.Children.Add(_pauseOverlay);
        }

        public UIElement Visual => _root;

        public void Configure(TableConfig cfg)
        {
            _cfg = cfg;
            _colTime.Width = cfg.ShowTimestamp ? new GridLength(110) : new GridLength(0);
            _colDelta.Width = cfg.ShowDelta ? new GridLength(80) : new GridLength(0);
            _hdrTime.Visibility = cfg.ShowTimestamp ? Visibility.Visible : Visibility.Collapsed;
            _hdrDelta.Visibility = cfg.ShowDelta ? Visibility.Visible : Visibility.Collapsed;
            _hdrValue.Text = string.IsNullOrWhiteSpace(cfg.ValueColumnName) ? "Value" : cfg.ValueColumnName;
            // Playback never auto-pauses — cursor anchors what's shown.
            if (cfg.PlaybackMode && !_autoFollow) _autoFollow = true;
            if (cfg.PlaybackMode) _pauseOverlay.Visibility = Visibility.Collapsed;
            RebuildAllVisuals();
            ScrollToTopIfFollowing();
        }

        public void AddSample(string value, DateTime utc)
        {
            if (_cfg == null) return;
            if (utc.Kind != DateTimeKind.Utc) utc = utc.ToUniversalTime();
            var row = new TableRow { TimestampUtc = utc, Value = value };
            _rows.Insert(0, (row, BuildRowVisual(row, 0)));
            _suppressScrollEvents = true;
            try { _rowsPanel.Children.Insert(0, _rows[0].Visual); }
            finally { _suppressScrollEvents = false; }
            // Inserting at the top shifts every subsequent row's parity, so
            // refresh their backgrounds. ApplyHighlight covers this since it
            // already iterates every row.
            PruneToLimits();
            ApplyHighlight();
            ScrollToTopIfFollowing();
        }

        public void ResetWithSamples(IReadOnlyList<(string Value, DateTime Utc)> samples)
        {
            if (_cfg == null) return;
            _suppressScrollEvents = true;
            try
            {
                _rows.Clear();
                _rowsPanel.Children.Clear();
                // Caller hands us samples in chronological (oldest-first) order;
                // we want newest at index 0, so iterate from the end.
                for (int i = samples.Count - 1; i >= 0; i--)
                {
                    var s = samples[i];
                    var u = s.Utc;
                    if (u.Kind != DateTimeKind.Utc) u = u.ToUniversalTime();
                    var row = new TableRow { TimestampUtc = u, Value = s.Value };
                    int newIndex = _rows.Count;
                    _rows.Add((row, BuildRowVisual(row, newIndex)));
                    _rowsPanel.Children.Add(_rows[newIndex].Visual);
                }
                PruneToLimits();
                ApplyHighlight();
                // After a bulk reset, force-follow regardless of prior pause state —
                // the buffer has been replaced.
                _autoFollow = true;
                _pauseOverlay.Visibility = Visibility.Collapsed;
            }
            finally { _suppressScrollEvents = false; }
            ScrollToTopIfFollowing();
        }

        public void SetCursor(DateTime? utc)
        {
            _cursorUtc = utc.HasValue && utc.Value.Kind != DateTimeKind.Utc
                ? utc.Value.ToUniversalTime()
                : utc;
            ApplyHighlight();
        }

        public void Clear()
        {
            _suppressScrollEvents = true;
            try
            {
                _rows.Clear();
                _rowsPanel.Children.Clear();
                _cursorUtc = null;
            }
            finally { _suppressScrollEvents = false; }
        }

        public void Pause()
        {
            if (_cfg != null && _cfg.PlaybackMode) return;
            _autoFollow = false;
            _pauseOverlay.Visibility = Visibility.Visible;
        }

        public void Resume()
        {
            _autoFollow = true;
            _pauseOverlay.Visibility = Visibility.Collapsed;
            ScrollToTopIfFollowing();
        }

        private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (_suppressScrollEvents) return;
            if (_cfg != null && _cfg.PlaybackMode) return;
            // Newest row sits at offset 0 — pause when the user scrolls down to
            // inspect older rows, resume when they're back at the top.
            const double topEpsilon = 4.0;
            bool atTop = e.VerticalOffset <= topEpsilon;
            if (!atTop && _autoFollow) Pause();
            else if (atTop && !_autoFollow) Resume();
        }

        private void PruneToLimits()
        {
            if (_cfg == null) return;
            // Window-second prune anchored on newest row's wall time. Rows are
            // sorted newest-first so older rows live at the END of the list.
            if (_rows.Count > 0 && _cfg.WindowSeconds > 0)
            {
                var newest = _rows[0].Row.TimestampUtc;
                var cutoff = newest.AddSeconds(-_cfg.WindowSeconds);
                int firstStale = _rows.Count;
                for (int i = _rows.Count - 1; i >= 0; i--)
                {
                    if (_rows[i].Row.TimestampUtc < cutoff) firstStale = i;
                    else break;
                }
                if (firstStale < _rows.Count)
                    RemoveTrailing(_rows.Count - firstStale);
            }
            // MaxRows hard cap — drop the oldest (tail) entries.
            int over = _rows.Count - _cfg.MaxRows;
            if (over > 0) RemoveTrailing(over);
        }

        private void RemoveTrailing(int n)
        {
            if (n <= 0) return;
            n = Math.Min(n, _rows.Count);
            int firstRemove = _rows.Count - n;
            _suppressScrollEvents = true;
            try
            {
                for (int i = 0; i < n; i++) _rowsPanel.Children.RemoveAt(firstRemove);
                _rows.RemoveRange(firstRemove, n);
            }
            finally { _suppressScrollEvents = false; }
        }

        private void RebuildAllVisuals()
        {
            _suppressScrollEvents = true;
            try
            {
                _rowsPanel.Children.Clear();
                for (int i = 0; i < _rows.Count; i++)
                {
                    var v = BuildRowVisual(_rows[i].Row, i);
                    _rows[i] = (_rows[i].Row, v);
                    _rowsPanel.Children.Add(v);
                }
            }
            finally { _suppressScrollEvents = false; }
        }

        private Grid BuildRowVisual(TableRow row, int index)
        {
            var g = new Grid
            {
                Background = (index & 1) == 0
                    ? Brushes.Transparent
                    : (Brush)new SolidColorBrush(WpfColor.FromRgb(0x1F, 0x26, 0x29)),
            };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = _colTime.Width });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = _colDelta.Width });

            var fs = _cfg != null ? _cfg.FontSize : 12;

            var timeTb = new TextBlock
            {
                Text = _cfg != null && _cfg.ShowTimestamp
                    ? row.TimestampUtc.ToLocalTime().ToString(_cfg.TimestampFormat, CultureInfo.InvariantCulture)
                    : "",
                Foreground = new SolidColorBrush(WidgetTheme.SubtleColor),
                FontSize = fs,
                Margin = new Thickness(8, 2, 8, 2),
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(timeTb, 0);
            g.Children.Add(timeTb);

            var valTb = new TextBlock
            {
                Text = string.IsNullOrEmpty(row.Value) ? "—" : row.Value,
                Foreground = new SolidColorBrush(GetValueColor(row.Value)),
                FontSize = fs,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(8, 2, 8, 2),
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = _cfg != null ? _cfg.ValueAlignment : TextAlignment.Left,
            };
            Grid.SetColumn(valTb, 1);
            g.Children.Add(valTb);

            if (_cfg != null && _cfg.ShowDelta)
            {
                var deltaTb = new TextBlock
                {
                    Text = ComputeDelta(index),
                    Foreground = new SolidColorBrush(WidgetTheme.DimColor),
                    FontSize = fs,
                    Margin = new Thickness(8, 2, 8, 2),
                    VerticalAlignment = VerticalAlignment.Center,
                };
                Grid.SetColumn(deltaTb, 2);
                g.Children.Add(deltaTb);
            }

            return g;
        }

        // Newest row sits at index 0; the row "before" it in time lives at index+1.
        // Returns "" for the oldest row (no predecessor) or whenever either value
        // can't be parsed as a number — text-based metadata simply yields a blank
        // delta column instead of crashing or showing junk.
        private string ComputeDelta(int index)
        {
            if (index < 0 || index >= _rows.Count - 1) return "";
            if (!double.TryParse(_rows[index].Row.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var cur))
                return "";
            if (!double.TryParse(_rows[index + 1].Row.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var prev))
                return "";
            var d = cur - prev;
            if (Math.Abs(d) < 1e-9) return "0";
            string sign = d > 0 ? "+" : "";
            return sign + (d == (long)d
                ? ((long)d).ToString(CultureInfo.InvariantCulture)
                : d.ToString("0.##", CultureInfo.InvariantCulture));
        }

        private WpfColor GetValueColor(string raw)
        {
            if (_cfg == null || _cfg.Numeric == null || !_cfg.Numeric.Enabled) return WidgetTheme.ValueColor;
            if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                return WidgetTheme.ValueColor;
            return _cfg.Numeric.PickColor(v);
        }

        private void ApplyHighlight()
        {
            if (_rows.Count == 0) return;
            int hl = -1;
            if (_cursorUtc.HasValue)
            {
                // Rows are newest-first, so the first row with TS <= cursor is the
                // at-or-before match (the most recent sample not yet in the future).
                for (int i = 0; i < _rows.Count; i++)
                {
                    if (_rows[i].Row.TimestampUtc <= _cursorUtc.Value) { hl = i; break; }
                }
            }
            for (int i = 0; i < _rows.Count; i++)
            {
                var bg = (i & 1) == 0
                    ? (Brush)Brushes.Transparent
                    : new SolidColorBrush(WpfColor.FromRgb(0x1F, 0x26, 0x29));
                if (i == hl)
                    bg = new SolidColorBrush(WpfColor.FromArgb(0x55, 0x4F, 0xC3, 0xF7));
                _rows[i].Visual.Background = bg;
            }
        }

        private void ScrollToTopIfFollowing()
        {
            if (!_autoFollow) return;
            _suppressScrollEvents = true;
            try { _scroller.ScrollToTop(); }
            finally { _suppressScrollEvents = false; }
        }
    }
}
