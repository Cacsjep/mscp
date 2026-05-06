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
    // Learn integration: Topic / Source filter / Data key are editable
    // ComboBoxes. ApplyLearnSnapshot pushes learned options into the dropdown
    // so the operator can pick from discovered values (same UX as the main
    // single-series fields). Free-form typing still works - the dropdown is
    // a hint, not a restriction.
    internal sealed class AdditionalLineSeriesRow
    {
        public Expander RootExpander { get; }
        public Action OnChanged;
        // Fired on UI thread when Topic / Field / Source filter text changes -
        // the configuration window uses this to rebuild the immutable
        // ExtractorConfig snapshot consumed on the source-callback thread.
        public Action OnExtractorChanged;
        public Action<AdditionalLineSeriesRow> OnRemove;
        // Set by the configuration window so the row can re-fetch the latest
        // learn snapshot every time the operator opens a dropdown - guarantees
        // dropdown items reflect current Learn state without manual refresh.
        public Func<LearnSnapshot> LearnSnapshotProvider;

        // Per-row independent learn session: each row can run its own learn
        // capture without overwriting the main series fields. The configuration
        // window owns the metadata source and fans out raw packets to every
        // row's session via Observe().
        public MetadataLearnSession LearnSession { get; } = new MetadataLearnSession();
        // Invoked when the row's Start Learn button is clicked - the
        // configuration window uses this to ensure the metadata channel is
        // picked and the source is started before learning begins.
        public Func<bool> OnStartLearnRequested;
        private Button _learnStartBtn;
        private Button _learnStopBtn;
        private TextBlock _learnStatus;

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

            LearnSession.Updated += snap =>
            {
                RootExpander.Dispatcher.BeginInvoke(new Action(() =>
                {
                    ApplyLearnSnapshot(snap);
                    if (_learnStatus != null)
                    {
                        int topicCount = snap.Topics.Count;
                        int keyCount = 0; foreach (var t in snap.Topics) keyCount += t.DataKeyExamples.Count;
                        _learnStatus.Text = $"Captured {snap.PacketsReceived} packet(s) - {topicCount} topic(s), {keyCount} key(s).";
                    }
                }));
            };
        }

        // UI-thread-only: build an immutable extractor config from the current
        // textbox/combo state. The configuration window calls this when the
        // row changes so the source thread has a stable snapshot to read.
        public ExtractorConfig BuildExtractorConfig()
        {
            return new ExtractorConfig
            {
                Topic = ResolveTopicFromSelection(),
                TopicMatchMode = "Exact",
                SourceFilters = ExtractorConfig.ParseSourceFilters(_sourceFiltersCombo.Text ?? string.Empty),
                DataKey = _dataKeyCombo.Text ?? string.Empty,
            };
        }

        public LineSeries ToLineSeries()
        {
            var s = new LineSeries
            {
                Topic = ResolveTopicFromSelection(),
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

        // Reentrancy guard: ApplyLearnSnapshot calls Items.Clear() which can
        // re-fire SelectionChanged on _topicCombo with a transient empty Text.
        // Without the guard, the inner call sees topicSelected=false and
        // repopulates dataKeyCombo with the union of all keys, which is
        // exactly the "Field shows wrong topic's keys" bug.
        private bool _applyingLearnSnapshot;

        // Refilter ONLY the Field dropdown for an explicit Topic value.
        // Callers pass the authoritative Topic string from SelectionChanged /
        // TextChanged - we don't read _topicCombo.Text here because in WPF
        // editable ComboBoxes the SelectionChanged event can fire BEFORE the
        // Text property is updated to reflect the picked item, so reading
        // Text inside the handler returns the PREVIOUS topic and the Field
        // dropdown ends up matching the wrong topic.
        private void RefilterDataKeyForTopic(string topicFilter)
        {
            if (_applyingLearnSnapshot) return;
            var snap = LearnSnapshotProvider?.Invoke();
            if (snap == null) return;
            topicFilter = topicFilter ?? string.Empty;
            bool topicSelected = !string.IsNullOrEmpty(topicFilter);
            var matchingKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in snap.Topics)
            {
                if (TopicMatches(t.Topic, topicFilter, "Exact"))
                    foreach (var dk in t.DataKeyExamples.Keys) matchingKeys.Add(dk);
            }
            if (!topicSelected)
            {
                foreach (var t in snap.Topics)
                    foreach (var dk in t.DataKeyExamples.Keys) matchingKeys.Add(dk);
            }
            string currentDataKey = _dataKeyCombo.Text ?? string.Empty;
            _dataKeyCombo.Items.Clear();
            foreach (var k in matchingKeys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                _dataKeyCombo.Items.Add(k);
            if (!string.IsNullOrEmpty(currentDataKey) && matchingKeys.Contains(currentDataKey))
                _dataKeyCombo.Text = currentDataKey;
            else if (topicSelected)
                _dataKeyCombo.Text = string.Empty;
            else
                _dataKeyCombo.Text = currentDataKey;
        }

        // SelectedItem is authoritative on SelectionChanged - it's set BEFORE
        // the event fires, regardless of whether the editable Text property
        // has caught up yet.
        private string ResolveTopicFromSelection()
        {
            if (_topicCombo.SelectedItem is string s && !string.IsNullOrEmpty(s)) return s;
            return _topicCombo.Text ?? string.Empty;
        }

        // Push learned topics / data keys / source-filter examples into the
        // dropdowns. Preserves whatever the user has typed in each field; new
        // items just get added as suggestions. Called by the configuration
        // window every time its LearnSnapshot updates.
        public void ApplyLearnSnapshot(LearnSnapshot snap)
        {
            if (snap == null) return;
            if (_applyingLearnSnapshot) return;
            _applyingLearnSnapshot = true;
            try { ApplyLearnSnapshotInner(snap); }
            finally { _applyingLearnSnapshot = false; }
        }

        private void ApplyLearnSnapshotInner(LearnSnapshot snap)
        {
            // Capture the operator's current Topic via the SelectedItem-first
            // resolver so that even if Text is mid-update from a recent dropdown
            // pick we restore and filter against the actual chosen topic.
            string currentTopic = ResolveTopicFromSelection();
            _topicCombo.Items.Clear();
            foreach (var topic in snap.Topics
                .Select(t => t.Topic)
                .Where(t => !string.IsNullOrEmpty(t))
                .OrderBy(t => t, StringComparer.OrdinalIgnoreCase))
            {
                _topicCombo.Items.Add(topic);
            }
            _topicCombo.Text = currentTopic;

            // Data key dropdown filtered to the topic currently typed in this
            // row, falling back to the union of all keys when the row's topic
            // is empty so the operator can still browse.
            var matchingKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string topicMatchMode = "Exact";
            string topicFilter = currentTopic;
            bool topicSelected = !string.IsNullOrEmpty(topicFilter);
            foreach (var t in snap.Topics)
            {
                if (TopicMatches(t.Topic, topicFilter, topicMatchMode))
                    foreach (var dk in t.DataKeyExamples.Keys) matchingKeys.Add(dk);
            }
            if (!topicSelected)
            {
                foreach (var t in snap.Topics)
                    foreach (var dk in t.DataKeyExamples.Keys) matchingKeys.Add(dk);
            }
            string currentDataKey = _dataKeyCombo.Text ?? string.Empty;
            _dataKeyCombo.Items.Clear();
            foreach (var k in matchingKeys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                _dataKeyCombo.Items.Add(k);
            // Drop the typed Field if it isn't a key under the now-selected
            // Topic - otherwise switching Topic A -> Topic B leaves Field
            // showing A's key, which the operator reads as "the selection
            // didn't take." Mirrors the main-config RefreshDataKeyCombo logic.
            if (!string.IsNullOrEmpty(currentDataKey) && matchingKeys.Contains(currentDataKey))
                _dataKeyCombo.Text = currentDataKey;
            else if (topicSelected)
                _dataKeyCombo.Text = string.Empty;
            else
                _dataKeyCombo.Text = currentDataKey;

            // Source-filter suggestions: show learned "name=value" examples
            // for the matched topic so the operator can copy/edit one.
            string currentSourceFilter = _sourceFiltersCombo.Text ?? string.Empty;
            _sourceFiltersCombo.Items.Clear();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in snap.Topics)
            {
                if (!TopicMatches(t.Topic, topicFilter, topicMatchMode) && topicSelected) continue;
                foreach (var sv in t.SourceValues)
                {
                    foreach (var v in sv.Value)
                    {
                        string suggestion = sv.Key + "=" + v;
                        if (seen.Add(suggestion)) _sourceFiltersCombo.Items.Add(suggestion);
                    }
                }
            }
            _sourceFiltersCombo.Text = currentSourceFilter;
        }

        private static bool TopicMatches(string topic, string filter, string mode)
        {
            if (string.IsNullOrEmpty(filter)) return true;
            if (string.IsNullOrEmpty(topic)) return false;
            switch (mode)
            {
                case "Exact":    return string.Equals(topic, filter, StringComparison.OrdinalIgnoreCase);
                case "EndsWith": return topic.EndsWith(filter, StringComparison.OrdinalIgnoreCase);
                default:         return topic.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
            }
        }

        private static double? TryParseNullable(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? (double?)v : null;
        }

        private static void SelectComboByContent(ComboBox combo, string content)
        {
            if (string.IsNullOrEmpty(content)) { combo.SelectedIndex = 0; return; }
            foreach (var it in combo.Items)
            {
                if (it is ComboBoxItem ci && string.Equals(ci.Content?.ToString(), content, StringComparison.OrdinalIgnoreCase))
                {
                    combo.SelectedItem = ci;
                    return;
                }
            }
            combo.SelectedIndex = 0;
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

            // Per-row Start/Stop Learn so the operator can capture topics +
            // fields specific to this series without disturbing the main
            // series fields. The session is owned by this row and fed by
            // the configuration window's packet fan-out.
            var learnRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            _learnStartBtn = new Button { Content = "Start Learn", Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 0, 8, 0) };
            _learnStopBtn = new Button { Content = "Stop", Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 0, 12, 0), IsEnabled = false };
            _learnStartBtn.Click += (s, e) =>
            {
                bool ok = OnStartLearnRequested?.Invoke() ?? false;
                if (!ok) return;
                LearnSession.Reset();
                LearnSession.Start();
                _learnStartBtn.IsEnabled = false;
                _learnStopBtn.IsEnabled = true;
                if (_learnStatus != null) _learnStatus.Text = "Listening - waiting for first packet...";
            };
            _learnStopBtn.Click += (s, e) =>
            {
                LearnSession.Stop();
                _learnStartBtn.IsEnabled = true;
                _learnStopBtn.IsEnabled = false;
            };
            learnRow.Children.Add(_learnStartBtn);
            learnRow.Children.Add(_learnStopBtn);
            _learnStatus = new TextBlock
            {
                Text = "Idle. Start Learn to discover topics and fields for this series only.",
                Foreground = new SolidColorBrush(Color.FromRgb(0x7A, 0x83, 0x88)),
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
            };
            learnRow.Children.Add(_learnStatus);
            inner.Children.Add(learnRow);

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
            // Re-apply the latest learn snapshot just before dropping the
            // list down, so the operator always sees the freshest options
            // without having to close + reopen the row.
            EventHandler refreshSnapshot = (s, e) =>
            {
                var snap = LearnSnapshotProvider?.Invoke();
                if (snap != null) ApplyLearnSnapshot(snap);
            };
            _topicCombo.DropDownOpened += refreshSnapshot;
            _sourceFiltersCombo.DropDownOpened += refreshSnapshot;
            _dataKeyCombo.DropDownOpened += refreshSnapshot;

            // Topic / Field / Source filter changes don't trigger Notify()
            // (which would wipe the chart's bucket history on every keystroke).
            // They fire OnExtractorChanged instead so the window can rebuild
            // the per-row ExtractorConfig snapshot on the UI thread.
            void NotifyExtractor() => OnExtractorChanged?.Invoke();
            // Topic changes also have to refilter THIS row's Field dropdown
            // (and clear an invalid typed Field). We DON'T call the full
            // ApplyLearnSnapshot here: that one rebuilds the Topic combo's
            // own Items, which during Items.Clear() re-fires SelectionChanged
            // with a transient empty Text and clobbers the user's pick. Only
            // the Field combo needs to refresh on Topic change.
            _topicCombo.SelectionChanged += (s, e) => { RefilterDataKeyForTopic(ResolveTopicFromSelection()); NotifyExtractor(); };
            _topicCombo.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent,
                new TextChangedEventHandler((s, e) => { RefilterDataKeyForTopic(_topicCombo.Text ?? string.Empty); NotifyExtractor(); }));
            _sourceFiltersCombo.SelectionChanged += (s, e) => NotifyExtractor();
            _sourceFiltersCombo.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent,
                new TextChangedEventHandler((s, e) => NotifyExtractor()));
            _dataKeyCombo.SelectionChanged += (s, e) => NotifyExtractor();
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
