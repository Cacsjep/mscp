using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using SystemStatus.Background;

namespace SystemStatus.Client
{
    public partial class SystemHealthWindow : Window
    {
        private const int RangeConcurrency = 6;             // parallel recording-range queries
        private static readonly TimeSpan LiveInterval = TimeSpan.FromSeconds(2);

        private bool _closing;
        private bool _fullLoading;
        private bool _liveBusy;
        private bool _autoRefresh = true;
        private string _statusMode = "All";        // All | Online | Offline | Streaming
        private string _viewMode = "Cameras";      // Cameras | Streams

        // Persistent, in-place-updated collections (preserve selection/scroll/sort across refreshes).
        private readonly ObservableCollection<StorageRow> _storages = new ObservableCollection<StorageRow>();
        private readonly ObservableCollection<CameraHealthRow> _cameras = new ObservableCollection<CameraHealthRow>();
        private readonly ObservableCollection<StreamStatRow> _streams = new ObservableCollection<StreamStatRow>();
        private readonly ObservableCollection<UserRow> _users = new ObservableCollection<UserRow>();
        private readonly Dictionary<Guid, CameraHealthRow> _cameraById = new Dictionary<Guid, CameraHealthRow>();
        private ICollectionView _camView;
        private ICollectionView _streamView;

        private DispatcherTimer _liveTimer;
        // _masterCts cancels EVERYTHING when the window closes. _rangeCts (linked to it) is recreated
        // on each full refresh so a new refresh cancels the previous range-loading sweep.
        private readonly CancellationTokenSource _masterCts = new CancellationTokenSource();
        private CancellationTokenSource _rangeCts;

        public SystemHealthWindow()
        {
            InitializeComponent();

            var wa = SystemParameters.WorkArea;
            Width = Math.Min(1500, wa.Width - 60);
            Height = Math.Min(900, wa.Height - 60);
            MaxWidth = wa.Width;
            MaxHeight = wa.Height;

            serversGrid.ItemsSource = _storages;
            usersGrid.ItemsSource = _users;
            _camView = CollectionViewSource.GetDefaultView(_cameras);
            _camView.Filter = CameraFilterPredicate;
            camerasGrid.ItemsSource = _camView;
            _streamView = CollectionViewSource.GetDefaultView(_streams);
            _streamView.Filter = StreamFilterPredicate;
            streamsGrid.ItemsSource = _streamView;

            _liveTimer = new DispatcherTimer { Interval = LiveInterval };
            _liveTimer.Tick += OnLiveTick;

            Loaded += OnLoaded;
            PreviewKeyDown += OnKeyDown;
            Closed += OnClosed;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            HighlightStatusButtons();
            HighlightModeButtons();
            HighlightAutoButton();
            ApplyViewMode();
            FullRefresh();
            if (_autoRefresh) _liveTimer.Start();
        }

        private void OnClosed(object sender, EventArgs e)
        {
            _closing = true;
            _autoRefresh = false;
            try { _liveTimer.Stop(); _liveTimer.Tick -= OnLiveTick; } catch { }
            try { _masterCts.Cancel(); } catch { }
        }

        // ── Full refresh (storage + state + stats + reload ranges) ────────────
        private async void FullRefresh()
        {
            if (_fullLoading || _closing) return;
            _fullLoading = true;
            refreshButton.IsEnabled = false;
            if (_cameras.Count == 0) ShowOverlay("Loading…");

            var plugin = SystemStatusBackgroundPlugin.Instance;
            SystemHealthSnapshot result = null;
            try
            {
                if (plugin != null) result = await Task.Run(() => plugin.FetchSystemHealth());
            }
            catch (Exception ex)
            {
                SystemStatusDefinition.Log.Error("FetchSystemHealth threw", ex);
            }

            if (_closing) { _fullLoading = false; return; }

            if (result == null)
            {
                ReplaceList(_storages, Array.Empty<StorageRow>());
                ShowOverlay("No data — background plugin unavailable.");
            }
            else
            {
                ReplaceList(_storages, result.Storages);
                ReplaceList(_users, result.Users);
                MergeCameras(result.Cameras, resetRanges: true);
                RenderSummaries(result);
                if (_cameras.Count == 0 && _storages.Count == 0 && _users.Count == 0)
                    ShowOverlay(result.Errors.Count > 0 ? "No data — see details below." : "No data.");
                else
                    HideOverlay();
                // Cancel any prior range sweep, then (re)load ranges for all cameras.
                try { _rangeCts?.Cancel(); } catch { }
                _rangeCts = CancellationTokenSource.CreateLinkedTokenSource(_masterCts.Token);
                LoadRanges(_cameras.Where(c => c.RangeStatus == CameraHealthRow.RangeState.NotLoaded).ToList());
            }

            _fullLoading = false;
            refreshButton.IsEnabled = true;
        }

        // ── Live tick (lightweight stats only, in-place merge) ────────────────
        private async void OnLiveTick(object sender, EventArgs e)
        {
            if (_closing || _liveBusy || _fullLoading || !_autoRefresh) return;
            var plugin = SystemStatusBackgroundPlugin.Instance;
            if (plugin == null) return;

            _liveBusy = true;
            try
            {
                var fresh = await Task.Run(() => plugin.FetchLiveCameraStats());
                if (_closing || !_autoRefresh) return;
                MergeCameras(fresh, resetRanges: false);
            }
            catch (Exception ex)
            {
                SystemStatusDefinition.Log.Error("Live tick failed", ex);
            }
            finally { _liveBusy = false; }
        }

        // Reconcile the persistent camera collection with a freshly-fetched set (UI thread).
        private void MergeCameras(IReadOnlyList<CameraHealthRow> fresh, bool resetRanges)
        {
            var seen = new HashSet<Guid>();
            var newlyAdded = new List<CameraHealthRow>();

            foreach (var f in fresh)
            {
                seen.Add(f.Id);
                if (_cameraById.TryGetValue(f.Id, out var existing))
                {
                    existing.ApplyLiveFrom(f);
                    if (resetRanges) existing.RangeStatus = CameraHealthRow.RangeState.NotLoaded;
                }
                else
                {
                    _cameras.Add(f);
                    _cameraById[f.Id] = f;
                    newlyAdded.Add(f);
                }
            }

            // Remove cameras no longer present.
            for (int i = _cameras.Count - 1; i >= 0; i--)
            {
                var c = _cameras[i];
                if (!seen.Contains(c.Id)) { _cameras.RemoveAt(i); _cameraById.Remove(c.Id); }
            }

            SyncStreams();

            // On live ticks, load ranges only for brand-new cameras (full refresh handles the rest).
            if (!resetRanges && newlyAdded.Count > 0) LoadRanges(newlyAdded);
        }

        // Keep the flat stream collection's membership in sync with the cameras' (stable) stream objects.
        private void SyncStreams()
        {
            var desired = new HashSet<StreamStatRow>();
            foreach (var cam in _cameras)
                foreach (var s in cam.Streams) desired.Add(s);

            for (int i = _streams.Count - 1; i >= 0; i--)
                if (!desired.Contains(_streams[i])) _streams.RemoveAt(i);

            var present = new HashSet<StreamStatRow>(_streams);
            foreach (var s in desired)
                if (!present.Contains(s)) _streams.Add(s);
        }

        private void RenderSummaries(SystemHealthSnapshot r)
        {
            serversSummary.Text = r.ServersSummary;
            usersSummary.Text = r.UsersSummary;
            _camerasSummaryText = r.CamerasSummary;
            overallSummary.Text = $"{r.RecorderCount} recorder(s)  ·  {r.Cameras.Count} cameras  ·  {r.Users.Count} users";
            UpdateCamerasSummary();

            if (r.Errors.Count > 0)
            {
                errorText.Text = string.Join(Environment.NewLine, r.Errors.Take(8))
                                 + (r.Errors.Count > 8 ? $"{Environment.NewLine}… and {r.Errors.Count - 8} more" : "");
                errorBox.Visibility = Visibility.Visible;
            }
            else errorBox.Visibility = Visibility.Collapsed;
        }

        private string _camerasSummaryText = "";

        private static void ReplaceList<T>(ObservableCollection<T> target, IReadOnlyList<T> source)
        {
            target.Clear();
            foreach (var x in source) target.Add(x);
        }

        // ── Recording ranges (throttled, cancellable with the window) ─────────
        private void LoadRanges(IReadOnlyList<CameraHealthRow> rows)
        {
            var plugin = SystemStatusBackgroundPlugin.Instance;
            if (plugin == null || rows == null || rows.Count == 0 || _closing) return;
            var token = (_rangeCts ?? _masterCts).Token;

            foreach (var r in rows) r.RangeStatus = CameraHealthRow.RangeState.Loading;

            Task.Run(() =>
            {
                using (var sem = new SemaphoreSlim(RangeConcurrency))
                {
                    var tasks = rows.Select(cam => Task.Run(() =>
                    {
                        try { sem.Wait(token); } catch { return; }
                        try
                        {
                            if (token.IsCancellationRequested) return;
                            var res = plugin.FetchRecordingRange(cam.Id);
                            if (token.IsCancellationRequested) return;
                            Dispatcher.BeginInvoke(new Action(() =>
                            {
                                if (_closing) return;
                                if (res != null && res.Ok) cam.SetRange(res.First, res.Last);
                                else cam.RangeStatus = CameraHealthRow.RangeState.Failed;
                            }));
                        }
                        finally { try { sem.Release(); } catch { } }
                    }, token)).ToArray();
                    try { Task.WaitAll(tasks); } catch { /* cancelled */ }
                }
            }, token);
        }

        // ── Filtering ────────────────────────────────────────────────────────
        private bool CameraFilterPredicate(object o)
        {
            if (!(o is CameraHealthRow c)) return false;
            switch (_statusMode)
            {
                case "Online": if (!c.Online) return false; break;
                case "Offline": if (c.Online) return false; break;
                case "Streaming": if (!c.HasStreams) return false; break;
            }
            var q = cameraFilter?.Text;
            if (string.IsNullOrWhiteSpace(q)) return true;
            q = q.Trim();
            return (c.Name?.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                || (c.RecorderHost?.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private bool StreamFilterPredicate(object o)
        {
            if (!(o is StreamStatRow s)) return false;
            var q = cameraFilter?.Text;
            if (string.IsNullOrWhiteSpace(q)) return true;
            q = q.Trim();
            return (s.CameraName?.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                || (s.RecorderName?.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                || (s.StreamName?.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private void OnCameraFilterTextChanged(object sender, TextChangedEventArgs e)
        {
            _camView?.Refresh();
            _streamView?.Refresh();
        }

        private void OnStatusFilterClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button b && b.Tag is string mode)
            {
                _statusMode = mode;
                HighlightStatusButtons();
                _camView?.Refresh();
            }
        }

        private void OnViewModeClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button b && b.Tag is string mode && mode != _viewMode)
            {
                _viewMode = mode;
                HighlightModeButtons();
                ApplyViewMode();
            }
        }

        private void ApplyViewMode()
        {
            bool cameras = _viewMode == "Cameras";
            if (camerasGrid != null) camerasGrid.Visibility = cameras ? Visibility.Visible : Visibility.Collapsed;
            if (streamsGrid != null) streamsGrid.Visibility = cameras ? Visibility.Collapsed : Visibility.Visible;
            var statusVis = cameras ? Visibility.Visible : Visibility.Collapsed;
            foreach (var btn in new[] { fltAll, fltOnline, fltOffline, fltStreaming })
                if (btn != null) btn.Visibility = statusVis;
            UpdateCamerasSummary();
        }

        private void UpdateCamerasSummary()
        {
            if (camerasSummary == null) return;
            camerasSummary.Text = _viewMode == "Cameras"
                ? _camerasSummaryText
                : $"{_streams.Count} stream(s) across all cameras";
        }

        // ── Auto-refresh toggle ───────────────────────────────────────────────
        private void OnAutoToggleClick(object sender, RoutedEventArgs e)
        {
            _autoRefresh = !_autoRefresh;
            HighlightAutoButton();
            if (_autoRefresh) { _liveTimer.Start(); }
            else { _liveTimer.Stop(); }
        }

        private void HighlightAutoButton()
        {
            autoButton.Background = _autoRefresh ? (Brush)FindResource("ScAccent") : Brushes.Transparent;
            autoButton.Foreground = _autoRefresh ? Brushes.White : (Brush)FindResource("ScSubtle");
            autoText.Text = _autoRefresh ? "Auto 2s" : "Auto off";
        }

        private void HighlightStatusButtons()
        {
            foreach (var b in new[] { fltAll, fltOnline, fltOffline, fltStreaming })
            {
                bool active = (string)b.Tag == _statusMode;
                b.Background = active ? (Brush)FindResource("ScAccent") : Brushes.Transparent;
                b.Foreground = active ? Brushes.White : (Brush)FindResource("ScSubtle");
            }
        }

        private void HighlightModeButtons()
        {
            foreach (var b in new[] { modeCameras, modeStreams })
            {
                bool active = (string)b.Tag == _viewMode;
                b.Background = active ? (Brush)FindResource("ScAccent") : Brushes.Transparent;
                b.Foreground = active ? Brushes.White : (Brush)FindResource("ScSubtle");
            }
        }

        // ── CSV export (exports what's shown: current sort + filter) ──────────
        private void OnExportServers(object sender, RoutedEventArgs e)
        {
            var rows = serversGrid.Items.OfType<StorageRow>()
                .Select(s => (IReadOnlyList<string>)new[]
                { s.RecorderHost, s.State, s.Kind, s.StorageName, s.Path, s.Used, s.Free, s.Total, s.UsedPercent, s.AvailableText });
            ExportSafely("recording-servers", new[] { "Recorder", "State", "Kind", "Storage", "Path", "Used", "Free", "Total", "Used %", "Available" }, rows);
        }

        private void OnExportCameras(object sender, RoutedEventArgs e)
        {
            if (_viewMode == "Streams")
            {
                var rows = streamsGrid.Items.OfType<StreamStatRow>()
                    .Select(s => (IReadOnlyList<string>)new[]
                    { s.CameraName, s.RecorderName, s.StreamName, s.Resolution, s.Codec, s.FpsText, s.FpsRequestedText, s.BitrateText, s.FrameSizeText, s.RoleText });
                ExportSafely("streams", new[] { "Camera", "Recorder", "Stream", "Resolution", "Codec", "FPS", "Req. FPS", "Bitrate", "Avg frame", "Role" }, rows);
            }
            else
            {
                var rows = camerasGrid.Items.OfType<CameraHealthRow>()
                    .Select(c => (IReadOnlyList<string>)new[]
                    { c.Name, c.RecorderHost, c.OnlineText, c.StreamCountText, c.Resolution, c.Codec, c.Fps, c.Bitrate,
                      c.UsedSpaceText, c.StoragePercentText, c.FirstRecordingText, c.LastRecordingText, c.SpanText });
                ExportSafely("cameras", new[] { "Camera", "Recorder", "Status", "Streams", "Resolution", "Codec", "FPS", "Bitrate", "Storage used", "Storage %", "First rec", "Last rec", "Span" }, rows);
            }
        }

        private void OnExportUsers(object sender, RoutedEventArgs e)
        {
            var rows = usersGrid.Items.OfType<UserRow>()
                .Select(u => (IReadOnlyList<string>)new[] { u.DisplayName, u.Secondary });
            ExportSafely("users", new[] { "User", "Client" }, rows);
        }

        private void ExportSafely(string name, IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string>> rows)
        {
            try
            {
                var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
                Csv.Save(this, $"systemhealth-{name}-{stamp}.csv", headers, rows.ToList());
            }
            catch (Exception ex)
            {
                SystemStatusDefinition.Log.Error($"CSV export ({name}) failed", ex);
                MessageBox.Show(this, "Export failed: " + ex.Message, "Export", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        // ── Window chrome ────────────────────────────────────────────────────
        private void ShowOverlay(string text)
        {
            overlayText.Text = text;
            overlay.Visibility = Visibility.Visible;
        }

        private void HideOverlay() => overlay.Visibility = Visibility.Collapsed;

        private void OnRefreshClick(object sender, RoutedEventArgs e) => FullRefresh();

        private void OnHeaderMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is DependencyObject src)
            {
                DependencyObject d = src;
                while (d != null)
                {
                    if (d is System.Windows.Controls.Primitives.ButtonBase) return;
                    if (d is TextBox) return;
                    d = System.Windows.Media.VisualTreeHelper.GetParent(d);
                }
            }
            try { DragMove(); } catch { }
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape) { SafeClose(); e.Handled = true; }
            else if (e.Key == Key.F5) { FullRefresh(); e.Handled = true; }
        }

        private void OnCloseClick(object sender, RoutedEventArgs e) => SafeClose();

        private void SafeClose()
        {
            if (_closing) return;
            try { Close(); } catch { }
        }
    }
}
