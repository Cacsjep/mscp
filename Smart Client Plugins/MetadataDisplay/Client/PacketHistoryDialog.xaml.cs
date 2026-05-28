using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.Xml.Linq;
using CommunitySDK;
using VideoOS.Platform;
using VideoOS.Platform.Data;

namespace MetadataDisplay.Client
{
    // Browser for recorded metadata packets. Opens a MetadataPlaybackSource on
    // the supplied channel, walks a [now-lookback, now] window in chunks of
    // 200, and lists one row per NotificationMessage so the operator can
    // filter by topic or data and pick a packet without waiting for live.
    //
    // The dialog is self-contained: it does not touch the configuration
    // window's live source. On "Use selected", the caller gets a
    // LearnSnapshot + raw XML mirroring PacketImportDialog's contract so the
    // same apply path can be reused.
    public partial class PacketHistoryDialog : Window
    {
        private static readonly PluginLog _log = new PluginLog("MetadataDisplay");

        // Hard cap on row count so a noisy channel can't OOM the dialog. Hit -> show
        // a hint that a shorter lookback or a more specific search would help.
        private const int MaxRows = 10000;

        private readonly Item _channel;
        private CancellationTokenSource _loadCts;
        private readonly ObservableCollection<PacketRow> _allRows = new ObservableCollection<PacketRow>();
        private readonly ObservableCollection<PacketRow> _visibleRows = new ObservableCollection<PacketRow>();
        private string _currentFilter = string.Empty;
        private DispatcherTimer _filterDebounce;
        private bool _isClosed;

        // After OK: snapshot built from the picked packet (mirrors PacketImportDialog).
        internal LearnSnapshot Snapshot { get; private set; }
        public string ImportedXml { get; private set; }
        // Topic of the row the operator picked - used by the caller to auto-select
        // when topicCombo is currently empty.
        public string PreferredTopic { get; private set; }

        public PacketHistoryDialog(Item channel)
        {
            _channel = channel ?? throw new ArgumentNullException(nameof(channel));
            InitializeComponent();
            packetGrid.ItemsSource = _visibleRows;
            headerText.Text =
                $"Pick a recorded packet from '{channel.Name}' to populate Topic and Field.";
            Loaded += (s, e) => ReloadAsync();
            Closed += OnDialogClosed;
            searchBox.TextChanged += OnSearchChanged;
        }

        private void OnDialogClosed(object sender, EventArgs e)
        {
            _isClosed = true;
            CancelLoad();
            if (_filterDebounce != null)
            {
                _filterDebounce.Stop();
                _filterDebounce = null;
            }
        }

        // ───────── Load / scan ─────────

        private void OnReload(object sender, RoutedEventArgs e) => ReloadAsync();

        private void ReloadAsync()
        {
            CancelLoad();
            var hours = SelectedLookbackHours();
            var nowUtc = DateTime.UtcNow;
            var fromUtc = nowUtc.AddHours(-hours);
            _allRows.Clear();
            _visibleRows.Clear();
            useButton.IsEnabled = false;
            previewRtb.Document = new FlowDocument(new Paragraph { Margin = new Thickness(0) }) { PageWidth = 4000 };
            statusText.Foreground = HintBrush;
            statusText.Text = $"Loading last {hours}h of recorded packets...";
            reloadButton.IsEnabled = false;

            _loadCts = new CancellationTokenSource();
            var ct = _loadCts.Token;

            Task.Run(() => ScanRange(fromUtc, nowUtc, ct), ct)
                .ContinueWith(t => OnScanComplete(t, hours, ct), TaskScheduler.FromCurrentSynchronizationContext());
        }

        private void CancelLoad()
        {
            var cts = _loadCts;
            _loadCts = null;
            if (cts == null) return;
            try { cts.Cancel(); } catch { }
            try { cts.Dispose(); } catch { }
        }

        private int SelectedLookbackHours()
        {
            var item = lookbackCombo.SelectedItem as ComboBoxItem;
            if (item?.Tag != null && int.TryParse(item.Tag.ToString(), out var h) && h > 0) return h;
            return 24;
        }

        // Returns the collected rows and a flag indicating whether the MaxRows cap
        // was hit. Init/constructor failures are logged and rethrown so the caller
        // can surface a real error instead of an empty result. In-loop Get errors
        // are tolerated (logged + break) since they may be transient.
        private ScanResult ScanRange(DateTime fromUtc, DateTime toUtc, CancellationToken ct)
        {
            var rows = new List<PacketRow>();
            bool capHit = false;
            MetadataPlaybackSource src;
            try
            {
                src = new MetadataPlaybackSource(_channel);
                src.Init();
            }
            catch (Exception ex)
            {
                _log.Error($"[History] PlaybackSource Init failed: {ex.Message}");
                throw;
            }

            try
            {
                var cursor = fromUtc;
                const int chunk = 200;
                int safety = 0;
                while (!ct.IsCancellationRequested && cursor <= toUtc && safety++ < 2000)
                {
                    List<MetadataPlaybackData> frames;
                    try { frames = src.Get(cursor, toUtc - cursor, chunk); }
                    catch (Exception ex)
                    {
                        _log.Error($"[History] Get threw: {ex.Message}");
                        break;
                    }
                    if (frames == null || frames.Count == 0) break;

                    DateTime? lastFrameUtc = null;
                    foreach (var f in frames)
                    {
                        if (ct.IsCancellationRequested) break;
                        if (f == null) continue;
                        string xml;
                        try { xml = f.Content?.GetMetadataString(); }
                        catch { continue; }
                        if (string.IsNullOrEmpty(xml)) continue;
                        lastFrameUtc = f.DateTime;

                        // Filter hidden topics so the operator never sees them in the list.
                        // Same treatment Inspect packet gives the live preview.
                        string filtered = MetadataExtractor.FilterHiddenTopics(xml);

                        var observed = MetadataExtractor.Observe(filtered).ToList();
                        if (observed.Count == 0)
                        {
                            // No NotificationMessages - still record a placeholder row so the
                            // operator can see something arrived at this timestamp.
                            rows.Add(new PacketRow
                            {
                                CaptureUtc = f.DateTime,
                                Topic = "(no NotificationMessage)",
                                Info = string.Empty,
                                Xml = filtered,
                            });
                        }
                        else
                        {
                            foreach (var msg in observed)
                            {
                                rows.Add(new PacketRow
                                {
                                    CaptureUtc = f.DateTime,
                                    Topic = msg.Topic ?? string.Empty,
                                    Info = BuildInfo(msg),
                                    Xml = filtered,
                                });
                            }
                        }

                        if (rows.Count >= MaxRows) { capHit = true; break; }
                    }

                    if (capHit) break;
                    if (!lastFrameUtc.HasValue) break;
                    var nextStart = lastFrameUtc.Value.AddTicks(1);
                    if (nextStart <= cursor) break;
                    cursor = nextStart;
                }
            }
            finally
            {
                try { src.Close(); } catch { }
            }
            return new ScanResult { Rows = rows, CapHit = capHit };
        }

        private sealed class ScanResult
        {
            public List<PacketRow> Rows;
            public bool CapHit;
        }

        private void OnScanComplete(Task<ScanResult> task, int hours, CancellationToken ct)
        {
            // Window closed or this load was superseded by a newer Reload click.
            // Bail out so we don't mutate UI on a dead dialog or stomp the newer load.
            if (_isClosed || ct.IsCancellationRequested) return;

            reloadButton.IsEnabled = true;
            if (task.IsFaulted)
            {
                statusText.Text = "Load failed: " + (task.Exception?.GetBaseException().Message ?? "(unknown)");
                statusText.Foreground = ErrorBrush;
                return;
            }

            var result = task.Result;
            var rows = result?.Rows ?? new List<PacketRow>();
            // Newest first feels more useful when scanning recent activity.
            rows.Sort((a, b) => b.CaptureUtc.CompareTo(a.CaptureUtc));
            foreach (var r in rows) _allRows.Add(r);
            RefilterRows();

            if (_allRows.Count == 0)
            {
                statusText.Text = $"No packets recorded in the last {hours}h for this channel.";
                statusText.Foreground = WarnBrush;
            }
            else if (result != null && result.CapHit)
            {
                statusText.Text =
                    $"Loaded {_allRows.Count} packet row(s) - row cap reached. " +
                    "Use a shorter lookback or narrow the search to see older packets.";
                statusText.Foreground = WarnBrush;
            }
            else
            {
                statusText.Text = $"Loaded {_allRows.Count} packet row(s) from the last {hours}h.";
                statusText.Foreground = OkBrush;
            }
        }

        // ───────── Filter ─────────

        private void OnSearchChanged(object sender, TextChangedEventArgs e)
        {
            // Debounce the filter so typing doesn't refilter thousands of rows per keystroke.
            if (_filterDebounce == null)
            {
                _filterDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
                _filterDebounce.Tick += (s, _) =>
                {
                    // Use sender so a queued Tick after OnDialogClosed nulled the
                    // field doesn't NRE on _filterDebounce.Stop().
                    (s as DispatcherTimer)?.Stop();
                    if (_isClosed) return;
                    _currentFilter = (searchBox.Text ?? string.Empty).Trim();
                    RefilterRows();
                };
            }
            _filterDebounce.Stop();
            _filterDebounce.Start();
        }

        private void RefilterRows()
        {
            _visibleRows.Clear();
            if (string.IsNullOrEmpty(_currentFilter))
            {
                foreach (var r in _allRows) _visibleRows.Add(r);
            }
            else
            {
                foreach (var r in _allRows)
                {
                    if (PassesFilter(r)) _visibleRows.Add(r);
                }
            }
        }

        private bool PassesFilter(PacketRow r)
        {
            var n = _currentFilter;
            return IndexOfIgnoreCase(r.Topic, n) ||
                   IndexOfIgnoreCase(r.Info, n) ||
                   IndexOfIgnoreCase(r.CaptureTimeText, n);
        }

        private static bool IndexOfIgnoreCase(string haystack, string needle)
            => !string.IsNullOrEmpty(haystack) && haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;

        // ───────── Selection / preview ─────────

        private void OnPacketSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var row = packetGrid.SelectedItem as PacketRow;
            useButton.IsEnabled = row != null;
            if (row == null)
            {
                previewRtb.Document = new FlowDocument(new Paragraph { Margin = new Thickness(0) }) { PageWidth = 4000 };
                return;
            }

            string pretty;
            try { pretty = XDocument.Parse(row.Xml).ToString(SaveOptions.None); }
            catch { pretty = row.Xml; }

            var para = new Paragraph { Margin = new Thickness(0) };
            XmlHighlighter.HighlightInto(para, pretty);
            previewRtb.Document = new FlowDocument(para) { PageWidth = 4000 };
        }

        private void OnPacketDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (packetGrid.SelectedItem is PacketRow) OnUse(sender, null);
        }

        // ───────── Apply ─────────

        private void OnUse(object sender, RoutedEventArgs e)
        {
            var row = packetGrid.SelectedItem as PacketRow;
            if (row == null) return;

            try
            {
                var snap = LearnSnapshot.FromXml(row.Xml);
                if (snap?.Topics == null || snap.Topics.Count == 0)
                {
                    statusText.Text = "Selected packet contains no NotificationMessage topics.";
                    statusText.Foreground = WarnBrush;
                    return;
                }

                Snapshot = snap;
                ImportedXml = row.Xml;
                PreferredTopic = row.Topic;
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                _log.Error($"[History] Apply failed: {ex.Message}", ex);
                statusText.Text = "Could not apply packet: " + ex.Message;
                statusText.Foreground = ErrorBrush;
            }
        }

        private void OnCancel(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        // ───────── Helpers ─────────

        private static string BuildInfo(ObservedMessage msg)
        {
            var parts = new List<string>();
            if (msg.Source != null)
            {
                foreach (var kv in msg.Source)
                    if (!string.IsNullOrEmpty(kv.Key))
                        parts.Add($"[{kv.Key}={kv.Value}]");
            }
            if (msg.Data != null)
            {
                foreach (var kv in msg.Data)
                    if (!string.IsNullOrEmpty(kv.Key))
                        parts.Add($"{kv.Key}={kv.Value}");
            }
            return string.Join("; ", parts);
        }

        private static readonly SolidColorBrush HintBrush  = new SolidColorBrush(Color.FromRgb(0x7A, 0x83, 0x88));
        private static readonly SolidColorBrush OkBrush    = new SolidColorBrush(Color.FromRgb(0x3C, 0xB3, 0x71));
        private static readonly SolidColorBrush WarnBrush  = new SolidColorBrush(Color.FromRgb(0xE6, 0x95, 0x00));
        private static readonly SolidColorBrush ErrorBrush = new SolidColorBrush(Color.FromRgb(0xD8, 0x39, 0x2C));

        // ───────── Row DTO ─────────

        public sealed class PacketRow
        {
            public DateTime CaptureUtc { get; set; }
            public string Topic { get; set; }
            public string Info { get; set; }
            public string Xml { get; set; }
            public string CaptureTimeText =>
                CaptureUtc.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
        }
    }
}
