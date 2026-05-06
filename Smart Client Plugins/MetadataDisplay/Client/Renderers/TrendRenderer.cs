using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;
using FontAwesome5;

namespace MetadataDisplay.Client.Renderers
{
    // How the Trend KPI computes its Δ% baseline. The baseline is always
    // fetched from the archive at the historical anchor time, with a window
    // around the anchor defined by the active LookbackSeconds (driven by the
    // in-pane window picker).
    //   SameTimeYesterday  - 24h earlier
    //   SameTimeLastWeek   - 7d earlier
    //   SameTimeLastMonth  - same calendar day-of-month one month earlier
    internal enum TrendComparisonMode
    {
        SameTimeYesterday = 0,
        SameTimeLastWeek = 1,
        SameTimeLastMonth = 2,
    }

    // Trend / KPI tile config. Big current value, Δ% versus an archive
    // baseline (yesterday / last week / last month), arrow indicator, and
    // a sparkline. Reuses NumericConfig for thresholds + unit; Direction
    // (HighIsBad) drives the arrow color so a "high is good" KPI shows
    // green-up / red-down and vice versa. PlaybackMode hints the renderer
    // to anchor at the cursor instead of "now".
    //
    // LookbackSeconds drives both the sparkline width and the averaging
    // window around the historical anchor (archive scan = ±LookbackSeconds
    // around the anchor). The in-pane window picker overrides this in
    // live / playback so the operator can re-frame both at once.
    internal sealed class TrendConfig
    {
        public int LookbackSeconds = 300;       // archive averaging window for the comparison anchor
        public bool ShowDelta = true;
        public bool ShowArrow = true;
        public double ValueFontSize = 48;
        public NumericConfig Numeric = new NumericConfig();
        public bool PlaybackMode;
        public TrendComparisonMode ComparisonMode = TrendComparisonMode.SameTimeYesterday;

        public static TrendConfig FromManager(MetadataDisplayViewItemManager m)
        {
            int lookback = 300;
            if (int.TryParse(m.TrendLookbackSeconds, NumberStyles.Integer, CultureInfo.InvariantCulture, out var lb) && lb > 0)
                lookback = lb;
            double fs = 48;
            if (double.TryParse(m.TrendValueFontSize, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) && f > 0)
                fs = f;
            return new TrendConfig
            {
                LookbackSeconds = lookback,
                ShowDelta = !string.Equals(m.TrendShowDelta, "false", StringComparison.OrdinalIgnoreCase),
                ShowArrow = !string.Equals(m.TrendShowArrow, "false", StringComparison.OrdinalIgnoreCase),
                ValueFontSize = fs,
                Numeric = NumericConfig.FromManager(m),
                ComparisonMode = ParseComparisonMode(m.TrendComparisonMode),
            };
        }

        public static TrendComparisonMode ParseComparisonMode(string s)
        {
            if (string.IsNullOrEmpty(s)) return TrendComparisonMode.SameTimeYesterday;
            switch (s.Trim())
            {
                case "Yesterday": return TrendComparisonMode.SameTimeYesterday;
                case "LastWeek":  return TrendComparisonMode.SameTimeLastWeek;
                case "LastMonth": return TrendComparisonMode.SameTimeLastMonth;
                default:          return TrendComparisonMode.SameTimeYesterday;
            }
        }
    }

    // KPI tile renderer. Shows the current value plus a Δ% (with arrow)
    // versus an archive-fetched baseline at a historical anchor. Keeps a
    // rolling sample buffer for the current value only (the latest sample
    // wins); no sparkline is drawn.
    internal sealed class TrendRenderer
    {
        private readonly Grid _root;
        private readonly TextBlock _valueRow;
        private readonly Run _valueRun;
        private readonly Run _unitRun;
        private readonly StackPanel _deltaRow;
        private readonly ImageAwesome _arrowIcon;
        private readonly TextBlock _deltaText;
        private readonly TextBlock _windowText;

        private readonly List<(DateTime Utc, double Value)> _samples = new List<(DateTime, double)>(512);
        private TrendConfig _cfg;
        private DateTime? _cursorUtc;
        // Externally-supplied baseline for the comparison mode (yesterday,
        // last week, last month). The host scans the archive around the
        // target time and pushes the average value here. Null until the
        // first scan completes.
        private double? _externalBaseline;
        // Time window the host scanned for the baseline. Surfaced in the UI
        // when the scan returns no samples so the operator can see exactly
        // which historical period is empty.
        private DateTime? _comparisonWindowFromUtc;
        private DateTime? _comparisonWindowToUtc;

        public TrendRenderer()
        {
            _valueRun = new Run
            {
                Text = "-",
                Foreground = new SolidColorBrush(WidgetTheme.ValueColor),
                FontSize = 48,
                FontWeight = FontWeights.SemiBold,
            };
            _unitRun = new Run
            {
                Text = "",
                Foreground = new SolidColorBrush(WidgetTheme.UnitColor),
                FontSize = 18,
            };
            _valueRow = new TextBlock
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
            };
            _valueRow.Inlines.Add(_valueRun);
            _valueRow.Inlines.Add(new Run(" ") { FontSize = 18 });
            _valueRow.Inlines.Add(_unitRun);

            _arrowIcon = new ImageAwesome
            {
                Icon = EFontAwesomeIcon.Solid_Minus,
                Width = 14,
                Height = 14,
                Margin = new Thickness(0, 0, 4, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(WidgetTheme.UnitColor),
            };
            _deltaText = new TextBlock
            {
                Text = "",
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(WidgetTheme.UnitColor),
                VerticalAlignment = VerticalAlignment.Center,
            };
            _deltaRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 4, 0, 0),
            };
            _deltaRow.Children.Add(_arrowIcon);
            _deltaRow.Children.Add(_deltaText);

            _windowText = new TextBlock
            {
                Text = "",
                FontSize = 11,
                Foreground = new SolidColorBrush(WidgetTheme.UnitColor),
                Opacity = 0.85,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 2, 0, 0),
                Visibility = Visibility.Collapsed,
            };

            var stack = new StackPanel
            {
                Orientation = Orientation.Vertical,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            stack.Children.Add(_valueRow);
            stack.Children.Add(_deltaRow);
            stack.Children.Add(_windowText);

            _root = new Grid();
            _root.Children.Add(stack);
        }

        public UIElement Visual => _root;

        public void Configure(TrendConfig cfg)
        {
            var prevMode = _cfg?.ComparisonMode;
            _cfg = cfg ?? new TrendConfig();
            _valueRun.FontSize = _cfg.ValueFontSize;
            _unitRun.FontSize = Math.Max(10, _cfg.ValueFontSize * 0.38);
            _unitRun.Text = _cfg.Numeric?.Unit ?? "";
            _deltaRow.Visibility = _cfg.ShowDelta || _cfg.ShowArrow ? Visibility.Visible : Visibility.Collapsed;
            // If the operator switched comparison mode, the previous baseline
            // applies to a different anchor period and would be misleading.
            if (prevMode.HasValue && prevMode.Value != _cfg.ComparisonMode)
                _externalBaseline = null;
            PruneAndRefresh();
        }

        public void AddSample(double value, DateTime utc)
        {
            if (_cfg == null) return;
            if (utc.Kind != DateTimeKind.Utc) utc = utc.ToUniversalTime();
            // Insert in chronological order - backfill can interleave.
            if (_samples.Count == 0 || utc >= _samples[_samples.Count - 1].Utc)
                _samples.Add((utc, value));
            else
            {
                int i = _samples.Count - 1;
                while (i >= 0 && _samples[i].Utc > utc) i--;
                _samples.Insert(i + 1, (utc, value));
            }
            PruneAndRefresh();
        }

        public void ResetWithSamples(IReadOnlyList<(double Value, DateTime Utc)> samples)
        {
            _samples.Clear();
            if (samples != null)
            {
                foreach (var s in samples)
                {
                    var u = s.Utc;
                    if (u.Kind != DateTimeKind.Utc) u = u.ToUniversalTime();
                    _samples.Add((u, s.Value));
                }
            }
            _samples.Sort((a, b) => a.Utc.Ticks.CompareTo(b.Utc.Ticks));
            PruneAndRefresh();
        }

        public void SetCursor(DateTime? utc)
        {
            _cursorUtc = utc;
            PruneAndRefresh();
        }

        // Push the comparison baseline value fetched from the archive. Called
        // by the host (the ViewItem control) every time the comparison mode's
        // anchor period is scanned. Pass null to clear (e.g. archive scan
        // returned no samples in the comparison window).
        public void SetComparisonBaseline(double? value)
        {
            SetComparisonBaseline(value, null, null);
        }

        // Variant that also records the UTC window the host scanned, so the
        // renderer can surface it in the "no data to compare" state.
        public void SetComparisonBaseline(double? value, DateTime? windowFromUtc, DateTime? windowToUtc)
        {
            _externalBaseline = value;
            _comparisonWindowFromUtc = windowFromUtc;
            _comparisonWindowToUtc = windowToUtc;
            PruneAndRefresh();
        }

        // Pre-populates the window the host is about to scan, before the scan
        // returns. Lets the operator see which historical period is being
        // checked even during the brief wait before SetComparisonBaseline
        // arrives, and stays visible if that scan ultimately returns null.
        public void SetComparisonWindow(DateTime? windowFromUtc, DateTime? windowToUtc)
        {
            _comparisonWindowFromUtc = windowFromUtc;
            _comparisonWindowToUtc = windowToUtc;
            PruneAndRefresh();
        }

        public void Clear()
        {
            _samples.Clear();
            _externalBaseline = null;
            _comparisonWindowFromUtc = null;
            _comparisonWindowToUtc = null;
            _valueRun.Text = "-";
            _valueRun.Foreground = new SolidColorBrush(WidgetTheme.ValueColor);
            _deltaText.Text = "";
            _arrowIcon.Icon = EFontAwesomeIcon.Solid_Minus;
            _arrowIcon.Foreground = new SolidColorBrush(WidgetTheme.UnitColor);
            if (_windowText != null)
            {
                _windowText.Text = "";
                _windowText.Visibility = Visibility.Collapsed;
            }
        }

        private DateTime AnchorUtc()
        {
            if (_cfg != null && _cfg.PlaybackMode && _cursorUtc.HasValue) return _cursorUtc.Value;
            return DateTime.UtcNow;
        }

        private void PruneAndRefresh()
        {
            if (_cfg == null) return;
            var anchor = AnchorUtc();
            var cutoff = anchor.AddSeconds(-_cfg.LookbackSeconds);

            // Drop samples older than the sparkline window.
            int drop = 0;
            while (drop < _samples.Count && _samples[drop].Utc < cutoff) drop++;
            if (drop > 0) _samples.RemoveRange(0, drop);

            if (_samples.Count == 0)
            {
                Clear();
                return;
            }

            var current = _samples[_samples.Count - 1];
            _valueRun.Text = FormatNumber(current.Value);
            var color = _cfg.Numeric?.PickColor(current.Value) ?? WidgetTheme.ValueColor;
            _valueRun.Foreground = new SolidColorBrush(color);

            if (_cfg.ShowDelta || _cfg.ShowArrow)
            {
                // Baseline is always the archive-fetched value at the
                // historical anchor. Null until the host's scan completes.
                ApplyDelta(current.Value, _externalBaseline);
            }
            else
            {
                _deltaText.Text = "";
                _arrowIcon.Icon = EFontAwesomeIcon.Solid_Minus;
                _arrowIcon.Foreground = new SolidColorBrush(WidgetTheme.UnitColor);
            }
        }

        private void ApplyDelta(double current, double? baseline)
        {
            if (!baseline.HasValue)
            {
                // Archive scan hit an empty period (e.g. nothing recorded for
                // this channel a week ago). Surface that explicitly so the
                // operator doesn't think the widget is stuck.
                _deltaText.Text = "no data to compare";
                _deltaText.Foreground = new SolidColorBrush(WidgetTheme.UnitColor);
                _arrowIcon.Icon = EFontAwesomeIcon.Solid_InfoCircle;
                _arrowIcon.Foreground = new SolidColorBrush(WidgetTheme.UnitColor);
                string windowText = FormatComparisonWindow(_comparisonWindowFromUtc, _comparisonWindowToUtc);
                if (_windowText != null)
                {
                    _windowText.Text = windowText;
                    _windowText.Visibility = string.IsNullOrEmpty(windowText) ? Visibility.Collapsed : Visibility.Visible;
                }
                string mode = DescribeMode(_cfg?.ComparisonMode ?? TrendComparisonMode.SameTimeYesterday);
                _deltaRow.ToolTip = string.IsNullOrEmpty(windowText)
                    ? "There are no recorded samples for the comparison period (" + mode + "), so we can't calculate a percentage change yet."
                    : "No recorded samples for the comparison window (" + mode + "): " + windowText;
                return;
            }
            if (_windowText != null)
            {
                _windowText.Text = "";
                _windowText.Visibility = Visibility.Collapsed;
            }
            if (baseline.Value == 0)
            {
                _deltaText.Text = "0%";
                _deltaText.Foreground = new SolidColorBrush(WidgetTheme.UnitColor);
                _arrowIcon.Icon = EFontAwesomeIcon.Solid_Minus;
                _arrowIcon.Foreground = new SolidColorBrush(WidgetTheme.UnitColor);
                _deltaRow.ToolTip = "Comparing against: " + DescribeMode(_cfg.ComparisonMode);
                return;
            }
            double pct = (current - baseline.Value) / Math.Abs(baseline.Value) * 100.0;
            string sign = pct > 0 ? "+" : "";
            _deltaText.Text = _cfg.ShowDelta ? $"{sign}{pct.ToString("0.#", CultureInfo.InvariantCulture)}%" : "";
            // Tooltip describes the comparison source so an operator hovering
            // over the Δ% knows what they're comparing against.
            _deltaRow.ToolTip = "Comparing against: " + DescribeMode(_cfg.ComparisonMode);
            if (!_cfg.ShowArrow)
            {
                _arrowIcon.Visibility = Visibility.Collapsed;
                return;
            }
            _arrowIcon.Visibility = Visibility.Visible;

            // Pick arrow + color. Direction (HighIsBad) flips the meaning of
            // "good": HighIsBad=false (i.e. high-is-good) → up is green, down is red.
            // HighIsBad=true → up is red, down is green.
            bool flat = Math.Abs(pct) < 0.05;
            if (flat)
            {
                _arrowIcon.Icon = EFontAwesomeIcon.Solid_Minus;
                _arrowIcon.Foreground = new SolidColorBrush(WidgetTheme.UnitColor);
                return;
            }

            bool up = pct > 0;
            bool highIsGood = _cfg.Numeric != null && !_cfg.Numeric.HighIsBad;
            bool good = up == highIsGood;
            Color c = good
                ? (_cfg.Numeric?.ColorOk ?? Color.FromRgb(0x3C, 0xB3, 0x71))
                : (_cfg.Numeric?.ColorBad ?? Color.FromRgb(0xD8, 0x39, 0x2C));
            _arrowIcon.Icon = up ? EFontAwesomeIcon.Solid_ArrowUp : EFontAwesomeIcon.Solid_ArrowDown;
            _arrowIcon.Foreground = new SolidColorBrush(c);
            _deltaText.Foreground = new SolidColorBrush(c);
        }

        // Renders the historical scan window in the operator's local time
        // zone. Collapses to a single date when both ends fall on the same
        // calendar day so the display stays compact in narrow tiles.
        private static string FormatComparisonWindow(DateTime? fromUtc, DateTime? toUtc)
        {
            if (!fromUtc.HasValue || !toUtc.HasValue) return string.Empty;
            var fromLocal = fromUtc.Value.ToLocalTime();
            var toLocal = toUtc.Value.ToLocalTime();
            string fromStr = fromLocal.ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture);
            if (fromLocal.Date == toLocal.Date)
            {
                string toTime = toLocal.ToString("HH:mm", CultureInfo.InvariantCulture);
                return fromStr + " - " + toTime;
            }
            string toFull = toLocal.ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture);
            return fromStr + " - " + toFull;
        }

        private static string DescribeMode(TrendComparisonMode mode)
        {
            switch (mode)
            {
                case TrendComparisonMode.SameTimeYesterday: return "same time yesterday";
                case TrendComparisonMode.SameTimeLastWeek:  return "same time last week";
                case TrendComparisonMode.SameTimeLastMonth: return "same time last month";
                default: return "same time yesterday";
            }
        }

        private static string FormatNumber(double v)
        {
            if (v == (long)v) return ((long)v).ToString(CultureInfo.InvariantCulture);
            return v.ToString("0.##", CultureInfo.InvariantCulture);
        }
    }
}
