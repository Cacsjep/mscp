using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CommunitySDK;
using Microsoft.Win32;
using VideoOS.Platform;

namespace MetadataDisplay.Client.Export
{
    // CSV-export dialog. Two-column layout:
    //   - Left:  From/To + daily window + weekday picker + CSV format, plus
    //            a Load Data button. Field edits only mark the preview stale;
    //            the operator clicks Load Data to run the archive scan.
    //   - Right: scrolling preview table (first 200 rows) with a spinner
    //            overlaid only over the preview pane during a scan.
    // Save-as is gated on a current, non-empty preview so an operator can never
    // ship an outdated CSV.
    internal partial class ExportDialog : Window
    {
        private static readonly PluginLog _log = new PluginLog("MetadataDisplay");

        private const int PreviewMaxRows = 200;
        private const int ScanMaxFrames = 200000;
        // Single human-readable timestamp format for both preview and CSV.
        // Local time, sortable, and round-trips cleanly into Excel.
        private const string TimestampFormat = "yyyy-MM-dd HH:mm:ss";
        private static readonly Color ValidationErrorBrushColor = Color.FromRgb(0xD8, 0x39, 0x2C);

        private readonly Item _metadataItem;
        private readonly ExtractorConfig _extractorCfg;
        private readonly string _channelName;
        private readonly string _renderType;     // "Lamp" | "Number" | … so we can fold the label column for Lamp.
        private readonly string _lampMap;        // populated only for Lamp; null otherwise
        private readonly DateTime _defaultFromUtc;
        private readonly DateTime _defaultToUtc;
        // Multi-series export (Line Chart with > 1 series). When non-null the
        // dialog runs ScanManyAsync, merges into wide rows and saves via
        // WriteMultiSeries. Single-series export leaves these null.
        private readonly IReadOnlyList<LineSeries> _multiSeries;
        private readonly bool _isMultiSeries;

        private CancellationTokenSource _scanCts;
        private List<ExportRow> _lastPreviewRows;
        private List<MultiSeriesExportRow> _lastMultiPreviewRows;
        private int _lastTotalCount;
        private bool _previewIsCurrent;
        private bool _suppressFieldEvents;

        public ExportDialog(
            Item metadataItem,
            ExtractorConfig extractorCfg,
            string channelName,
            string renderType,
            string lampMap,
            DateTime defaultFromUtc,
            DateTime defaultToUtc)
            : this(metadataItem, extractorCfg, channelName, renderType, lampMap, defaultFromUtc, defaultToUtc, /*multiSeries*/ null)
        {
        }

        // Multi-series ctor - Line Chart widgets with more than one configured
        // series open the dialog through this overload. The first series's
        // ExtractorConfig drives the channel summary (Topic / DataKey lines)
        // and the multi list drives the actual scan + wide-format CSV.
        public ExportDialog(
            Item metadataItem,
            ExtractorConfig extractorCfg,
            string channelName,
            string renderType,
            string lampMap,
            DateTime defaultFromUtc,
            DateTime defaultToUtc,
            IReadOnlyList<LineSeries> multiSeries)
        {
            _metadataItem = metadataItem;
            _extractorCfg = extractorCfg;
            _channelName = channelName ?? string.Empty;
            _renderType = renderType ?? string.Empty;
            _lampMap = string.Equals(_renderType, "Lamp", StringComparison.OrdinalIgnoreCase) ? lampMap : null;
            _defaultFromUtc = defaultFromUtc.Kind == DateTimeKind.Utc ? defaultFromUtc : defaultFromUtc.ToUniversalTime();
            _defaultToUtc = defaultToUtc.Kind == DateTimeKind.Utc ? defaultToUtc : defaultToUtc.ToUniversalTime();
            _multiSeries = multiSeries;
            _isMultiSeries = multiSeries != null && multiSeries.Count > 1;
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            HydrateFromInputs();
            HookEvents();
            ApplyLampLabelColumnVisibility();
            // No auto-scan: operator tunes settings, then explicitly clicks
            // "Load Data" to run the archive scan. This avoids hammering the
            // archive on every keystroke and matches how the Number / Lamp /
            // other widgets don't pre-load anything in setup either.
            SetStatus("Click Load Data to preview the matching rows.", isError: false);
            saveButton.IsEnabled = false;
            _previewIsCurrent = false;
            RenderPreviewRows();
        }

        private void HydrateFromInputs()
        {
            _suppressFieldEvents = true;
            try
            {
                PopulateHourMinuteCombos();
                ApplyDateTimeToControls(_defaultFromUtc, fromDatePicker, fromHourCombo, fromMinuteCombo);
                ApplyDateTimeToControls(_defaultToUtc, toDatePicker, toHourCombo, toMinuteCombo);

                dailyStartBox.Text = "08:00";
                dailyEndBox.Text = "17:00";
                dailyStartBox.IsEnabled = false;
                dailyEndBox.IsEnabled = false;
            }
            finally { _suppressFieldEvents = false; }
        }

        // Fill hour combos with 0..23 and minute combos with 0..59 (5-minute
        // increments - matches Timelapse + keeps the dropdown short while
        // covering the common cases for hand-tweaking).
        private void PopulateHourMinuteCombos()
        {
            fromHourCombo.Items.Clear();
            toHourCombo.Items.Clear();
            for (int h = 0; h < 24; h++)
            {
                string label = h.ToString("00", CultureInfo.InvariantCulture);
                fromHourCombo.Items.Add(label);
                toHourCombo.Items.Add(label);
            }
            fromMinuteCombo.Items.Clear();
            toMinuteCombo.Items.Clear();
            for (int m = 0; m < 60; m += 5)
            {
                string label = m.ToString("00", CultureInfo.InvariantCulture);
                fromMinuteCombo.Items.Add(label);
                toMinuteCombo.Items.Add(label);
            }
        }

        private static void ApplyDateTimeToControls(DateTime utc, DatePicker datePicker, ComboBox hourCombo, ComboBox minuteCombo)
        {
            var local = utc.ToLocalTime();
            datePicker.SelectedDate = local.Date;
            hourCombo.SelectedIndex = local.Hour;
            // Snap to nearest 5-minute slot so the combo always has a selection.
            int slot = Math.Min(11, Math.Max(0, local.Minute / 5));
            minuteCombo.SelectedIndex = slot;
        }

        private void HookEvents()
        {
            // Settings changes only mark the preview stale - the operator
            // explicitly clicks Load Data to run the actual archive scan.
            fromDatePicker.SelectedDateChanged += (s, e) => MarkPreviewStale();
            toDatePicker.SelectedDateChanged += (s, e) => MarkPreviewStale();
            fromHourCombo.SelectionChanged += (s, e) => MarkPreviewStale();
            fromMinuteCombo.SelectionChanged += (s, e) => MarkPreviewStale();
            toHourCombo.SelectionChanged += (s, e) => MarkPreviewStale();
            toMinuteCombo.SelectionChanged += (s, e) => MarkPreviewStale();

            dailyWindowCheck.Checked += (s, e) => { ApplyDailyWindowEnabled(); MarkPreviewStale(); };
            dailyWindowCheck.Unchecked += (s, e) => { ApplyDailyWindowEnabled(); MarkPreviewStale(); };
            dailyStartBox.TextChanged += (s, e) => MarkPreviewStale();
            dailyEndBox.TextChanged += (s, e) => MarkPreviewStale();

            weekdayCombo.SelectionChanged += (s, e) => { ApplyWeekdayComboVisibility(); MarkPreviewStale(); };
            foreach (var cb in EnumerateDayChecks())
            {
                cb.Checked += (s, e) => MarkPreviewStale();
                cb.Unchecked += (s, e) => MarkPreviewStale();
            }

            // CSV format toggles only affect rendering of an existing preview;
            // re-render in place rather than invalidating the loaded rows.
            delimiterCombo.SelectionChanged += (s, e) => RenderPreviewRows();
            decimalCombo.SelectionChanged += (s, e) => RenderPreviewRows();
            includeHeaderCheck.Checked += (s, e) => RenderPreviewRows();
            includeHeaderCheck.Unchecked += (s, e) => RenderPreviewRows();
        }

        private IEnumerable<CheckBox> EnumerateDayChecks()
        {
            yield return daySunCheck;
            yield return dayMonCheck;
            yield return dayTueCheck;
            yield return dayWedCheck;
            yield return dayThuCheck;
            yield return dayFriCheck;
            yield return daySatCheck;
        }

        private void ApplyDailyWindowEnabled()
        {
            bool on = dailyWindowCheck.IsChecked == true;
            dailyStartBox.IsEnabled = on;
            dailyEndBox.IsEnabled = on;
        }

        private void ApplyWeekdayComboVisibility()
        {
            var tag = (weekdayCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString();
            weekdayCustomPanel.Visibility = string.Equals(tag, "custom", StringComparison.Ordinal)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void ApplyLampLabelColumnVisibility()
        {
            bool show = _lampMap != null;
            previewLabelHeader.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            previewLabelHeaderCol.Width = show ? new GridLength(140) : new GridLength(0);
        }

        // Marks any previous scan result stale: the status hint nudges the
        // operator to click Load Data again so they can refresh the preview
        // against the new settings. Save-as stays enabled if there are rows in
        // the preview - what gets written is exactly the rows the operator can
        // see, which is the safest contract.
        private void MarkPreviewStale()
        {
            if (_suppressFieldEvents) return;
            _previewIsCurrent = false;
            SetStatus("Settings changed. Click Load Data to refresh the preview.", isError: false);
        }

        private void OnLoadDataClick(object sender, RoutedEventArgs e)
        {
            StartScan();
        }

        private void OnQuickRangeClick(object sender, RoutedEventArgs e)
        {
            var tag = (sender as System.Windows.Controls.Primitives.ButtonBase)?.Tag?.ToString();
            if (string.IsNullOrEmpty(tag)) return;
            DateTime nowUtc = DateTime.UtcNow;
            DateTime fromUtc, toUtc;
            switch (tag)
            {
                case "today":
                    {
                        var localToday = DateTime.Now.Date;
                        fromUtc = localToday.ToUniversalTime();
                        toUtc = nowUtc;
                        break;
                    }
                case "yesterday":
                    {
                        var localYesterday = DateTime.Now.Date.AddDays(-1);
                        fromUtc = localYesterday.ToUniversalTime();
                        toUtc = localYesterday.AddDays(1).AddTicks(-1).ToUniversalTime();
                        break;
                    }
                default:
                    if (!int.TryParse(tag, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds) || seconds <= 0)
                        return;
                    fromUtc = nowUtc.AddSeconds(-seconds);
                    toUtc = nowUtc;
                    break;
            }
            _suppressFieldEvents = true;
            try
            {
                ApplyDateTimeToControls(fromUtc, fromDatePicker, fromHourCombo, fromMinuteCombo);
                ApplyDateTimeToControls(toUtc, toDatePicker, toHourCombo, toMinuteCombo);
            }
            finally { _suppressFieldEvents = false; }
            // Stage only - the operator clicks Load Data to scan.
            MarkPreviewStale();
        }

        private async void StartScan()
        {
            // Validate inputs first - invalid input shows a status message but
            // doesn't kick off a scan that would just return zero. We leave
            // saveButton alone here: any rows from a prior successful scan
            // remain in the preview and stay savable.
            if (!TryComposeLocalUtc(fromDatePicker, fromHourCombo, fromMinuteCombo, out var fromUtc))
            {
                SetStatus("Pick a valid From date.", isError: true);
                return;
            }
            if (!TryComposeLocalUtc(toDatePicker, toHourCombo, toMinuteCombo, out var toUtc))
            {
                SetStatus("Pick a valid To date.", isError: true);
                return;
            }
            if (toUtc <= fromUtc)
            {
                SetStatus("To must be after From.", isError: true);
                return;
            }

            TimeSpan? dailyStart = null, dailyEnd = null;
            if (dailyWindowCheck.IsChecked == true)
            {
                if (!TryParseTimeOfDay(dailyStartBox.Text, out var ds))
                {
                    MarkInvalid(dailyStartBox);
                    SetStatus("Daily Start is invalid (expected HH:mm).", isError: true);
                    return;
                }
                ResetValidationStyle(dailyStartBox);
                if (!TryParseTimeOfDay(dailyEndBox.Text, out var de))
                {
                    MarkInvalid(dailyEndBox);
                    SetStatus("Daily End is invalid (expected HH:mm).", isError: true);
                    return;
                }
                ResetValidationStyle(dailyEndBox);
                dailyStart = ds;
                dailyEnd = de;
            }

            byte? weekdayMask = ResolveWeekdayMask();
            if (weekdayMask.HasValue && (weekdayMask.Value & 0x7F) == 0)
            {
                SetStatus("At least one day must be selected.", isError: true);
                return;
            }

            var filter = new ExportFilter
            {
                FromUtc = fromUtc,
                ToUtc = toUtc,
                DailyStartLocal = dailyStart,
                DailyEndLocal = dailyEnd,
                WeekdayMask = weekdayMask,
            };

            // Cancel in-flight scan and start fresh - the prior scan's results
            // are about to be replaced anyway.
            try { _scanCts?.Cancel(); } catch { }
            _scanCts = new CancellationTokenSource();
            var ct = _scanCts.Token;

            ShowSpinner(true);
            SetStatus("Scanning archive...", isError: false);
            saveButton.IsEnabled = false;

            try
            {
                if (_isMultiSeries)
                {
                    var cfgs = new List<ExtractorConfig>();
                    foreach (var s in _multiSeries)
                        cfgs.Add(s != null ? s.ToExtractorConfig() : null);
                    var perSeries = await ArchiveExporter.ScanManyAsync(
                        _metadataItem, cfgs, filter, ScanMaxFrames, ct).ConfigureAwait(true);
                    if (ct.IsCancellationRequested) return;
                    var wide = ArchiveExporter.MergeWide(perSeries);
                    _lastMultiPreviewRows = wide;
                    _lastPreviewRows = null;
                    _lastTotalCount = wide.Count;
                    _previewIsCurrent = true;
                    RenderPreviewRows();
                    SetStatus(wide.Count == 0
                        ? "Scan complete. No matching rows."
                        : $"Scan complete. {wide.Count:N0} merged rows across {cfgs.Count} series.",
                        isError: false);
                }
                else
                {
                    var rows = await ArchiveExporter.ScanAsync(
                        _metadataItem, _extractorCfg, filter, _lampMap, ScanMaxFrames, ct).ConfigureAwait(true);
                    if (ct.IsCancellationRequested) return;
                    _lastPreviewRows = rows;
                    _lastMultiPreviewRows = null;
                    _lastTotalCount = rows.Count;
                    _previewIsCurrent = true;
                    RenderPreviewRows();
                    SetStatus(rows.Count == 0
                        ? "Scan complete. No matching rows."
                        : $"Scan complete. {rows.Count:N0} matching rows.",
                        isError: false);
                }
            }
            catch (OperationCanceledException) { /* superseded by another scan */ }
            catch (Exception ex)
            {
                _log.Error($"[Export] Scan failed: {ex.Message}", ex);
                _previewIsCurrent = false;
                _lastPreviewRows = null;
                _lastMultiPreviewRows = null;
                _lastTotalCount = 0;
                // RenderPreviewRows resets saveButton via the empty-rows path.
                RenderPreviewRows();
                SetStatus($"Scan failed: {ex.Message}", isError: true);
            }
            finally { ShowSpinner(false); }
        }

        private byte? ResolveWeekdayMask()
        {
            var tag = (weekdayCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "all";
            switch (tag)
            {
                case "weekdays": return (byte)0b0111110;  // Mon(1)..Fri(5)
                case "weekends": return (byte)0b1000001;  // Sun(0) + Sat(6)
                case "custom":
                    byte mask = 0;
                    if (daySunCheck.IsChecked == true) mask |= 1 << 0;
                    if (dayMonCheck.IsChecked == true) mask |= 1 << 1;
                    if (dayTueCheck.IsChecked == true) mask |= 1 << 2;
                    if (dayWedCheck.IsChecked == true) mask |= 1 << 3;
                    if (dayThuCheck.IsChecked == true) mask |= 1 << 4;
                    if (dayFriCheck.IsChecked == true) mask |= 1 << 5;
                    if (daySatCheck.IsChecked == true) mask |= 1 << 6;
                    return mask;
                default:
                    return null; // "all" - every day matches
            }
        }

        private void RenderPreviewRows()
        {
            previewRowsPanel.Children.Clear();
            int total = _lastTotalCount;
            previewCountText.Text = total > 0
                ? $"showing {Math.Min(total, PreviewMaxRows)} of {total:N0} matching rows"
                : "no rows";

            bool empty = _isMultiSeries
                ? (_lastMultiPreviewRows == null || _lastMultiPreviewRows.Count == 0)
                : (_lastPreviewRows == null || _lastPreviewRows.Count == 0);
            // Single source of truth for the Save button: rows in preview means
            // Save is clickable. No other code path should toggle it.
            saveButton.IsEnabled = !empty;
            if (empty)
            {
                previewEmptyPanel.Visibility = Visibility.Visible;
                previewEmptyText.Text = _previewIsCurrent
                    ? "No matching rows in this range."
                    : "Adjust the settings on the left to preview.";
                previewEmptyDetail.Text = _previewIsCurrent
                    ? "Widen the From/To range, relax the daily window, or enable more weekdays."
                    : string.Empty;
                return;
            }
            previewEmptyPanel.Visibility = Visibility.Collapsed;

            string decimalSeparator = (decimalCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? ".";

            if (_isMultiSeries)
            {
                int colCount = _multiSeries.Count;
                int n = Math.Min(_lastMultiPreviewRows.Count, PreviewMaxRows);
                for (int i = 0; i < n; i++)
                {
                    var r = _lastMultiPreviewRows[i];
                    var grid = new Grid
                    {
                        Background = (i & 1) == 0 ? Brushes.Transparent : (Brush)new SolidColorBrush(Color.FromRgb(0x1F, 0x26, 0x29)),
                    };
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
                    for (int c = 0; c < colCount; c++)
                        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                    string ts = FormatTimestampLocal(r.TimestampUtc);
                    grid.Children.Add(MakeCell(ts, 0));
                    for (int c = 0; c < colCount; c++)
                    {
                        string raw = (r.SeriesValues != null && c < r.SeriesValues.Length) ? r.SeriesValues[c] : null;
                        grid.Children.Add(MakeCell(FormatNumeric(raw, decimalSeparator), c + 1));
                    }
                    previewRowsPanel.Children.Add(grid);
                }
                return;
            }

            bool showLabel = _lampMap != null;
            int rowsToShow = Math.Min(_lastPreviewRows.Count, PreviewMaxRows);
            for (int i = 0; i < rowsToShow; i++)
            {
                var r = _lastPreviewRows[i];
                var grid = new Grid
                {
                    Background = (i & 1) == 0 ? Brushes.Transparent : (Brush)new SolidColorBrush(Color.FromRgb(0x1F, 0x26, 0x29)),
                };
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = showLabel ? new GridLength(140) : new GridLength(0) });

                string ts = FormatTimestampLocal(r.TimestampUtc);

                grid.Children.Add(MakeCell(ts, 0));
                grid.Children.Add(MakeCell(FormatNumeric(r.Value, decimalSeparator), 1));
                if (showLabel) grid.Children.Add(MakeCell(r.Label ?? string.Empty, 2));
                previewRowsPanel.Children.Add(grid);
            }
        }

        private static TextBlock MakeCell(string text, int column)
        {
            var tb = new TextBlock
            {
                Text = text ?? string.Empty,
                Foreground = new SolidColorBrush(Color.FromRgb(0xCF, 0xD7, 0xDA)),
                FontSize = 12,
                Margin = new Thickness(8, 2, 8, 2),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            Grid.SetColumn(tb, column);
            return tb;
        }

        private static string FormatTimestampLocal(DateTime utc)
        {
            return utc.ToLocalTime().ToString(TimestampFormat, CultureInfo.InvariantCulture);
        }

        private static string FormatNumeric(string raw, string decimalSeparator)
        {
            if (string.IsNullOrEmpty(decimalSeparator) || decimalSeparator == ".") return raw ?? string.Empty;
            if (raw == null) return string.Empty;
            if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) return raw;
            return d.ToString("R", CultureInfo.InvariantCulture).Replace(".", decimalSeparator);
        }

        private void ShowSpinner(bool show)
        {
            previewSpinnerPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            previewTable.Opacity = show ? 0.4 : 1.0;
            // Hide the "no rows / adjust settings" overlay while a scan is in
            // flight so the spinner has the pane to itself - otherwise the two
            // messages stack on top of each other.
            if (show) previewEmptyPanel.Visibility = Visibility.Collapsed;
        }

        private void SetStatus(string text, bool isError)
        {
            statusText.Text = text ?? string.Empty;
            statusText.Foreground = isError
                ? new SolidColorBrush(ValidationErrorBrushColor)
                : new SolidColorBrush(Color.FromRgb(0xA9, 0xB5, 0xBB));
        }

        private static void MarkInvalid(TextBox tb)
        {
            tb.BorderBrush = new SolidColorBrush(ValidationErrorBrushColor);
            tb.BorderThickness = new Thickness(1.5);
        }

        private static void ResetValidationStyle(TextBox tb)
        {
            tb.BorderBrush = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44));
            tb.BorderThickness = new Thickness(1);
        }

        // Compose a UTC instant from a DatePicker + hour combo + minute combo.
        // Returns false if the date isn't set; defaults the time to 00:00 if a
        // combo selection is missing (newly opened dialogs always have one).
        private static bool TryComposeLocalUtc(DatePicker datePicker, ComboBox hourCombo, ComboBox minuteCombo, out DateTime utc)
        {
            utc = default;
            if (datePicker?.SelectedDate == null) return false;
            int hour = 0;
            if (hourCombo?.SelectedItem is string hs &&
                int.TryParse(hs, NumberStyles.Integer, CultureInfo.InvariantCulture, out var h))
                hour = h;
            int minute = 0;
            if (minuteCombo?.SelectedItem is string ms &&
                int.TryParse(ms, NumberStyles.Integer, CultureInfo.InvariantCulture, out var m))
                minute = m;
            var local = datePicker.SelectedDate.Value.Date.AddHours(hour).AddMinutes(minute);
            // Treat the picker as wall-clock local; convert to UTC for the scan.
            utc = DateTime.SpecifyKind(local, DateTimeKind.Local).ToUniversalTime();
            return true;
        }

        private static bool TryParseTimeOfDay(string s, out TimeSpan tod)
        {
            tod = default;
            if (string.IsNullOrWhiteSpace(s)) return false;
            string[] formats = { @"hh\:mm", @"h\:mm", @"hh\:mm\:ss" };
            if (TimeSpan.TryParseExact(s.Trim(), formats, CultureInfo.InvariantCulture, out tod))
            {
                if (tod >= TimeSpan.Zero && tod < TimeSpan.FromDays(1)) return true;
            }
            return false;
        }

        private void OnSaveAsClick(object sender, RoutedEventArgs e)
        {
            // Save whatever rows the operator is currently looking at - we
            // never want a "preview shows rows but Save is disabled" mismatch.
            // Staleness (operator changed settings without re-running) is
            // surfaced via the status line, not by gating Save.
            bool empty = _isMultiSeries
                ? (_lastMultiPreviewRows == null || _lastMultiPreviewRows.Count == 0)
                : (_lastPreviewRows == null || _lastPreviewRows.Count == 0);
            if (empty)
            {
                SetStatus("Nothing to save - click Load Data first.", isError: true);
                return;
            }
            var sfd = new SaveFileDialog
            {
                Filter = "CSV file (*.csv)|*.csv|All files (*.*)|*.*",
                FileName = SuggestFilename(),
                AddExtension = true,
                DefaultExt = ".csv",
                OverwritePrompt = true,
            };
            if (sfd.ShowDialog(this) != true) return;

            var opt = new CsvOptions
            {
                Delimiter = (delimiterCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? ",",
                IncludeHeader = includeHeaderCheck.IsChecked == true,
                TimestampFormat = TimestampFormat,
                TimestampInLocalTime = true,
                DecimalSeparator = (decimalCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? ".",
                ValueHeader = string.IsNullOrEmpty(_extractorCfg?.DataKey) ? "value" : _extractorCfg.DataKey,
                IncludeLabelColumn = !_isMultiSeries && _lampMap != null,
            };

            try
            {
                using (var sw = new StreamWriter(sfd.FileName, false, new System.Text.UTF8Encoding(true)))
                {
                    if (_isMultiSeries)
                    {
                        var names = new List<string>(_multiSeries.Count);
                        foreach (var s in _multiSeries)
                        {
                            string label = s == null ? "value" : s.DisplayName;
                            if (string.IsNullOrEmpty(label)) label = "value";
                            names.Add(label);
                        }
                        CsvWriter.WriteMultiSeries(sw, names, _lastMultiPreviewRows, opt);
                    }
                    else
                    {
                        CsvWriter.WriteSingleSeries(sw, _lastPreviewRows, opt);
                    }
                }
                int written = _isMultiSeries ? _lastMultiPreviewRows.Count : _lastPreviewRows.Count;
                _log.Info($"[Export] Wrote {written} rows to {sfd.FileName}");
                SetStatus($"Saved {written:N0} rows to {sfd.FileName}", isError: false);
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                _log.Error($"[Export] Save failed: {ex.Message}", ex);
                SetStatus($"Save failed: {ex.Message}", isError: true);
            }
        }

        private string SuggestFilename()
        {
            return "export_" + DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".csv";
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            try { _scanCts?.Cancel(); } catch { }
            DialogResult = false;
            Close();
        }
    }
}
