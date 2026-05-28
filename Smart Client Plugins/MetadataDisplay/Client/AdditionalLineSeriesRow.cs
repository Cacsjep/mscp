using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CommunitySDK;
using VideoOS.Platform.UI;

namespace MetadataDisplay.Client
{
    // One row in the configuration window's "Additional series" accordion.
    // Owns the WPF controls for one LineSeries (topic match, data key, color,
    // thickness, line type, Y axis, optional threshold) plus a Remove button.
    // The configuration window builds 0..7 of these (cap is 8 series total
    // counting Series 1 in the legacy single-series fields).
    //
    // Reading: ToLineSeries() pulls the current control values into a fresh
    // LineSeries POCO. The window calls this on every preview rebuild + on
    // save so the additional-series list is always derived from the live UI.
    //
    // Snapshot integration: Topic / Source filter / Data key are editable
    // ComboBoxes. ApplySnapshot pushes a picked-packet snapshot into the
    // dropdowns so the operator can pick from observed values. Free-form
    // typing still works - the dropdown is a hint, not a restriction.
    internal sealed class AdditionalLineSeriesRow
    {
        public Expander RootExpander { get; }
        public Action OnChanged;
        // Fired on UI thread when Topic / Field / Source filter text changes -
        // the configuration window uses this to rebuild the immutable
        // ExtractorConfig snapshot consumed on the source-callback thread.
        public Action OnExtractorChanged;
        public Action<AdditionalLineSeriesRow> OnRemove;

        // The row's last applied snapshot. Mirrors the single `_lastSnapshot`
        // field on the main window: every dropdown rebuild and every
        // topic-change refilter reads exclusively from this. Updated only
        // when a non-empty snapshot arrives so an empty fan-out can never
        // wipe the discovered list.
        private LearnSnapshot _lastSnapshot;
        // Invoked when the row's Pick packet button is clicked. The
        // configuration window opens the packet browser scoped to the
        // configured metadata channel and returns the picked snapshot.
        // Null = cancelled or no channel.
        public Func<Window, LearnSnapshot> OnPickPacketRequested;
        // Invoked when the row's Import packet button is clicked. The
        // configuration window opens the import dialog and returns its result
        // so the row can apply the parsed snapshot. Null result = cancelled.
        public Func<Window, LearnSnapshot> OnImportPacketRequested;

        private readonly ComboBox _topicCombo;
        private readonly ComboBox _sourceFiltersCombo;
        private readonly ComboBox _dataKeyCombo;
        private readonly TextBox _nameBox;
        private readonly ColorPickerControl _color;
        private readonly TextBox _thicknessBox;
        private readonly ComboBox _typeCombo;
        private readonly ComboBox _yAxisCombo;
        private readonly CheckBox _fillCheck;
        private readonly CheckBox _markerCheck;

        private readonly CheckBox _thOnCheck;
        private readonly TextBox _thMinBox;
        private readonly TextBox _thMaxBox;
        private readonly CheckBox _thHighIsBadCheck;
        private readonly ColorPickerControl _thColorOk;
        private readonly ColorPickerControl _thColorWarn;
        private readonly ColorPickerControl _thColorBad;

        public AdditionalLineSeriesRow(LineSeries seed)
        {
            seed = seed ?? new LineSeries();

            _topicCombo = new ComboBox { IsEditable = true, Text = seed.Topic ?? string.Empty };

            _sourceFiltersCombo = new ComboBox { IsEditable = true, Text = seed.SourceFilters ?? string.Empty };
            _dataKeyCombo = new ComboBox { IsEditable = true, Text = seed.DataKey ?? string.Empty };
            _nameBox = new TextBox { Text = seed.Name ?? string.Empty };

            _color = new ColorPickerControl();
            _color.HexValue = string.IsNullOrEmpty(seed.Color) ? "#FF4FC3F7" : seed.Color;

            _thicknessBox = new TextBox { Text = seed.Thickness.ToString("0.##", CultureInfo.InvariantCulture), Width = 60 };
            _typeCombo = new ComboBox { Width = 110 };
            _typeCombo.Items.Add(new ComboBoxItem { Content = "Straight", Tag = "Straight" });
            _typeCombo.Items.Add(new ComboBoxItem { Content = "Smooth", Tag = "Smooth" });
            _typeCombo.Items.Add(new ComboBoxItem { Content = "Step", Tag = "Step" });
            SelectComboByTag(_typeCombo, seed.LineType ?? "Straight");

            _yAxisCombo = new ComboBox { Width = 110 };
            _yAxisCombo.Items.Add(new ComboBoxItem { Content = "Left axis", Tag = "Left" });
            _yAxisCombo.Items.Add(new ComboBoxItem { Content = "Right axis", Tag = "Right" });
            SelectComboByTag(_yAxisCombo, seed.YAxis == LineSeriesAxis.Right ? "Right" : "Left");

            _fillCheck = new CheckBox { Content = "Filled", IsChecked = seed.FillEnabled, VerticalAlignment = VerticalAlignment.Center };
            _markerCheck = new CheckBox { Content = "Markers", IsChecked = seed.ShowMarker, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };

            var th = seed.Threshold ?? new LineSeriesThreshold();
            _thOnCheck = new CheckBox { Content = "Enable threshold", IsChecked = th.Enabled };
            _thMinBox = new TextBox { Text = th.Min.HasValue ? th.Min.Value.ToString("R", CultureInfo.InvariantCulture) : "" };
            _thMaxBox = new TextBox { Text = th.Max.HasValue ? th.Max.Value.ToString("R", CultureInfo.InvariantCulture) : "" };
            _thHighIsBadCheck = new CheckBox { Content = "High value is bad", IsChecked = th.HighIsBad };
            _thColorOk = new ColorPickerControl(); _thColorOk.HexValue = th.ColorOk ?? "#3CB371";
            _thColorWarn = new ColorPickerControl(); _thColorWarn.HexValue = th.ColorWarn ?? "#E69500";
            _thColorBad = new ColorPickerControl(); _thColorBad.HexValue = th.ColorBad ?? "#D8392C";

            RootExpander = BuildLayout(seed);
            HookEvents();
        }

        // UI-thread-only: build an immutable extractor config from the current
        // textbox/combo state. The configuration window calls this when the
        // row changes so the source thread has a stable snapshot to read.
        // Reads Text directly (not SelectedItem) because this can be called
        // while the operator is mid-typing - Text is authoritative for the
        // visible UI state in every context except a SelectionChanged handler.
        public ExtractorConfig BuildExtractorConfig()
        {
            return new ExtractorConfig
            {
                Topic = _topicCombo.Text ?? string.Empty,
                TopicMatchMode = "Exact",
                SourceFilters = ExtractorConfig.ParseSourceFilters(_sourceFiltersCombo.Text ?? string.Empty),
                DataKey = _dataKeyCombo.Text ?? string.Empty,
            };
        }

        public LineSeries ToLineSeries()
        {
            var s = new LineSeries
            {
                Topic = _topicCombo.Text ?? string.Empty,
                TopicMatchMode = "Exact",
                SourceFilters = _sourceFiltersCombo.Text ?? string.Empty,
                DataKey = _dataKeyCombo.Text ?? string.Empty,
                Name = _nameBox.Text ?? string.Empty,
                Color = string.IsNullOrWhiteSpace(_color.HexValue) ? "#FF4FC3F7" : _color.HexValue,
                LineType = (_typeCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Straight",
                YAxis = string.Equals((_yAxisCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString(), "Right", StringComparison.OrdinalIgnoreCase)
                    ? LineSeriesAxis.Right : LineSeriesAxis.Left,
                FillEnabled = _fillCheck.IsChecked == true,
                ShowMarker = _markerCheck.IsChecked == true,
            };
            if (double.TryParse(_thicknessBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var t) && t > 0)
                s.Thickness = t;

            s.Threshold = new LineSeriesThreshold
            {
                Enabled = _thOnCheck.IsChecked == true,
                Min = TryParseNullable(_thMinBox.Text),
                Max = TryParseNullable(_thMaxBox.Text),
                HighIsBad = _thHighIsBadCheck.IsChecked == true,
                ColorOk = string.IsNullOrWhiteSpace(_thColorOk.HexValue) ? "#3CB371" : _thColorOk.HexValue,
                ColorWarn = string.IsNullOrWhiteSpace(_thColorWarn.HexValue) ? "#E69500" : _thColorWarn.HexValue,
                ColorBad = string.IsNullOrWhiteSpace(_thColorBad.HexValue) ? "#D8392C" : _thColorBad.HexValue,
            };
            return s;
        }

        // Reentrancy guard: ApplySnapshot rebuilds combo Items, which can
        // re-fire TextChanged on those combos and pull the handlers into the
        // middle of a half-built state. The flag lets bulk refreshes mark
        // themselves "in progress" and have handlers bail out instead of
        // clobbering the dropdowns mid-rebuild.
        private bool _applyingSnapshot;

        // Refilter only the Field dropdown for the supplied Topic. Doesn't
        // touch _topicCombo.Items so it can't recursively retrigger its
        // own TextChanged. Called from the Topic.TextChanged handler.
        private void RefilterFieldCombo(string topic)
        {
            if (_applyingSnapshot) return;
            if (_lastSnapshot == null) return;
            RefreshFieldComboItems(_lastSnapshot, topic);
        }

        // Opens the channel-scoped Pick packet browser and applies the picked
        // snapshot to this row only. Auto-selects the first topic when the
        // row's Topic combo is still empty so the typical "pick and choose
        // the field" flow works in one step.
        private void OnPickPacketClick(object sender, RoutedEventArgs e)
        {
            var owner = Window.GetWindow(RootExpander);
            var snap = OnPickPacketRequested?.Invoke(owner);
            if (snap == null) return;
            ConsumePickedSnapshot(snap);
        }

        // Opens the shared packet-import dialog (owned by the configuration
        // window) and applies the resulting snapshot to this row only.
        private void OnImportPacketClick(object sender, RoutedEventArgs e)
        {
            var owner = Window.GetWindow(RootExpander);
            var snap = OnImportPacketRequested?.Invoke(owner);
            if (snap == null) return;
            ConsumePickedSnapshot(snap);
        }

        private void ConsumePickedSnapshot(LearnSnapshot snap)
        {
            ApplySnapshot(snap);

            if (string.IsNullOrWhiteSpace(_topicCombo.Text))
            {
                var first = PacketImportDialog.FirstTopic(snap);
                if (!string.IsNullOrEmpty(first)) _topicCombo.Text = first;
            }

            // Row-level extractor rebuild so the new Topic / Field text
            // propagates to the snapshot the source thread reads from.
            OnExtractorChanged?.Invoke();
        }

        // Push picked topics + data keys + source filters into the dropdowns.
        // Preserves currently-typed values; new items appear as suggestions.
        // Called by:
        //   - this row's Pick packet / Import packet button
        //   - the configuration window pre-populating a freshly-added row
        //
        // Empty snapshots (Topics.Count == 0) are ignored so a packet with no
        // NotificationMessages can't wipe the row's accumulated unique list.
        public void ApplySnapshot(LearnSnapshot snap)
        {
            if (snap == null || _applyingSnapshot) return;
            if (snap.Topics == null || snap.Topics.Count == 0) return;

            _lastSnapshot = snap;
            _applyingSnapshot = true;
            try
            {
                RefreshTopicComboItems(snap);
                string topic = _topicCombo.Text ?? string.Empty;
                RefreshFieldComboItems(snap, topic);
                RefreshSourceFilterItems(snap, topic);
            }
            finally { _applyingSnapshot = false; }
        }

        // Just the Topic combo. Used by Topic.DropDownOpened so opening the
        // dropdown sees the latest picked topics without rebuilding the
        // sibling combos (which would clobber the operator's Field selection).
        private void RefreshTopicComboItems(LearnSnapshot snap)
        {
            string topic = _topicCombo.Text ?? string.Empty;
            FillCombo(_topicCombo, snap.Topics
                .Select(t => t.Topic)
                .Where(t => !string.IsNullOrEmpty(t))
                .OrderBy(t => t, StringComparer.OrdinalIgnoreCase));
            _topicCombo.Text = topic;
        }

        // Just the Field combo, filtered to the supplied Topic. Used by
        // Field.DropDownOpened and by the Topic-change handlers.
        private void RefreshFieldComboItems(LearnSnapshot snap, string topic)
        {
            var keys = MatchingDataKeys(snap, topic);
            string current = _dataKeyCombo.Text ?? string.Empty;
            FillCombo(_dataKeyCombo, keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
            bool topicSelected = !string.IsNullOrEmpty(topic);
            _dataKeyCombo.Text = keys.Contains(current) ? current : (topicSelected ? string.Empty : current);
        }

        // Just the Source filter combo's suggestions, filtered to the
        // supplied Topic. Used by SourceFilter.DropDownOpened.
        private void RefreshSourceFilterItems(LearnSnapshot snap, string topic)
        {
            string current = _sourceFiltersCombo.Text ?? string.Empty;
            FillCombo(_sourceFiltersCombo, MatchingSourceSuggestions(snap, topic));
            _sourceFiltersCombo.Text = current;
        }

        // Keys observed under the supplied Topic. When no Topic is selected,
        // returns the union of every observed key so the operator can browse.
        private static HashSet<string> MatchingDataKeys(LearnSnapshot snap, string topic)
        {
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            bool topicSelected = !string.IsNullOrEmpty(topic);
            foreach (var t in snap.Topics)
            {
                if (!topicSelected || string.Equals(t.Topic, topic, StringComparison.OrdinalIgnoreCase))
                    foreach (var dk in t.DataKeyExamples.Keys) keys.Add(dk);
            }
            return keys;
        }

        // Source-filter "name=value" suggestions for the supplied Topic, or
        // every observed pair when no Topic is selected.
        private static IEnumerable<string> MatchingSourceSuggestions(LearnSnapshot snap, string topic)
        {
            bool topicSelected = !string.IsNullOrEmpty(topic);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in snap.Topics)
            {
                if (topicSelected && !string.Equals(t.Topic, topic, StringComparison.OrdinalIgnoreCase)) continue;
                foreach (var sv in t.SourceValues)
                    foreach (var v in sv.Value)
                    {
                        var s = sv.Key + "=" + v;
                        if (seen.Add(s)) yield return s;
                    }
            }
        }

        private static void FillCombo(ComboBox combo, IEnumerable<string> items)
        {
            combo.Items.Clear();
            foreach (var i in items) combo.Items.Add(i);
        }

        private static double? TryParseNullable(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? (double?)v : null;
        }

        private static void SelectComboByTag(ComboBox combo, string tag)
        {
            if (string.IsNullOrEmpty(tag)) { combo.SelectedIndex = 0; return; }
            foreach (var it in combo.Items)
            {
                if (it is ComboBoxItem ci && string.Equals(ci.Tag?.ToString(), tag, StringComparison.OrdinalIgnoreCase))
                {
                    combo.SelectedItem = ci;
                    return;
                }
            }
            combo.SelectedIndex = 0;
        }

        // Label sized to match the main "What to read" section (120px column).
        private static TextBlock Label(string text)
            => new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0), Foreground = new SolidColorBrush(Color.FromRgb(0xCF, 0xD7, 0xDA)) };

        // Two-column row: 120px label + stretching content. Mirrors the
        // styling of the main series rows so additional series rows don't
        // look hand-rolled and cramped.
        private static Grid LabeledRow(string labelText, UIElement content)
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var lbl = Label(labelText);
            Grid.SetColumn(lbl, 0);
            grid.Children.Add(lbl);
            Grid.SetColumn(content, 1);
            grid.Children.Add(content);
            return grid;
        }

        private Expander BuildLayout(LineSeries seed)
        {
            var inner = new StackPanel { Margin = new Thickness(8, 6, 8, 8) };

            // Pick packet + Import packet so the row's Topic / Field /
            // Source filter dropdowns can be populated from a real packet
            // without needing the operator to type from memory.
            var pickerRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            var pickBtn = new Button
            {
                Content = "Pick packet...",
                Padding = new Thickness(10, 3, 10, 3),
                Margin = new Thickness(0, 0, 8, 0),
                ToolTip = "Browse recorded packets from the selected channel and apply one to this series.",
            };
            pickBtn.Click += OnPickPacketClick;
            var importBtn = new Button
            {
                Content = "Import packet...",
                Padding = new Thickness(10, 3, 10, 3),
                Margin = new Thickness(0, 0, 0, 0),
                ToolTip = "Paste an XML packet to populate this series's Topic and Field.",
            };
            importBtn.Click += OnImportPacketClick;
            pickerRow.Children.Add(pickBtn);
            pickerRow.Children.Add(importBtn);
            inner.Children.Add(pickerRow);

            // Topic + Field on one row (always Exact match for the topic).
            // Topic label uses the same 120-px leading column as the other
            // LabeledRow rows so input fields line up vertically across rows.
            var topicFieldGrid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            topicFieldGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
            topicFieldGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            topicFieldGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            topicFieldGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var topicLbl = Label("Topic"); Grid.SetColumn(topicLbl, 0); topicFieldGrid.Children.Add(topicLbl);
            _topicCombo.Margin = new Thickness(0, 0, 16, 0);
            Grid.SetColumn(_topicCombo, 1); topicFieldGrid.Children.Add(_topicCombo);
            var fieldLbl = Label("Field"); fieldLbl.Margin = new Thickness(0, 0, 6, 0); Grid.SetColumn(fieldLbl, 2); topicFieldGrid.Children.Add(fieldLbl);
            Grid.SetColumn(_dataKeyCombo, 3); topicFieldGrid.Children.Add(_dataKeyCombo);
            inner.Children.Add(topicFieldGrid);

            // Series name on its own row.
            inner.Children.Add(LabeledRow("Series name", _nameBox));

            // Source filters live in an optional expander - same UX as the
            // main series, since most channels don't need source filtering.
            var sfInner = new StackPanel { Margin = new Thickness(8, 6, 8, 6) };
            var sfHint = new TextBlock
            {
                Text = "Restrict to Source/SimpleItem matches. Format: name1=value1;name2=value2",
                Foreground = new SolidColorBrush(Color.FromRgb(0x7A, 0x83, 0x88)),
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 6),
                TextWrapping = TextWrapping.Wrap,
            };
            sfInner.Children.Add(sfHint);
            sfInner.Children.Add(_sourceFiltersCombo);
            var sfExp = new Expander
            {
                Header = "Advanced - Source filter (per Source SimpleItem)",
                IsExpanded = !string.IsNullOrWhiteSpace(seed.SourceFilters),
                Content = sfInner,
                Margin = new Thickness(-6, 0, 0, 6),
                Foreground = new SolidColorBrush(Color.FromRgb(0xD0, 0xD0, 0xD0)),
            };
            inner.Children.Add(sfExp);

            // Line visual: color + thickness + type + Y axis + Fill/Markers all in one row.
            var lineContent = new StackPanel { Orientation = Orientation.Horizontal };
            lineContent.Children.Add(_color);
            var thLbl = Label("Thickness"); thLbl.Margin = new Thickness(16, 0, 6, 0);
            lineContent.Children.Add(thLbl);
            _thicknessBox.VerticalAlignment = VerticalAlignment.Center;
            lineContent.Children.Add(_thicknessBox);
            var typeLbl = Label("Type"); typeLbl.Margin = new Thickness(16, 0, 6, 0);
            lineContent.Children.Add(typeLbl);
            lineContent.Children.Add(_typeCombo);
            var yaxisLbl = Label("Y axis"); yaxisLbl.Margin = new Thickness(16, 0, 6, 0);
            lineContent.Children.Add(yaxisLbl);
            lineContent.Children.Add(_yAxisCombo);
            _fillCheck.Margin = new Thickness(16, 0, 12, 0);
            lineContent.Children.Add(_fillCheck);
            _markerCheck.Margin = new Thickness(0, 0, 0, 0);
            lineContent.Children.Add(_markerCheck);
            inner.Children.Add(LabeledRow("Line", lineContent));

            // Threshold: simple checkbox + collapsible single-row block.
            // Mirrors the main Line series UX (no nested expander).
            _thOnCheck.Margin = new Thickness(0, 8, 0, 6);
            inner.Children.Add(_thOnCheck);

            var thBlock = new StackPanel();
            var thRow = new StackPanel { Orientation = Orientation.Horizontal };
            var minLbl = Label("Min"); minLbl.Margin = new Thickness(0, 0, 6, 0);
            thRow.Children.Add(minLbl);
            _thMinBox.Width = 70; _thMinBox.Margin = new Thickness(0, 0, 8, 0);
            thRow.Children.Add(_thMinBox);
            var maxLbl = Label("Max"); maxLbl.Margin = new Thickness(0, 0, 6, 0);
            thRow.Children.Add(maxLbl);
            _thMaxBox.Width = 70; _thMaxBox.Margin = new Thickness(0, 0, 16, 0);
            thRow.Children.Add(_thMaxBox);
            var okLbl = Label("OK"); okLbl.Margin = new Thickness(0, 0, 6, 0);
            thRow.Children.Add(okLbl);
            _thColorOk.Margin = new Thickness(0, 0, 12, 0);
            thRow.Children.Add(_thColorOk);
            var warnLbl = Label("Warn"); warnLbl.Margin = new Thickness(0, 0, 6, 0);
            thRow.Children.Add(warnLbl);
            _thColorWarn.Margin = new Thickness(0, 0, 12, 0);
            thRow.Children.Add(_thColorWarn);
            var badLbl = Label("Bad"); badLbl.Margin = new Thickness(0, 0, 6, 0);
            thRow.Children.Add(badLbl);
            _thColorBad.Margin = new Thickness(0, 0, 16, 0);
            thRow.Children.Add(_thColorBad);
            _thHighIsBadCheck.VerticalAlignment = VerticalAlignment.Center;
            thRow.Children.Add(_thHighIsBadCheck);
            thBlock.Children.Add(thRow);

            thBlock.Visibility = (_thOnCheck.IsChecked == true) ? Visibility.Visible : Visibility.Collapsed;
            _thOnCheck.Checked += (s, e) => thBlock.Visibility = Visibility.Visible;
            _thOnCheck.Unchecked += (s, e) => thBlock.Visibility = Visibility.Collapsed;
            inner.Children.Add(thBlock);

            // Remove button
            var removeBtn = new Button { Content = "Remove this series", HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
            removeBtn.Click += (s, e) => OnRemove?.Invoke(this);
            inner.Children.Add(removeBtn);

            string headerText = string.IsNullOrWhiteSpace(seed.DisplayName) ? "(unnamed series)" : seed.DisplayName;
            var exp = new Expander
            {
                Header = headerText,
                IsExpanded = true,
                Content = inner,
                Margin = new Thickness(0, 4, 0, 4),
            };

            // Header reflects display name as the user types it.
            _nameBox.TextChanged += (s, e) =>
            {
                exp.Header = string.IsNullOrWhiteSpace(_nameBox.Text)
                    ? (string.IsNullOrEmpty(_dataKeyCombo.Text) ? "(unnamed series)" : _dataKeyCombo.Text)
                    : _nameBox.Text;
            };
            _dataKeyCombo.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent,
                new TextChangedEventHandler((s, e) =>
                {
                    if (string.IsNullOrWhiteSpace(_nameBox.Text))
                        exp.Header = string.IsNullOrEmpty(_dataKeyCombo.Text) ? "(unnamed series)" : _dataKeyCombo.Text;
                }));

            return exp;
        }

        private void HookEvents()
        {
            void Notify() { OnChanged?.Invoke(); }
            // ComboBox in editable mode needs both selection-change and the
            // inner TextBox change-event hooked, otherwise free-form typing
            // doesn't trigger the preview re-render.
            // Each combo's DropDownOpened pulls the latest snapshot and
            // refreshes ONLY that combo's items - rebuilding all three at
            // once (as a full ApplySnapshot does) clobbers _topicCombo's
            // Items mid-flight on a Field/Source-filter drop, which in
            // editable mode resets _topicCombo's coupling between
            // SelectedItem and Text and feeds the wrong topic back into the
            // Field filter.
            _topicCombo.DropDownOpened += (s, e) =>
            {
                if (_applyingSnapshot || _lastSnapshot == null) return;
                _applyingSnapshot = true;
                try { RefreshTopicComboItems(_lastSnapshot); }
                finally { _applyingSnapshot = false; }
            };
            _dataKeyCombo.DropDownOpened += (s, e) =>
            {
                if (_applyingSnapshot || _lastSnapshot == null) return;
                _applyingSnapshot = true;
                try { RefreshFieldComboItems(_lastSnapshot, _topicCombo.Text ?? string.Empty); }
                finally { _applyingSnapshot = false; }
            };
            _sourceFiltersCombo.DropDownOpened += (s, e) =>
            {
                if (_applyingSnapshot || _lastSnapshot == null) return;
                _applyingSnapshot = true;
                try { RefreshSourceFilterItems(_lastSnapshot, _topicCombo.Text ?? string.Empty); }
                finally { _applyingSnapshot = false; }
            };

            // Topic / Field / Source filter changes drive a per-row extractor
            // rebuild via OnExtractorChanged (NOT Notify, which would wipe
            // the line chart's bucket history on every keystroke).
            //
            // Hook ONLY TextChanged for the topic combo, not SelectionChanged.
            // Reasons:
            //   1. WPF editable ComboBoxes update Text whenever SelectedItem
            //      changes via dropdown pick, so TextChanged catches both
            //      typed input and dropdown picks.
            //   2. Hooking both events doubles up the work and creates a race
            //      where SelectionChanged sees the new SelectedItem but Text
            //      still holds the old value, briefly filtering the Field
            //      combo against the wrong topic.
            void NotifyExtractor() => OnExtractorChanged?.Invoke();
            _topicCombo.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent,
                new TextChangedEventHandler((s, e) => { RefilterFieldCombo(_topicCombo.Text ?? string.Empty); NotifyExtractor(); }));
            _sourceFiltersCombo.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent,
                new TextChangedEventHandler((s, e) => NotifyExtractor()));
            _dataKeyCombo.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent,
                new TextChangedEventHandler((s, e) => NotifyExtractor()));
            _nameBox.TextChanged += (s, e) => Notify();
            _color.ColorChanged += (s, e) => Notify();
            _thicknessBox.TextChanged += (s, e) => Notify();
            _typeCombo.SelectionChanged += (s, e) => Notify();
            _yAxisCombo.SelectionChanged += (s, e) => Notify();
            _fillCheck.Checked += (s, e) => Notify(); _fillCheck.Unchecked += (s, e) => Notify();
            _markerCheck.Checked += (s, e) => Notify(); _markerCheck.Unchecked += (s, e) => Notify();
            _thOnCheck.Checked += (s, e) => Notify(); _thOnCheck.Unchecked += (s, e) => Notify();
            _thMinBox.TextChanged += (s, e) => Notify();
            _thMaxBox.TextChanged += (s, e) => Notify();
            _thHighIsBadCheck.Checked += (s, e) => Notify(); _thHighIsBadCheck.Unchecked += (s, e) => Notify();
            _thColorOk.ColorChanged += (s, e) => Notify();
            _thColorWarn.ColorChanged += (s, e) => Notify();
            _thColorBad.ColorChanged += (s, e) => Notify();
        }
    }
}
