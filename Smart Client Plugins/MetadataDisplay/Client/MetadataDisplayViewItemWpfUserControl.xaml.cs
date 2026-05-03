using System;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CommunitySDK;
using MetadataDisplay.Client.Renderers;
using VideoOS.Platform;
using VideoOS.Platform.Client;
using VideoOS.Platform.Live;
using VideoOS.Platform.Messaging;

namespace MetadataDisplay.Client
{
    public partial class MetadataDisplayViewItemWpfUserControl : ViewItemWpfUserControl
    {
        private static readonly PluginLog _log = new PluginLog("MetadataDisplay");

        private readonly MetadataDisplayViewItemManager _viewItemManager;
        private object _modeChangedReceiver;

        private MetadataLiveSource _liveSource;
        private Item _metadataItem;
        private ExtractorConfig _extractorCfg;

        private MetadataPlaybackPump _pump;
        private object _playbackTimeReceiver;

        private LampRenderer _lampRenderer;
        private NumberRenderer _numberRenderer;
        private GaugeRenderer _gaugeRenderer;
        private TextRenderer _textRenderer;

        private DateTime? _lastValueUtc;
        private DispatcherTimer _staleTicker;
        private int _staleSeconds;

        public MetadataDisplayViewItemWpfUserControl(MetadataDisplayViewItemManager viewItemManager)
        {
            _viewItemManager = viewItemManager;
            InitializeComponent();
        }

        public override void Init()
        {
            _log.Info($"[ViewItem] Init id={_viewItemManager?.MetadataId} render={_viewItemManager?.RenderType} mode={EnvironmentManager.Instance.Mode}");
            _modeChangedReceiver = EnvironmentManager.Instance.RegisterReceiver(
                new MessageReceiver(OnModeChanged),
                new MessageIdFilter(MessageId.System.ModeChangedIndication));
            ApplyMode(EnvironmentManager.Instance.Mode);
        }

        public override void Close()
        {
            _log.Info("[ViewItem] Close");
            if (_modeChangedReceiver != null)
            {
                EnvironmentManager.Instance.UnRegisterReceiver(_modeChangedReceiver);
                _modeChangedReceiver = null;
            }
            StopLive();
            StopPlayback();
            StopStaleTicker();
        }

        // Called by the configuration window after a Save so the rendered widget
        // picks up the new config without needing a mode toggle.
        internal void Reconfigure()
        {
            ApplyMode(EnvironmentManager.Instance.Mode);
        }

        private void ApplyMode(Mode mode)
        {
            _log.Info($"[ViewItem] ApplyMode mode={mode}");
            ApplyTitleSettings();
            staleBadge.Visibility = Visibility.Collapsed;
            _lastValueUtc = null;

            _extractorCfg = ExtractorConfig.FromManager(_viewItemManager);
            BuildRenderHost();
            ResolveMetadataItem();

            StopLive();
            StopPlayback();
            StopStaleTicker();

            string overlay = ResolveOverlayMessage(mode);
            if (overlay != null)
            {
                setupHint.Text = overlay;
                setupHint.Visibility = Visibility.Visible;
                renderHost.Visibility = Visibility.Collapsed;
                return;
            }

            setupHint.Visibility = Visibility.Collapsed;
            renderHost.Visibility = Visibility.Visible;

            if (mode == Mode.ClientLive)
                StartLive();
            else if (mode == Mode.ClientPlayback)
                StartPlayback();

            StartStaleTickerIfEnabled();
        }

        // null = no overlay (render the widget). Non-null = show this message instead.
        private string ResolveOverlayMessage(Mode mode)
        {
            if (mode == Mode.ClientSetup)
                return "Configure in properties";

            bool hasChannelId = !string.IsNullOrEmpty(_viewItemManager.MetadataId)
                                && Guid.TryParse(_viewItemManager.MetadataId, out var g)
                                && g != Guid.Empty;
            if (!hasChannelId)
                return "Configure in properties";

            if (_metadataItem == null)
            {
                var name = _viewItemManager.MetadataName;
                return string.IsNullOrEmpty(name)
                    ? "Metadata channel not found"
                    : $"Metadata channel \"{name}\" not found";
            }

            if (string.IsNullOrEmpty(_extractorCfg?.DataKey))
                return "No data key configured";

            return null;
        }

        private void ApplyTitleSettings()
        {
            var title = _viewItemManager.Title ?? "";
            bool show = !string.Equals(_viewItemManager.ShowTitle, "false", StringComparison.OrdinalIgnoreCase)
                        && !string.IsNullOrEmpty(title);

            if (!show)
            {
                titleText.Visibility = Visibility.Collapsed;
                titleRow.Height = new GridLength(0);
                return;
            }

            titleText.Text = title;
            titleText.Visibility = Visibility.Visible;
            titleRow.Height = GridLength.Auto;

            // Position
            switch (_viewItemManager.TitlePosition ?? "Left")
            {
                case "Center": titleText.HorizontalAlignment = HorizontalAlignment.Center; titleText.TextAlignment = TextAlignment.Center; break;
                case "Right":  titleText.HorizontalAlignment = HorizontalAlignment.Right;  titleText.TextAlignment = TextAlignment.Right;  break;
                default:       titleText.HorizontalAlignment = HorizontalAlignment.Left;   titleText.TextAlignment = TextAlignment.Left;   break;
            }

            // Font size
            if (double.TryParse(_viewItemManager.TitleFontSize, NumberStyles.Float, CultureInfo.InvariantCulture, out var fs) && fs > 0)
                titleText.FontSize = fs;
            else
                titleText.FontSize = 14;

            // Color
            try
            {
                var c = (Color)ColorConverter.ConvertFromString(_viewItemManager.TitleColor);
                titleText.Foreground = new SolidColorBrush(c);
            }
            catch
            {
                titleText.Foreground = new SolidColorBrush(Color.FromRgb(0xCF, 0xD7, 0xDA));
            }
        }

        private void StartStaleTickerIfEnabled()
        {
            _staleSeconds = 0;
            int.TryParse(_viewItemManager.StaleSeconds ?? "0", NumberStyles.Integer, CultureInfo.InvariantCulture, out _staleSeconds);
            if (_staleSeconds <= 0) return;

            _staleTicker = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _staleTicker.Tick += (s, e) => UpdateStaleVisuals();
            _staleTicker.Start();
        }

        private void StopStaleTicker()
        {
            if (_staleTicker == null) return;
            _staleTicker.Stop();
            _staleTicker = null;
        }

        private void UpdateStaleVisuals()
        {
            if (_staleSeconds <= 0 || !_lastValueUtc.HasValue)
            {
                staleBadge.Visibility = Visibility.Collapsed;
                renderHost.Opacity = 1.0;
                return;
            }
            var age = DateTime.UtcNow - _lastValueUtc.Value;
            if (age.TotalSeconds >= _staleSeconds)
            {
                staleBadge.Visibility = Visibility.Visible;
                renderHost.Opacity = 0.45;
            }
            else
            {
                staleBadge.Visibility = Visibility.Collapsed;
                renderHost.Opacity = 1.0;
            }
        }

        private void StartPlayback()
        {
            if (_metadataItem == null) { _log.Info("[ViewItem] StartPlayback skipped (no channel item)"); return; }
            if (_pump != null) return;
            if (string.IsNullOrEmpty(_extractorCfg?.DataKey)) { _log.Info("[ViewItem] StartPlayback skipped (no DataKey)"); return; }
            _log.Info($"[ViewItem] StartPlayback channel='{_metadataItem.Name}'");

            _pump = new MetadataPlaybackPump(_metadataItem,
                _ => ExtractorConfig.FromManager(_viewItemManager),
                (value, ts) => Dispatcher.BeginInvoke(new Action(() =>
                {
                    _lastValueUtc = ts;
                    RenderValue(value);
                    UpdateStaleVisuals();
                })));
            _pump.Start();

            _playbackTimeReceiver = EnvironmentManager.Instance.RegisterReceiver(
                new MessageReceiver(OnPlaybackTime),
                new MessageIdFilter(MessageId.SmartClient.PlaybackCurrentTimeIndication));
        }

        private void StopPlayback()
        {
            if (_playbackTimeReceiver == null && _pump == null) return;
            _log.Info("[ViewItem] StopPlayback");
            if (_playbackTimeReceiver != null)
            {
                EnvironmentManager.Instance.UnRegisterReceiver(_playbackTimeReceiver);
                _playbackTimeReceiver = null;
            }
            if (_pump != null)
            {
                _pump.Stop();
                _pump = null;
            }
        }

        private object OnPlaybackTime(Message message, FQID destination, FQID sender)
        {
            try
            {
                if (_pump == null) return null;

                DateTime? utc = null;
                if (message.Data is DateTime dt)
                {
                    utc = dt.Kind == DateTimeKind.Utc ? dt : dt.ToUniversalTime();
                }
                else if (message.Data != null)
                {
                    var prop = message.Data.GetType().GetProperty("DateTime");
                    if (prop != null && prop.GetValue(message.Data) is DateTime dt2)
                        utc = dt2.Kind == DateTimeKind.Utc ? dt2 : dt2.ToUniversalTime();
                }

                if (utc.HasValue) _pump.RequestTime(utc.Value);
            }
            catch (Exception ex)
            {
                _log.Error($"OnPlaybackTime threw: {ex.Message}");
            }
            return null;
        }

        private void BuildRenderHost()
        {
            renderHost.Children.Clear();
            _lampRenderer = null;
            _numberRenderer = null;
            _gaugeRenderer = null;
            _textRenderer = null;

            UIElement visual;
            switch (_viewItemManager.RenderType)
            {
                case "Number":
                    _numberRenderer = new NumberRenderer();
                    visual = _numberRenderer.Visual;
                    _numberRenderer.Clear();
                    break;
                case "Gauge":
                    _gaugeRenderer = new GaugeRenderer();
                    visual = _gaugeRenderer.Visual;
                    _gaugeRenderer.Clear();
                    break;
                case "Text":
                    _textRenderer = new TextRenderer();
                    visual = _textRenderer.Visual;
                    _textRenderer.Clear();
                    break;
                case "Lamp":
                default:
                    _lampRenderer = new LampRenderer();
                    visual = _lampRenderer.Visual;
                    _lampRenderer.Clear();
                    break;
            }

            if (visual is FrameworkElement fe)
            {
                fe.HorizontalAlignment = HorizontalAlignment.Stretch;
                fe.VerticalAlignment = VerticalAlignment.Stretch;
            }
            renderHost.Children.Add(visual);
        }

        private void ResolveMetadataItem()
        {
            _metadataItem = null;
            if (Guid.TryParse(_viewItemManager.MetadataId, out var id) && id != Guid.Empty)
            {
                try
                {
                    _metadataItem = Configuration.Instance.GetItem(id, Kind.Metadata);
                }
                catch (Exception ex)
                {
                    _log.Error($"Resolve metadata item {id} failed: {ex.Message}");
                }
            }
        }

        private int _livePacketsSeen;

        private void StartLive()
        {
            if (_metadataItem == null)
            {
                _log.Info("[ViewItem] StartLive skipped (no channel item)");
                return;
            }
            if (_liveSource != null) return;
            if (string.IsNullOrEmpty(_extractorCfg?.DataKey))
            {
                _log.Info("[ViewItem] StartLive skipped (no DataKey)");
                return;
            }

            try
            {
                _livePacketsSeen = 0;
                _log.Info($"[ViewItem] StartLive channel='{_metadataItem.Name}' id={_metadataItem.FQID.ObjectId} key={_extractorCfg.DataKey}");
                _liveSource = new MetadataLiveSource(_metadataItem) { LiveModeStart = true };
                _liveSource.Init();
                _liveSource.LiveContentEvent += OnLiveContent;
                _liveSource.ErrorEvent += OnLiveError;
                _log.Info("[ViewItem] StartLive subscribed OK");
            }
            catch (Exception ex)
            {
                _log.Error($"[ViewItem] StartLive failed: {ex.Message}", ex);
                _liveSource = null;
            }
        }

        private void StopLive()
        {
            if (_liveSource == null) return;
            _log.Info($"[ViewItem] StopLive packets={_livePacketsSeen}");
            try
            {
                _liveSource.LiveContentEvent -= OnLiveContent;
                _liveSource.ErrorEvent -= OnLiveError;
                _liveSource.Close();
            }
            catch { }
            _liveSource = null;
        }

        private void OnLiveContent(MetadataLiveSource source, MetadataLiveContent content)
        {
            try
            {
                _livePacketsSeen++;
                string xml = content?.Content?.GetMetadataString();
                if (string.IsNullOrEmpty(xml))
                {
                    _log.Info($"[ViewItem] Live packet #{_livePacketsSeen} empty");
                    return;
                }

                if (_metadataItem != null)
                    LastXmlCache.Put(_metadataItem.FQID.ObjectId, xml);

                var hit = MetadataExtractor.TryExtract(xml, _extractorCfg);
                if (_livePacketsSeen <= 3 || _livePacketsSeen % 50 == 0)
                    _log.Info($"[ViewItem] Live packet #{_livePacketsSeen} bytes={xml.Length} extracted={(hit?.Value ?? "(no match)")}");
                if (hit == null) return;

                Dispatcher.BeginInvoke(new Action(() =>
                {
                    _lastValueUtc = hit.TimestampUtc;
                    RenderValue(hit.Value);
                    UpdateStaleVisuals();
                }));
            }
            catch (Exception ex)
            {
                _log.Error($"OnLiveContent threw: {ex.Message}", ex);
            }
        }

        private void OnLiveError(MetadataLiveSource source, Exception ex)
        {
            _log.Error($"[ViewItem] LiveSource error: {ex?.GetType().Name}: {ex?.Message}");
        }

        private void RenderValue(string value)
        {
            if (_lampRenderer != null)
                _lampRenderer.Update(value, LampMapParser.Parse(_viewItemManager.LampMap));
            else if (_numberRenderer != null)
                _numberRenderer.Update(value, NumericConfig.FromManager(_viewItemManager));
            else if (_gaugeRenderer != null)
                _gaugeRenderer.Update(value, GaugeConfig.FromManager(_viewItemManager));
            else if (_textRenderer != null)
                _textRenderer.Update(value);
        }

        private object OnModeChanged(Message message, FQID destination, FQID sender)
        {
            Dispatcher.BeginInvoke(new Action(() => ApplyMode((Mode)message.Data)));
            return null;
        }

        private void OnMouseLeftUp(object sender, MouseButtonEventArgs e) => FireClickEvent();
        private void OnMouseDoubleClick(object sender, MouseButtonEventArgs e) => FireDoubleClickEvent();

        public override bool Maximizable => true;
        public override bool Selectable => true;
        public override bool ShowToolbar => false;
    }
}
