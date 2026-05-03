using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.Xml;
using System.Xml.Linq;
using CommunitySDK;
using FontAwesome5;
using MetadataDisplay.Client.Renderers;
using VideoOS.Platform;
using VideoOS.Platform.Live;
using VideoOS.Platform.UI;

namespace MetadataDisplay.Client
{
    public partial class MetadataDisplayConfigurationWindow : Window
    {
        private static readonly PluginLog _log = new PluginLog("MetadataDisplay");

        private readonly MetadataDisplayViewItemManager _vim;

        private Item _metadataItem;
        private string _metadataIdString = "";
        private string _metadataNameString = "";

        // Single shared MetadataLiveSource: feeds both preview render and learn aggregator.
        private MetadataLiveSource _source;
        private int _packetsSeen;

        // Snapshot of the extractor config built on the UI thread; read lock-free from
        // the background LiveContentEvent callback to avoid touching WPF controls there.
        private volatile ExtractorConfig _extractorSnapshot;

        // Learn is just an aggregator now; the source is shared.
        private readonly MetadataLearnSession _learn = new MetadataLearnSession();

        private DateTime? _lastPreviewUtc;
        private string _lastPreviewValue;
        private DispatcherTimer _ageTicker;
        private bool _uiReady;

        // Live preview renderers (mirror the actual ViewItem widget)
        private LampRenderer _previewLamp;
        private NumberRenderer _previewNumber;
        private GaugeRenderer _previewGauge;
        private TextRenderer _previewText;

        // Color pickers (one per color slot)
        private ColorPickerControl _colorOk, _colorWarn, _colorBad;
        private ColorPickerControl _gaugeColorOk, _gaugeColorWarn, _gaugeColorBad;
        private ColorPickerControl _titleColor;

        public MetadataDisplayConfigurationWindow(MetadataDisplayViewItemManager viewItemManager)
        {
            _vim = viewItemManager;
            InitializeComponent();
            _learn.Updated += OnLearnUpdated;
            Loaded += (s, e) =>
            {
                _log.Info("[ConfigWindow] Loaded");
                _uiReady = true;
                Hydrate();
            };
            Closed += (s, e) => { _log.Info("[ConfigWindow] Closed"); Teardown(); };
        }

        // ───────── Hydrate ─────────

        private void Hydrate()
        {
            // Color pickers — instantiate once and host them in the placeholder Grids.
            _colorOk = MountColorPicker(colorOkPickerHost);
            _colorWarn = MountColorPicker(colorWarnPickerHost);
            _colorBad = MountColorPicker(colorBadPickerHost);
            _gaugeColorOk = MountColorPicker(gaugeColorOkPickerHost);
            _gaugeColorWarn = MountColorPicker(gaugeColorWarnPickerHost);
            _gaugeColorBad = MountColorPicker(gaugeColorBadPickerHost);
            _titleColor = MountColorPicker(titleColorPickerHost);

            // Title section
            SelectComboItem(densityCombo, _vim.WidgetDensity ?? "Comfortable");
            showTitleCheck.IsChecked = !string.Equals(_vim.ShowTitle, "false", StringComparison.OrdinalIgnoreCase);
            titleBox.Text = _vim.Title ?? "";
            SelectComboItem(titlePositionCombo, _vim.TitlePosition ?? "Left");
            titleFontSizeBox.Text = _vim.TitleFontSize ?? "14";
            _titleColor.HexValue = _vim.TitleColor ?? "#FFCFD7DA";
            ApplyTitleEnabled();

            _metadataIdString = _vim.MetadataId ?? "";
            _metadataNameString = _vim.MetadataName ?? "";
            ResolveMetadataItem();
            UpdateChannelLabel();

            topicCombo.Text = _vim.Topic ?? "";
            SelectComboItem(topicMatchModeCombo, _vim.TopicMatchMode ?? "Contains");
            dataKeyCombo.Text = _vim.DataKey ?? "";
            sourceFilterBox.Text = _vim.SourceFilters ?? "";
            sourceFilterExpander.IsExpanded = !string.IsNullOrEmpty(_vim.SourceFilters);

            switch ((_vim.RenderType ?? "Lamp"))
            {
                case "Number": rtNumber.IsChecked = true; break;
                case "Gauge":  rtGauge.IsChecked = true; break;
                case "Text":   rtText.IsChecked = true; break;
                default:       rtLamp.IsChecked = true; break;
            }

            RebuildLampRowsFromManager();
            lampIconSizeBox.Text = _vim.LampIconSize ?? "96";
            textFontSizeBox.Text = _vim.TextFontSize ?? "28";

            numMinBox.Text = _vim.NumMin ?? "";
            numMaxBox.Text = _vim.NumMax ?? "";
            highIsBadCheck.IsChecked = !string.Equals(_vim.NumDirection, "LowIsBad", StringComparison.OrdinalIgnoreCase);
            _colorOk.HexValue = _vim.ColorOk ?? "#3CB371";
            _colorWarn.HexValue = _vim.ColorWarn ?? "#E69500";
            _colorBad.HexValue = _vim.ColorBad ?? "#D8392C";
            unitBoxNumber.Text = _vim.Unit ?? "";

            gaugeRangeMinBox.Text = _vim.GaugeRangeMin ?? "0";
            gaugeRangeMaxBox.Text = _vim.GaugeRangeMax ?? "100";
            SelectComboItem(gaugeStyleCombo, _vim.GaugeStyle ?? "Arc180");
            gaugeNumMinBox.Text = _vim.NumMin ?? "";
            gaugeNumMaxBox.Text = _vim.NumMax ?? "";
            gaugeHighIsBadCheck.IsChecked = highIsBadCheck.IsChecked;
            _gaugeColorOk.HexValue = _colorOk.HexValue;
            _gaugeColorWarn.HexValue = _colorWarn.HexValue;
            _gaugeColorBad.HexValue = _colorBad.HexValue;
            unitBoxGauge.Text = _vim.Unit ?? "";
            gaugeShowValueCheck.IsChecked = !string.Equals(_vim.GaugeShowValue, "false", StringComparison.OrdinalIgnoreCase);
            gaugeValueFontSizeBox.Text = _vim.GaugeValueFontSize ?? "34";
            gaugeShowTicksCheck.IsChecked = string.Equals(_vim.GaugeShowTicks, "true", StringComparison.OrdinalIgnoreCase);
            gaugeTickCountBox.Text = _vim.GaugeTickCount ?? "10";
            gaugeTrackThicknessBox.Text = _vim.GaugeTrackThickness ?? "14";

            staleSecondsBox.Text = _vim.StaleSeconds ?? "0";

            ApplyRenderTypeVisibility();
            BuildPreviewHost();
            RebuildExtractorSnapshot();
            InstallNumericValidation();
            StartSourceIfReady();
            EnsureAgeTicker();

            // Re-render whenever fields that affect the preview change.
            HookFieldChangeHandlers();

            UpdatePreviewTitle();
        }

        // Builds the extractor config from the WPF controls. MUST be called on the UI
        // thread; the resulting immutable snapshot is then safe to read from any thread.
        private void RebuildExtractorSnapshot()
        {
            var mode = (topicMatchModeCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Contains";
            _extractorSnapshot = new ExtractorConfig
            {
                Topic = topicCombo.Text ?? "",
                TopicMatchMode = mode,
                SourceFilters = ExtractorConfig.ParseSourceFilters(sourceFilterBox.Text ?? ""),
                DataKey = dataKeyCombo.Text ?? "",
            };
        }

        private ColorPickerControl MountColorPicker(Grid host)
        {
            var picker = new ColorPickerControl();
            host.Children.Clear();
            host.Children.Add(picker);
            picker.ColorChanged += (s, e) => ReRenderPreview();
            return picker;
        }

        private void ApplyTitleEnabled()
        {
            bool show = showTitleCheck.IsChecked == true;
            titleConfigPanel.IsEnabled = show;
            titleConfigPanel.Opacity = show ? 1.0 : 0.5;
        }

        private void UpdatePreviewTitle()
        {
            bool show = showTitleCheck.IsChecked == true;
            var text = titleBox.Text ?? "";
            if (!show || string.IsNullOrEmpty(text))
            {
                previewTitleText.Visibility = Visibility.Collapsed;
                return;
            }
            previewTitleText.Visibility = Visibility.Visible;
            previewTitleText.Text = text;

            // Position
            switch ((titlePositionCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Left")
            {
                case "Center": previewTitleText.HorizontalAlignment = HorizontalAlignment.Center; previewTitleText.TextAlignment = TextAlignment.Center; break;
                case "Right":  previewTitleText.HorizontalAlignment = HorizontalAlignment.Right;  previewTitleText.TextAlignment = TextAlignment.Right;  break;
                default:       previewTitleText.HorizontalAlignment = HorizontalAlignment.Left;   previewTitleText.TextAlignment = TextAlignment.Left;   break;
            }
            // Font size — density scales the title alongside the rest of the widget.
            double baseFs = 14;
            if (double.TryParse(titleFontSizeBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var fs) && fs > 0)
                baseFs = fs;
            string density = (densityCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Comfortable";
            previewTitleText.FontSize = baseFs * Renderers.WidgetTheme.DensityScale(density);
            // Color
            try
            {
                var c = (Color)ColorConverter.ConvertFromString(_titleColor.HexValue);
                previewTitleText.Foreground = new SolidColorBrush(c);
            }
            catch { }
        }

        // ───────── Channel ─────────

        private void OnPickChannel(object sender, RoutedEventArgs e)
        {
            try
            {
                var picker = new ItemPickerWpfWindow
                {
                    Items = Configuration.Instance.GetItemsByKind(Kind.Metadata),
                    KindsFilter = new List<Guid> { Kind.Metadata },
                    SelectionMode = SelectionModeOptions.AutoCloseOnSelect,
                };
                if (picker.ShowDialog() == true && picker.SelectedItems != null && picker.SelectedItems.Any())
                {
                    StopSource();
                    _learn.Reset();
                    _learn.Stop();
                    learnStartButton.IsEnabled = true;
                    learnStopButton.IsEnabled = false;
                    learnStatus.Text = "Idle. Pick a Topic + Data key from the discovered list, or click Start Learn.";

                    _metadataItem = picker.SelectedItems.First();
                    _metadataIdString = _metadataItem.FQID.ObjectId.ToString();
                    _metadataNameString = _metadataItem.Name ?? "";
                    UpdateChannelLabel();
                    StartSourceIfReady();
                }
            }
            catch (Exception ex)
            {
                _log.Error($"Pick channel failed: {ex.Message}", ex);
                MessageBox.Show(this, "Could not open the channel picker:\n" + ex.Message, "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ResolveMetadataItem()
        {
            _metadataItem = null;
            if (Guid.TryParse(_metadataIdString, out var id) && id != Guid.Empty)
            {
                try { _metadataItem = Configuration.Instance.GetItem(id, Kind.Metadata); }
                catch (Exception ex) { _log.Error($"Resolve metadata item failed: {ex.Message}"); }
            }
        }

        private void UpdateChannelLabel()
        {
            if (_metadataItem != null)
                channelLabel.Text = _metadataItem.Name;
            else if (!string.IsNullOrEmpty(_metadataNameString))
                channelLabel.Text = _metadataNameString + "  (not found)";
            else
                channelLabel.Text = "(none selected)";
        }

        // ───────── Render type sub-panels ─────────

        private void OnRenderTypeChanged(object sender, RoutedEventArgs e)
        {
            if (!_uiReady) return;
            ApplyRenderTypeVisibility();
            BuildPreviewHost();
            ReRenderPreview();
        }

        private void ApplyRenderTypeVisibility()
        {
            if (lampPanel == null || numberPanel == null || gaugePanel == null || textPanel == null) return;
            lampPanel.Visibility   = rtLamp.IsChecked   == true ? Visibility.Visible : Visibility.Collapsed;
            numberPanel.Visibility = rtNumber.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            gaugePanel.Visibility  = rtGauge.IsChecked  == true ? Visibility.Visible : Visibility.Collapsed;
            textPanel.Visibility   = rtText.IsChecked   == true ? Visibility.Visible : Visibility.Collapsed;
        }

        private void BuildPreviewHost()
        {
            if (previewHost == null) return;
            previewHost.Children.Clear();
            _previewLamp = null;
            _previewNumber = null;
            _previewGauge = null;
            _previewText = null;

            UIElement visual;
            if (rtNumber.IsChecked == true)
            {
                _previewNumber = new NumberRenderer();
                visual = _previewNumber.Visual;
                _previewNumber.Clear();
            }
            else if (rtGauge.IsChecked == true)
            {
                _previewGauge = new GaugeRenderer();
                visual = _previewGauge.Visual;
                _previewGauge.Clear();
            }
            else if (rtText.IsChecked == true)
            {
                _previewText = new TextRenderer();
                visual = _previewText.Visual;
                _previewText.Clear();
            }
            else
            {
                _previewLamp = new LampRenderer();
                visual = _previewLamp.Visual;
                _previewLamp.Clear();
            }

            previewHost.Children.Add(visual);
        }

        private void HookFieldChangeHandlers()
        {
            // Re-render when values that affect colors/labels/thresholds change.
            // Topic/Source/DataKey changes also re-extract from the cached XML so
            // the preview reflects the new selection without waiting for a fresh packet.
            topicCombo.SelectionChanged += (s, e) => { RebuildExtractorSnapshot(); RefreshDataKeyCombo(); RefreshLearnedSourceList(); ReExtractAndRender(); };
            topicCombo.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent,
                new TextChangedEventHandler((s, e) => { RebuildExtractorSnapshot(); RefreshDataKeyCombo(); RefreshLearnedSourceList(); ReExtractAndRender(); }));
            topicMatchModeCombo.SelectionChanged += (s, e) => { RebuildExtractorSnapshot(); RefreshDataKeyCombo(); RefreshLearnedSourceList(); ReExtractAndRender(); };
            dataKeyCombo.SelectionChanged += (s, e) => { RebuildExtractorSnapshot(); ReExtractAndRender(); };
            dataKeyCombo.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent,
                new TextChangedEventHandler((s, e) => { RebuildExtractorSnapshot(); ReExtractAndRender(); }));
            sourceFilterBox.TextChanged += (s, e) => { RebuildExtractorSnapshot(); ReExtractAndRender(); };
            numMinBox.TextChanged += (s, e) => ReRenderPreview();
            numMaxBox.TextChanged += (s, e) => ReRenderPreview();
            unitBoxNumber.TextChanged += (s, e) => ReRenderPreview();
            gaugeRangeMinBox.TextChanged += (s, e) => ReRenderPreview();
            gaugeRangeMaxBox.TextChanged += (s, e) => ReRenderPreview();
            gaugeNumMinBox.TextChanged += (s, e) => ReRenderPreview();
            gaugeNumMaxBox.TextChanged += (s, e) => ReRenderPreview();
            unitBoxGauge.TextChanged += (s, e) => ReRenderPreview();
            gaugeStyleCombo.SelectionChanged += (s, e) => { BuildPreviewHost(); ReRenderPreview(); };
            highIsBadCheck.Checked += (s, e) => ReRenderPreview();
            highIsBadCheck.Unchecked += (s, e) => ReRenderPreview();
            gaugeHighIsBadCheck.Checked += (s, e) => ReRenderPreview();
            gaugeHighIsBadCheck.Unchecked += (s, e) => ReRenderPreview();
            gaugeShowValueCheck.Checked += (s, e) => ReRenderPreview();
            gaugeShowValueCheck.Unchecked += (s, e) => ReRenderPreview();
            gaugeValueFontSizeBox.TextChanged += (s, e) => ReRenderPreview();
            gaugeShowTicksCheck.Checked += (s, e) => ReRenderPreview();
            gaugeShowTicksCheck.Unchecked += (s, e) => ReRenderPreview();
            gaugeTickCountBox.TextChanged += (s, e) => ReRenderPreview();
            gaugeTrackThicknessBox.TextChanged += (s, e) => ReRenderPreview();
            lampIconSizeBox.TextChanged += (s, e) => ReRenderPreview();
            textFontSizeBox.TextChanged += (s, e) => ReRenderPreview();
            densityCombo.SelectionChanged += (s, e) => { UpdatePreviewTitle(); ReRenderPreview(); };

            // Title section live preview
            showTitleCheck.Checked += (s, e) => { ApplyTitleEnabled(); UpdatePreviewTitle(); };
            showTitleCheck.Unchecked += (s, e) => { ApplyTitleEnabled(); UpdatePreviewTitle(); };
            titleBox.TextChanged += (s, e) => UpdatePreviewTitle();
            titlePositionCombo.SelectionChanged += (s, e) => UpdatePreviewTitle();
            titleFontSizeBox.TextChanged += (s, e) => UpdatePreviewTitle();
            _titleColor.ColorChanged += (s, e) => UpdatePreviewTitle();
        }

        // ───────── Lamp rows ─────────

        private sealed class LampRowControls
        {
            public Grid Container;
            public TextBox ValueBox;
            public TextBox LabelBox;
            public ColorPickerControl ColorPicker;
            public Button IconButton;
            public string IconName;
        }

        private readonly List<LampRowControls> _lampRowControls = new List<LampRowControls>();

        private void RebuildLampRowsFromManager()
        {
            _lampRowControls.Clear();
            var rows = LampMapParser.Parse(_vim.LampMap);
            lampRows.Items.Clear();
            if (rows.Count == 0)
            {
                lampRows.Items.Add(BuildLampRow("0", "Off", "#777777", "").Container);
                lampRows.Items.Add(BuildLampRow("1", "On", "#3CB371", "").Container);
            }
            else
            {
                foreach (var r in rows)
                    lampRows.Items.Add(BuildLampRow(r.Value, r.Label, r.ColorHex, r.IconName).Container);
            }
        }

        private LampRowControls BuildLampRow(string value, string label, string color, string iconName)
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var valueBox = new TextBox { Text = value, Padding = new Thickness(3, 2, 3, 2), Margin = new Thickness(0, 0, 6, 0) };
            var labelBox = new TextBox { Text = label, Padding = new Thickness(3, 2, 3, 2), Margin = new Thickness(0, 0, 6, 0) };

            var picker = new ColorPickerControl { Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
            picker.HexValue = color;

            var ctrls = new LampRowControls
            {
                Container = grid,
                ValueBox = valueBox,
                LabelBox = labelBox,
                ColorPicker = picker,
                IconName = iconName ?? "",
            };

            var iconButton = new Button
            {
                Content = BuildIconButtonContent(ctrls.IconName),
                Padding = new Thickness(6, 2, 6, 2),
                Margin = new Thickness(0, 0, 6, 0),
                ToolTip = "Pick icon",
                Width = 60,
            };
            ctrls.IconButton = iconButton;
            iconButton.Click += (s, e) => OpenIconPickerForRow(ctrls);

            valueBox.TextChanged += (s, e) => ReRenderPreview();
            labelBox.TextChanged += (s, e) => ReRenderPreview();
            picker.ColorChanged += (s, e) => ReRenderPreview();

            var removeButton = new Button { Content = "Remove", Padding = new Thickness(8, 2, 8, 2) };
            removeButton.Click += (s, e) =>
            {
                lampRows.Items.Remove(grid);
                _lampRowControls.Remove(ctrls);
                ReRenderPreview();
            };

            Grid.SetColumn(valueBox, 0);
            Grid.SetColumn(labelBox, 1);
            Grid.SetColumn(picker, 2);
            Grid.SetColumn(iconButton, 3);
            Grid.SetColumn(removeButton, 4);
            grid.Children.Add(valueBox);
            grid.Children.Add(labelBox);
            grid.Children.Add(picker);
            grid.Children.Add(iconButton);
            grid.Children.Add(removeButton);

            _lampRowControls.Add(ctrls);
            return ctrls;
        }

        private static UIElement BuildIconButtonContent(string iconName)
        {
            if (LampMapParser.TryParseIcon(iconName, out var fa))
            {
                return new ImageAwesome
                {
                    Icon = fa,
                    Width = 16,
                    Height = 16,
                    Foreground = new SolidColorBrush(Color.FromRgb(0xD0, 0xD0, 0xD0)),
                };
            }
            return new TextBlock { Text = "Icon", Foreground = new SolidColorBrush(Color.FromRgb(0xD0, 0xD0, 0xD0)) };
        }

        private void OpenIconPickerForRow(LampRowControls ctrls)
        {
            var initial = EFontAwesomeIcon.None;
            LampMapParser.TryParseIcon(ctrls.IconName, out initial);
            var dlg = new IconPickerWindow(initial) { Owner = this };
            if (dlg.ShowDialog() == true)
            {
                ctrls.IconName = dlg.SelectedIcon == EFontAwesomeIcon.None ? "" : dlg.SelectedIcon.ToString();
                ctrls.IconButton.Content = BuildIconButtonContent(ctrls.IconName);
                ReRenderPreview();
            }
        }

        private void OnAddLampRow(object sender, RoutedEventArgs e)
        {
            var row = BuildLampRow("", "", "#3CB371", "");
            lampRows.Items.Add(row.Container);
        }

        private string SerializeLampRows()
        {
            var entries = new List<LampMapEntry>();
            foreach (var ctrls in _lampRowControls)
            {
                var v = ctrls.ValueBox.Text?.Trim() ?? "";
                if (string.IsNullOrEmpty(v)) continue;
                entries.Add(new LampMapEntry
                {
                    Value = v,
                    Label = ctrls.LabelBox.Text?.Trim() ?? "",
                    ColorHex = ctrls.ColorPicker.HexValue,
                    IconName = ctrls.IconName ?? "",
                });
            }
            return LampMapParser.Serialize(entries);
        }

        // ───────── Single shared MetadataLiveSource ─────────

        private DateTime _sourceStartedUtc;
        private DispatcherTimer _diagTicker;

        private void StartSourceIfReady()
        {
            StopSource();
            if (_metadataItem == null)
            {
                previewStatusText.Text = "Pick a metadata channel to start streaming.";
                _log.Info("[ConfigWindow] StartSource skipped (no channel)");
                return;
            }

            try
            {
                _packetsSeen = 0;
                _sourceStartedUtc = DateTime.UtcNow;
                _log.Info($"[ConfigWindow] StartSource channel='{_metadataItem.Name}' id={_metadataItem.FQID.ObjectId}");
                _source = new MetadataLiveSource(_metadataItem) { LiveModeStart = true };
                _source.Init();
                _source.LiveContentEvent += OnSourceContent;
                _source.ErrorEvent += OnSourceError;
                _log.Info("[ConfigWindow] Source subscribed");
                previewStatusText.Text = $"Subscribed to '{_metadataItem.Name}'. Waiting for first packet...";

                EnsureDiagTicker();
                SeedPreviewFromCache();
            }
            catch (Exception ex)
            {
                _log.Error($"[ConfigWindow] StartSource failed: {ex.Message}", ex);
                _source = null;
                previewStatusText.Text = "Could not subscribe: " + ex.Message;
            }
        }

        private void EnsureDiagTicker()
        {
            if (_diagTicker != null) return;
            _diagTicker = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _diagTicker.Tick += (s, e) => RefreshDiagStatus();
            _diagTicker.Start();
        }

        private void RefreshDiagStatus()
        {
            if (_source == null) return;
            if (_packetsSeen > 0) return; // healthy stream messaging is handled in OnSourceContent

            var elapsed = (int)(DateTime.UtcNow - _sourceStartedUtc).TotalSeconds;
            previewStatusText.Text =
                $"Subscribed to '{_metadataItem?.Name}'. No packets received in {elapsed}s. " +
                "If your camera publishes events sparsely (loitering, IO change), this is normal until something happens. " +
                "Check the channel is enabled and recording metadata.";
        }

        private void StopSource()
        {
            if (_source == null) return;
            _log.Info($"[ConfigWindow] StopSource packets={_packetsSeen}");
            try
            {
                _source.LiveContentEvent -= OnSourceContent;
                _source.ErrorEvent -= OnSourceError;
                _source.Close();
            }
            catch { }
            _source = null;
            _diagTicker?.Stop();
            _diagTicker = null;
        }

        private void SeedPreviewFromCache()
        {
            if (_metadataItem == null) { _log.Info("[ConfigWindow] Seed skipped (no item)"); return; }
            if (!LastXmlCache.TryGet(_metadataItem.FQID.ObjectId, out var xml, out var cachedAt))
            {
                _log.Info("[ConfigWindow] Seed: no cached XML available");
                return;
            }

            if (_learn.IsActive) _learn.Observe(xml);

            var cfg = BuildExtractorCfgFromUi();
            if (string.IsNullOrEmpty(cfg.DataKey))
            {
                _log.Info("[ConfigWindow] Seed: cached XML found but DataKey is empty");
                return;
            }
            var hit = MetadataExtractor.TryExtract(xml, cfg);
            if (hit == null)
            {
                _log.Info($"[ConfigWindow] Seed: cached XML found but no match for topic='{cfg.Topic}' key='{cfg.DataKey}'");
                return;
            }

            _log.Info($"[ConfigWindow] Seed: rendered cached value '{hit.Value}'");
            _lastPreviewUtc = hit.TimestampUtc;
            _lastPreviewValue = hit.Value;
            previewStatusText.Text =
                $"Showing cached value from {FormatAge(DateTime.UtcNow - cachedAt)} ago - waiting for next packet...";
            ReRenderPreview();
        }

        private void OnSourceContent(MetadataLiveSource source, MetadataLiveContent content)
        {
            try
            {
                _packetsSeen++;
                string xml = content?.Content?.GetMetadataString();
                if (string.IsNullOrEmpty(xml))
                {
                    _log.Info($"[ConfigWindow] Packet #{_packetsSeen} empty");
                    return;
                }

                if (_metadataItem != null)
                    LastXmlCache.Put(_metadataItem.FQID.ObjectId, xml);

                if (_packetsSeen <= 3 || _packetsSeen % 50 == 0)
                    _log.Info($"[ConfigWindow] Packet #{_packetsSeen} bytes={xml.Length}");

                // Learn aggregation
                if (_learn.IsActive) _learn.Observe(xml);

                // Preview extraction — read the UI-thread-built snapshot, NOT the controls.
                var cfg = _extractorSnapshot;
                if (cfg == null || string.IsNullOrEmpty(cfg.DataKey))
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        previewStatusText.Text = $"Streaming - {_packetsSeen} packet(s) received. Pick a Data key to render.";
                    }));
                    return;
                }

                var hit = MetadataExtractor.TryExtract(xml, cfg);

                if (hit == null && (_packetsSeen <= 3 || _packetsSeen % 50 == 0))
                {
                    var dump = DumpXmlContents(xml);
                    _log.Info($"[ConfigWindow] No match for topic='{cfg.Topic}' mode={cfg.TopicMatchMode} key='{cfg.DataKey}'. Packet contains: {dump}");
                }

                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (hit != null)
                    {
                        _lastPreviewUtc = hit.TimestampUtc;
                        _lastPreviewValue = hit.Value;
                        previewStatusText.Text = $"Streaming - {_packetsSeen} packet(s).";
                        ReRenderPreview();
                    }
                    else
                    {
                        previewStatusText.Text =
                            $"Streaming - {_packetsSeen} packet(s). No match yet for current Topic/Source/Data key.";
                    }
                }));
            }
            catch (Exception ex)
            {
                _log.Error($"OnSourceContent threw: {ex.Message}", ex);
            }
        }

        private void OnSourceError(MetadataLiveSource source, Exception ex)
        {
            _log.Error($"[ConfigWindow] Source error: {ex?.GetType().Name}: {ex?.Message}");
            Dispatcher.BeginInvoke(new Action(() =>
                previewStatusText.Text = "Stream error: " + (ex?.Message ?? "(unknown)")));
        }

        // ───────── Learn ─────────

        private void OnStartLearn(object sender, RoutedEventArgs e)
        {
            if (_metadataItem == null)
            {
                MessageBox.Show(this, "Pick a metadata channel first.", "Learn",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            // Learn just toggles aggregation on the shared source; if no source yet, start one.
            if (_source == null) StartSourceIfReady();

            _learn.Reset();
            _learn.Start();
            learnStartButton.IsEnabled = false;
            learnStopButton.IsEnabled = true;
            learnStatus.Text = "Listening - waiting for first packet...";
        }

        private void OnStopLearn(object sender, RoutedEventArgs e)
        {
            _learn.Stop();
            learnStartButton.IsEnabled = true;
            learnStopButton.IsEnabled = false;
        }

        private void OnLearnUpdated(LearnSnapshot snap)
        {
            Dispatcher.BeginInvoke(new Action(() => ApplyLearnSnapshot(snap)));
        }

        // Last received learn snapshot — kept so we can re-filter the Data key dropdown
        // whenever the user changes Topic / TopicMatchMode without needing a fresh packet.
        private LearnSnapshot _lastSnapshot;

        private void ApplyLearnSnapshot(LearnSnapshot snap)
        {
            _lastSnapshot = snap;

            int topicCount = snap.Topics.Count;
            int keyCount = 0;
            int srcCount = 0;
            foreach (var t in snap.Topics)
            {
                keyCount += t.DataKeyExamples.Count;
                foreach (var sv in t.SourceValues) srcCount += sv.Value.Count;
            }
            learnStatus.Text =
                $"Captured {snap.PacketsReceived} packet(s) - {topicCount} topic(s), {keyCount} key(s), {srcCount} source value(s).";

            string currentTopicText = topicCombo.Text;
            topicCombo.Items.Clear();
            foreach (var t in snap.Topics.OrderBy(x => x.Topic, StringComparer.OrdinalIgnoreCase))
                topicCombo.Items.Add(t.Topic);
            if (!string.IsNullOrEmpty(currentTopicText))
                topicCombo.Text = currentTopicText;

            RefreshDataKeyCombo();
            RefreshLearnedSourceList();
        }

        // Re-filters the Data key dropdown to keys observed for the currently chosen Topic.
        // Clears the typed text when it doesn't belong to any of the matching topics, so a
        // user can't keep a stale key from a previously selected topic.
        private void RefreshDataKeyCombo()
        {
            var snap = _lastSnapshot;
            var matchingKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            bool topicSelected = !string.IsNullOrEmpty(topicCombo.Text);

            if (snap != null)
            {
                foreach (var t in snap.Topics)
                {
                    if (TopicMatchesUi(t.Topic))
                        foreach (var dk in t.DataKeyExamples.Keys) matchingKeys.Add(dk);
                }
                // If no topic is chosen yet, fall back to the full pool so the user can browse.
                if (!topicSelected)
                {
                    foreach (var t in snap.Topics)
                        foreach (var dk in t.DataKeyExamples.Keys) matchingKeys.Add(dk);
                }
            }

            string currentKeyText = dataKeyCombo.Text ?? "";
            dataKeyCombo.Items.Clear();
            foreach (var k in matchingKeys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                dataKeyCombo.Items.Add(k);

            // Preserve the typed value only when it's actually one of the keys present for
            // the currently selected topic. Otherwise drop it so the user re-picks.
            if (!string.IsNullOrEmpty(currentKeyText) && matchingKeys.Contains(currentKeyText))
                dataKeyCombo.Text = currentKeyText;
            else if (topicSelected)
                dataKeyCombo.Text = "";
            else
                dataKeyCombo.Text = currentKeyText;
        }

        private void RefreshLearnedSourceList()
        {
            var snap = _lastSnapshot;
            learnedSourceList.Items.Clear();
            if (snap == null) return;

            int distinct = 0;
            foreach (var t in snap.Topics)
            {
                if (!TopicMatchesUi(t.Topic)) continue;
                foreach (var sv in t.SourceValues)
                {
                    foreach (var v in sv.Value)
                    {
                        learnedSourceList.Items.Add(new TextBlock
                        {
                            Text = $"{sv.Key} = {v}",
                            Foreground = new SolidColorBrush(Color.FromRgb(0xCF, 0xD7, 0xDA)),
                            Margin = new Thickness(0, 1, 0, 1),
                        });
                        distinct++;
                    }
                }
            }
            if (distinct >= 2 && !sourceFilterExpander.IsExpanded)
                sourceFilterExpander.IsExpanded = true;
        }

        private bool TopicMatchesUi(string topic)
        {
            var filter = topicCombo.Text ?? "";
            if (string.IsNullOrEmpty(filter)) return true;
            var mode = (topicMatchModeCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Contains";
            switch (mode)
            {
                case "Exact":     return string.Equals(topic, filter, StringComparison.OrdinalIgnoreCase);
                case "EndsWith":  return topic != null && topic.EndsWith(filter, StringComparison.OrdinalIgnoreCase);
                default:          return topic != null && topic.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
            }
        }

        // ───────── Preview render ─────────

        private ExtractorConfig BuildExtractorCfgFromUi()
        {
            var mode = (topicMatchModeCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Contains";
            return new ExtractorConfig
            {
                Topic = topicCombo.Text ?? "",
                TopicMatchMode = mode,
                SourceFilters = ExtractorConfig.ParseSourceFilters(sourceFilterBox.Text ?? ""),
                DataKey = dataKeyCombo.Text ?? "",
            };
        }

        private void EnsureAgeTicker()
        {
            if (_ageTicker != null) return;
            _ageTicker = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _ageTicker.Tick += (s, e) => UpdatePreviewAge();
            _ageTicker.Start();
        }

        private void ReExtractAndRender()
        {
            // Pull the most recent XML from the cache and extract with the current
            // form state so combo edits are reflected without waiting for a packet.
            if (_metadataItem != null
                && LastXmlCache.TryGet(_metadataItem.FQID.ObjectId, out var xml, out _))
            {
                var cfg = BuildExtractorCfgFromUi();
                if (!string.IsNullOrEmpty(cfg.DataKey))
                {
                    var hit = MetadataExtractor.TryExtract(xml, cfg);
                    if (hit != null)
                    {
                        _lastPreviewUtc = hit.TimestampUtc;
                        _lastPreviewValue = hit.Value;
                    }
                    else
                    {
                        _lastPreviewValue = null;
                    }
                }
            }
            ReRenderPreview();
        }

        private void ReRenderPreview()
        {
            if (!_uiReady) return;
            if (_lastPreviewValue == null)
            {
                _previewLamp?.Clear();
                _previewNumber?.Clear();
                _previewGauge?.Clear();
                _previewText?.Clear();
                previewMetaText.Text = "";
                UpdatePreviewAge();
                return;
            }

            previewMetaText.Text = $"key={dataKeyCombo.Text}  value={_lastPreviewValue}";

            string density = (densityCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Comfortable";

            if (_previewLamp != null)
            {
                var rows = LampMapParser.Parse(SerializeLampRows());
                _previewLamp.Density = density;
                _previewLamp.IconSize = TryParseDouble(lampIconSizeBox.Text) ?? 96;
                _previewLamp.Update(_lastPreviewValue, rows);
            }
            else if (_previewNumber != null)
            {
                _previewNumber.Density = density;
                _previewNumber.Update(_lastPreviewValue, BuildNumericConfigFromUi(false));
            }
            else if (_previewGauge != null)
            {
                _previewGauge.Density = density;
                _previewGauge.Update(_lastPreviewValue, BuildGaugeConfigFromUi());
            }
            else if (_previewText != null)
            {
                _previewText.FontSize = TryParseDouble(textFontSizeBox.Text) ?? 28;
                _previewText.Update(_lastPreviewValue);
            }

            UpdatePreviewAge();
        }

        private NumericConfig BuildNumericConfigFromUi(bool fromGaugePanel)
        {
            string min = fromGaugePanel ? gaugeNumMinBox.Text : numMinBox.Text;
            string max = fromGaugePanel ? gaugeNumMaxBox.Text : numMaxBox.Text;
            bool highBad = fromGaugePanel ? (gaugeHighIsBadCheck.IsChecked == true) : (highIsBadCheck.IsChecked == true);
            string ok = fromGaugePanel ? _gaugeColorOk.HexValue : _colorOk.HexValue;
            string warn = fromGaugePanel ? _gaugeColorWarn.HexValue : _colorWarn.HexValue;
            string bad = fromGaugePanel ? _gaugeColorBad.HexValue : _colorBad.HexValue;
            string unit = fromGaugePanel ? unitBoxGauge.Text : unitBoxNumber.Text;

            return new NumericConfig
            {
                Min = TryParseDouble(min),
                Max = TryParseDouble(max),
                HighIsBad = highBad,
                ColorOk = ColorUtil.Parse(ok, Color.FromRgb(0x3C, 0xB3, 0x71)),
                ColorWarn = ColorUtil.Parse(warn, Color.FromRgb(0xE6, 0x95, 0x00)),
                ColorBad = ColorUtil.Parse(bad, Color.FromRgb(0xD8, 0x39, 0x2C)),
                Unit = unit ?? "",
            };
        }

        private GaugeConfig BuildGaugeConfigFromUi()
        {
            var rmin = TryParseDouble(gaugeRangeMinBox.Text) ?? 0;
            var rmax = TryParseDouble(gaugeRangeMaxBox.Text) ?? 100;
            if (rmax <= rmin) rmax = rmin + 1;

            var styleText = (gaugeStyleCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Modern180";
            var style = GaugeStyle.Modern180;
            if (string.Equals(styleText, "Arc270", StringComparison.OrdinalIgnoreCase)) style = GaugeStyle.Arc270;
            else if (string.Equals(styleText, "Arc180", StringComparison.OrdinalIgnoreCase)) style = GaugeStyle.Arc180;
            else if (string.Equals(styleText, "Bar", StringComparison.OrdinalIgnoreCase)) style = GaugeStyle.Bar;
            else if (string.Equals(styleText, "Modern270", StringComparison.OrdinalIgnoreCase)) style = GaugeStyle.Modern270;

            int tc = 10;
            if (int.TryParse(gaugeTickCountBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ptc) && ptc >= 0)
                tc = ptc;

            return new GaugeConfig
            {
                RangeMin = rmin,
                RangeMax = rmax,
                Style = style,
                ShowValue = gaugeShowValueCheck.IsChecked == true,
                ValueFontSize = TryParseDouble(gaugeValueFontSizeBox.Text) ?? 34,
                ShowTicks = gaugeShowTicksCheck.IsChecked == true,
                TickCount = tc,
                TrackThickness = TryParseDouble(gaugeTrackThicknessBox.Text) ?? 14,
                Numeric = BuildNumericConfigFromUi(true),
            };
        }

        private static double? TryParseDouble(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? (double?)v : null;
        }

        private void UpdatePreviewAge()
        {
            if (_lastPreviewUtc.HasValue)
            {
                var age = DateTime.UtcNow - _lastPreviewUtc.Value;
                if (age < TimeSpan.Zero) age = TimeSpan.Zero;
                previewAgeText.Text = $"updated {FormatAge(age)} ago";
            }
            else
            {
                previewAgeText.Text = "";
            }
        }

        private static string FormatAge(TimeSpan span)
        {
            if (span.TotalSeconds < 1) return "just now";
            if (span.TotalSeconds < 90) return $"{(int)span.TotalSeconds}s";
            if (span.TotalMinutes < 90) return $"{(int)span.TotalMinutes}m";
            return $"{(int)span.TotalHours}h";
        }

        // ───────── Save / Cancel ─────────

        private void OnSave(object sender, RoutedEventArgs e)
        {
            if (!ValidateForSave(out var error))
            {
                statusText.Text = error;
                return;
            }
            WriteToManager();
            DialogResult = true;
            Close();
        }

        private void OnCancel(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private bool ValidateForSave(out string error)
        {
            if (string.IsNullOrEmpty(_metadataIdString))
            {
                error = "Pick a metadata channel.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(dataKeyCombo.Text))
            {
                error = "Set a Data key.";
                return false;
            }

            foreach (var box in EnumerateValidatedTextBoxes())
            {
                if (!IsTextBoxValid(box, out var err))
                {
                    error = err;
                    box.Focus();
                    return false;
                }
            }
            error = null;
            return true;
        }

        // ───────── Numeric input validation ─────────

        // Tag values: "number" (any double), "positiveNumber" (> 0), "nonNegativeInteger" (>= 0 integer).
        private static readonly Regex NumberAllowedChars = new Regex(@"^[\-0-9\.,]+$");
        private static readonly Regex IntegerAllowedChars = new Regex(@"^[0-9]+$");

        private void InstallNumericValidation()
        {
            foreach (var tb in EnumerateValidatedTextBoxes())
            {
                tb.PreviewTextInput += OnNumericPreviewTextInput;
                DataObject.AddPastingHandler(tb, OnNumericPaste);
                tb.TextChanged += (s, e) => UpdateValidationStyle((TextBox)s);
                UpdateValidationStyle(tb);
            }
        }

        private IEnumerable<TextBox> EnumerateValidatedTextBoxes()
        {
            yield return numMinBox;
            yield return numMaxBox;
            yield return gaugeRangeMinBox;
            yield return gaugeRangeMaxBox;
            yield return gaugeNumMinBox;
            yield return gaugeNumMaxBox;
            yield return titleFontSizeBox;
            yield return gaugeValueFontSizeBox;
            yield return gaugeTickCountBox;
            yield return gaugeTrackThicknessBox;
            yield return lampIconSizeBox;
            yield return textFontSizeBox;
            yield return staleSecondsBox;
        }

        private static string TagOf(TextBox tb) => tb?.Tag as string ?? "number";

        private void OnNumericPreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            var tb = (TextBox)sender;
            var tag = TagOf(tb);
            var ch = e.Text;
            if (tag == "nonNegativeInteger")
            {
                if (!IntegerAllowedChars.IsMatch(ch)) e.Handled = true;
            }
            else
            {
                if (!NumberAllowedChars.IsMatch(ch)) e.Handled = true;
            }
        }

        private void OnNumericPaste(object sender, DataObjectPastingEventArgs e)
        {
            if (!e.SourceDataObject.GetDataPresent(DataFormats.Text)) { e.CancelCommand(); return; }
            var s = e.SourceDataObject.GetData(DataFormats.Text) as string;
            var tag = TagOf((TextBox)sender);
            var allowed = tag == "nonNegativeInteger" ? IntegerAllowedChars : NumberAllowedChars;
            if (string.IsNullOrEmpty(s) || !allowed.IsMatch(s)) e.CancelCommand();
        }

        private void UpdateValidationStyle(TextBox tb)
        {
            tb.BorderBrush = IsTextBoxValid(tb, out _)
                ? new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44))
                : new SolidColorBrush(Color.FromRgb(0xD8, 0x39, 0x2C));
        }

        private static bool IsTextBoxValid(TextBox tb, out string error)
        {
            var tag = TagOf(tb);
            var s = tb.Text ?? "";
            if (string.IsNullOrWhiteSpace(s))
            {
                // Empty values are tolerated (treated as "use default") for thresholds/scale.
                // Only positiveNumber and nonNegativeInteger require a value.
                if (tag == "positiveNumber" || tag == "nonNegativeInteger")
                {
                    error = $"'{tb.Name}': value required.";
                    return false;
                }
                error = null;
                return true;
            }
            if (tag == "nonNegativeInteger")
            {
                if (!int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var iv) || iv < 0)
                {
                    error = $"'{tb.Name}': must be a non-negative integer.";
                    return false;
                }
            }
            else
            {
                if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var dv))
                {
                    error = $"'{tb.Name}': must be a number.";
                    return false;
                }
                if (tag == "positiveNumber" && dv <= 0)
                {
                    error = $"'{tb.Name}': must be greater than 0.";
                    return false;
                }
            }
            error = null;
            return true;
        }

        // ───────── Packet inspector ─────────

        private void OnInspectPacket(object sender, RoutedEventArgs e)
        {
            string xml = null;
            if (_metadataItem != null && LastXmlCache.TryGet(_metadataItem.FQID.ObjectId, out var cached, out _))
                xml = cached;

            if (string.IsNullOrEmpty(xml))
            {
                MessageBox.Show(this,
                    "No packet captured yet. Pick a metadata channel and wait for the first packet.",
                    "Inspect packet", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string pretty;
            try
            {
                var filtered = MetadataExtractor.FilterHiddenTopics(xml);
                pretty = XDocument.Parse(filtered).ToString(SaveOptions.None);
            }
            catch { pretty = xml; }

            var win = new Window
            {
                Title = "Latest packet (XML)",
                Width = 900,
                Height = 700,
                Owner = this,
                Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E)),
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
            };
            var rtb = new RichTextBox
            {
                Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A)),
                Foreground = new SolidColorBrush(Color.FromRgb(0xE6, 0xEA, 0xEC)),
                BorderThickness = new Thickness(0),
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12,
                IsReadOnly = true,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            };
            var para = new Paragraph { Margin = new Thickness(0) };
            HighlightXmlInto(para, pretty);
            rtb.Document = new System.Windows.Documents.FlowDocument(para)
            {
                PageWidth = 4000,
            };
            win.Content = rtb;
            win.ShowDialog();
        }

        private static string DumpXmlContents(string xml)
        {
            try
            {
                var msgs = MetadataExtractor.Observe(xml).ToList();
                if (msgs.Count == 0) return "(no NotificationMessages)";
                var sb = new System.Text.StringBuilder();
                int i = 0;
                foreach (var m in msgs)
                {
                    if (i++ >= 4) { sb.Append("..."); break; }
                    sb.Append("Topic=").Append(m.Topic);
                    if (m.Data != null && m.Data.Count > 0)
                    {
                        sb.Append(" Data[");
                        bool first = true;
                        foreach (var kv in m.Data)
                        {
                            if (!first) sb.Append(',');
                            sb.Append(kv.Key).Append('=').Append(kv.Value);
                            first = false;
                        }
                        sb.Append(']');
                    }
                    sb.Append("; ");
                }
                return sb.ToString();
            }
            catch (Exception ex)
            {
                return "(dump failed: " + ex.Message + ")";
            }
        }

        // Coloring rules (mirrors Admin/MetadataViewer style):
        //   <,>,/  : dim gray
        //   element name      : light blue
        //   attribute name    : salmon
        //   attribute value   : light orange
        //   text content      : default foreground
        private static readonly SolidColorBrush BrushBracket = new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80));
        private static readonly SolidColorBrush BrushElement = new SolidColorBrush(Color.FromRgb(0x6B, 0xB6, 0xFF));
        private static readonly SolidColorBrush BrushAttr = new SolidColorBrush(Color.FromRgb(0xE6, 0x95, 0x80));
        private static readonly SolidColorBrush BrushAttrValue = new SolidColorBrush(Color.FromRgb(0xE6, 0xB8, 0x6B));
        private static readonly SolidColorBrush BrushText = new SolidColorBrush(Color.FromRgb(0xE6, 0xEA, 0xEC));

        private static readonly Regex XmlTokenRegex = new Regex(
            @"(?<tag><(?<close>/?)(?<elname>[\w:.\-]+)(?<attrs>(\s+[\w:.\-]+\s*=\s*""[^""]*"")*)\s*(?<self>/?)>)|(?<text>[^<]+)",
            RegexOptions.Compiled | RegexOptions.Singleline);

        private static readonly Regex AttrRegex = new Regex(
            @"(?<name>[\w:.\-]+)\s*=\s*""(?<value>[^""]*)""",
            RegexOptions.Compiled);

        private static void HighlightXmlInto(Paragraph para, string xml)
        {
            int i = 0;
            foreach (Match m in XmlTokenRegex.Matches(xml))
            {
                if (m.Index > i)
                    para.Inlines.Add(new Run(xml.Substring(i, m.Index - i)) { Foreground = BrushText });

                if (m.Groups["text"].Success)
                {
                    para.Inlines.Add(new Run(m.Value) { Foreground = BrushText });
                }
                else
                {
                    para.Inlines.Add(new Run("<" + m.Groups["close"].Value) { Foreground = BrushBracket });
                    para.Inlines.Add(new Run(m.Groups["elname"].Value) { Foreground = BrushElement });
                    var attrs = m.Groups["attrs"].Value;
                    if (!string.IsNullOrEmpty(attrs))
                    {
                        int last = 0;
                        foreach (Match am in AttrRegex.Matches(attrs))
                        {
                            if (am.Index > last)
                                para.Inlines.Add(new Run(attrs.Substring(last, am.Index - last)) { Foreground = BrushBracket });
                            para.Inlines.Add(new Run(am.Groups["name"].Value) { Foreground = BrushAttr });
                            para.Inlines.Add(new Run("=\"") { Foreground = BrushBracket });
                            para.Inlines.Add(new Run(am.Groups["value"].Value) { Foreground = BrushAttrValue });
                            para.Inlines.Add(new Run("\"") { Foreground = BrushBracket });
                            last = am.Index + am.Length;
                        }
                        if (last < attrs.Length)
                            para.Inlines.Add(new Run(attrs.Substring(last)) { Foreground = BrushBracket });
                    }
                    var selfClose = m.Groups["self"].Value;
                    para.Inlines.Add(new Run(selfClose + ">") { Foreground = BrushBracket });
                }

                i = m.Index + m.Length;
            }
            if (i < xml.Length)
                para.Inlines.Add(new Run(xml.Substring(i)) { Foreground = BrushText });
        }

        private void WriteToManager()
        {
            _vim.Title = titleBox.Text?.Trim() ?? "";
            _vim.ShowTitle = (showTitleCheck.IsChecked == true) ? "true" : "false";
            _vim.TitlePosition = (titlePositionCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Left";
            _vim.TitleFontSize = NormalizeNumberText(titleFontSizeBox.Text, "14");
            _vim.TitleColor = NormalizeColor(_titleColor.HexValue);

            _vim.MetadataId = _metadataIdString ?? "";
            _vim.MetadataName = _metadataNameString ?? "";

            _vim.Topic = topicCombo.Text?.Trim() ?? "";
            _vim.TopicMatchMode = (topicMatchModeCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Contains";
            _vim.SourceFilters = sourceFilterBox.Text?.Trim() ?? "";
            _vim.DataKey = dataKeyCombo.Text?.Trim() ?? "";

            string rt = "Lamp";
            if (rtNumber.IsChecked == true) rt = "Number";
            else if (rtGauge.IsChecked == true) rt = "Gauge";
            else if (rtText.IsChecked == true) rt = "Text";
            _vim.RenderType = rt;

            _vim.LampMap = SerializeLampRows();
            _vim.LampIconSize = NormalizeNumberText(lampIconSizeBox.Text, "96");
            _vim.TextFontSize = NormalizeNumberText(textFontSizeBox.Text, "28");

            if (rt == "Gauge")
            {
                _vim.NumMin = gaugeNumMinBox.Text?.Trim() ?? "";
                _vim.NumMax = gaugeNumMaxBox.Text?.Trim() ?? "";
                _vim.NumDirection = (gaugeHighIsBadCheck.IsChecked == true) ? "HighIsBad" : "LowIsBad";
                _vim.ColorOk = NormalizeColor(_gaugeColorOk.HexValue);
                _vim.ColorWarn = NormalizeColor(_gaugeColorWarn.HexValue);
                _vim.ColorBad = NormalizeColor(_gaugeColorBad.HexValue);
                _vim.Unit = unitBoxGauge.Text?.Trim() ?? "";
            }
            else
            {
                _vim.NumMin = numMinBox.Text?.Trim() ?? "";
                _vim.NumMax = numMaxBox.Text?.Trim() ?? "";
                _vim.NumDirection = (highIsBadCheck.IsChecked == true) ? "HighIsBad" : "LowIsBad";
                _vim.ColorOk = NormalizeColor(_colorOk.HexValue);
                _vim.ColorWarn = NormalizeColor(_colorWarn.HexValue);
                _vim.ColorBad = NormalizeColor(_colorBad.HexValue);
                _vim.Unit = unitBoxNumber.Text?.Trim() ?? "";
            }

            _vim.GaugeRangeMin = NormalizeNumberText(gaugeRangeMinBox.Text, "0");
            _vim.GaugeRangeMax = NormalizeNumberText(gaugeRangeMaxBox.Text, "100");
            _vim.GaugeStyle = (gaugeStyleCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Modern180";
            _vim.GaugeShowValue = (gaugeShowValueCheck.IsChecked == true) ? "true" : "false";
            _vim.GaugeValueFontSize = NormalizeNumberText(gaugeValueFontSizeBox.Text, "34");
            _vim.GaugeShowTicks = (gaugeShowTicksCheck.IsChecked == true) ? "true" : "false";
            _vim.GaugeTickCount = NormalizeNumberText(gaugeTickCountBox.Text, "10");
            _vim.GaugeTrackThickness = NormalizeNumberText(gaugeTrackThicknessBox.Text, "14");

            _vim.StaleSeconds = NormalizeNumberText(staleSecondsBox.Text, "0");

            _vim.WidgetDensity = (densityCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Comfortable";
        }

        private static string NormalizeColor(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "#777777";
            var t = s.Trim();
            return t.StartsWith("#") ? t : "#" + t;
        }

        private static string NormalizeNumberText(string s, string fallback)
        {
            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                return v.ToString(CultureInfo.InvariantCulture);
            return fallback;
        }

        // ───────── Helpers ─────────

        private static void SelectComboItem(ComboBox combo, string text)
        {
            for (int i = 0; i < combo.Items.Count; i++)
            {
                var item = combo.Items[i] as ComboBoxItem;
                if (item == null) continue;
                if (string.Equals(item.Tag?.ToString(), text, StringComparison.Ordinal)
                    || string.Equals(item.Content?.ToString(), text, StringComparison.Ordinal))
                {
                    combo.SelectedIndex = i;
                    return;
                }
            }
            if (combo.Items.Count > 0) combo.SelectedIndex = 0;
        }

        private void Teardown()
        {
            _learn.Stop();
            StopSource();
            _ageTicker?.Stop();
            _ageTicker = null;
            _diagTicker?.Stop();
            _diagTicker = null;
        }
    }
}
