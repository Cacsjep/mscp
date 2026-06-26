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
        private const int StorageRefreshEveryTicks = 5;     // every 5th 2s tick (~10s) do a full fetch
        private static readonly TimeSpan LiveInterval = TimeSpan.FromSeconds(2);

        private bool _closing;
        private bool _fullLoading;
        private bool _liveBusy;
        private bool _autoRefresh = true;
        private string _statusMode = "All";        // All | Online | Offline
        private string _viewMode = "Cameras";      // Cameras | Streams
        private bool _grouping;                     // group cameras by recorder / streams by camera
        private bool _foldering;                     // additionally group cameras by device-tree folder

        // Persistent, in-place-updated collections (preserve selection/scroll/sort across refreshes).
        private readonly ObservableCollection<StorageRow> _storages = new ObservableCollection<StorageRow>();
        private readonly ObservableCollection<CameraHealthRow> _cameras = new ObservableCollection<CameraHealthRow>();
        private readonly ObservableCollection<StreamStatRow> _streams = new ObservableCollection<StreamStatRow>();
        private readonly ObservableCollection<UserRow> _users = new ObservableCollection<UserRow>();
        private readonly Dictionary<Guid, CameraHealthRow> _cameraById = new Dictionary<Guid, CameraHealthRow>();
        private readonly Dictionary<string, StorageRow> _storageByKey = new Dictionary<string, StorageRow>();
        private int _recorderCount;
        private ICollectionView _camView;
        private ICollectionView _streamView;
        private int _tickCount;

        private DispatcherTimer _liveTimer;
        // _masterCts cancels EVERYTHING when the window closes. _rangeCts (linked to it) is recreated
        // on each full refresh so a new refresh cancels the previous range-loading sweep.
        private readonly CancellationTokenSource _masterCts = new CancellationTokenSource();
        private CancellationTokenSource _rangeCts;

        public SystemHealthWindow()
        {
            InitializeComponent();

            // Default = comfortable size, and that is also the minimum; the user can enlarge up to
            // the monitor's working area.
            var wa = SystemParameters.WorkArea;
            double w = Math.Min(1500, wa.Width - 60);
            double h = Math.Min(900, wa.Height - 60);
            Width = w; Height = h;
            MinWidth = w; MinHeight = h;
            MaxWidth = wa.Width; MaxHeight = wa.Height;

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
            HighlightGroupButton();
            HighlightFolderButton();
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
                ShowOverlay("No data - background plugin unavailable.");
            }
            else
            {
                MergeStorages(result.Storages);
                ReplaceList(_users, result.Users);
                MergeCameras(result.Cameras, resetRanges: true);
                RenderErrors(result);
                _recorderCount = result.RecorderCount;
                UpdateDashboard();
                if (_cameras.Count == 0 && _storages.Count == 0 && _users.Count == 0)
                    ShowOverlay(result.Errors.Count > 0 ? "No data - see details below." : "No data.");
                else
                    HideOverlay();
                // Cancel any prior range sweep, then (re)load ranges - but only for cameras whose
                // recorder answered. Querying sequences against an offline recorder throws an
                // UnreachableServer exception (and hangs), so skip those (they show "-").
                try { _rangeCts?.Cancel(); } catch { }
                _rangeCts = CancellationTokenSource.CreateLinkedTokenSource(_masterCts.Token);
                LoadRanges(_cameras.Where(c => c.RecorderReachable
                                            && c.RangeStatus == CameraHealthRow.RangeState.NotLoaded).ToList());
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
                _tickCount++;
                // Most ticks are a cheap stats-only refresh; every Nth tick we do a full fetch so the
                // storage table, recorder capacity (the storage-% denominator) and recorder state stay
                // current and recover after a recording server comes back online.
                if (_tickCount % StorageRefreshEveryTicks == 0)
                {
                    var snap = await Task.Run(() => plugin.FetchSystemHealth());
                    if (_closing || !_autoRefresh) return;
                    MergeStorages(snap.Storages);
                    ReplaceList(_users, snap.Users);
                    MergeCameras(snap.Cameras, resetRanges: false);
                    RenderErrors(snap);
                    _recorderCount = snap.RecorderCount;
                    UpdateDashboard();
                }
                else
                {
                    var fresh = await Task.Run(() => plugin.FetchLiveCameraStats());
                    if (_closing || !_autoRefresh) return;
                    MergeCameras(fresh, resetRanges: false);
                    UpdateDashboard();
                }
            }
            catch (Exception ex)
            {
                SystemStatusDefinition.Log.Error("Live tick failed", ex);
            }
            finally { _liveBusy = false; }
        }

        // Reconcile the persistent storage collection in place (preserve sort/selection).
        private void MergeStorages(IReadOnlyList<StorageRow> fresh)
        {
            var seen = new HashSet<string>();
            foreach (var f in fresh)
            {
                seen.Add(f.Key);
                if (_storageByKey.TryGetValue(f.Key, out var existing)) existing.ApplyFrom(f);
                else { _storages.Add(f); _storageByKey[f.Key] = f; }
            }
            for (int i = _storages.Count - 1; i >= 0; i--)
            {
                var s = _storages[i];
                if (!seen.Contains(s.Key)) { _storages.RemoveAt(i); _storageByKey.Remove(s.Key); }
            }
        }

        // Reconcile the persistent camera collection with a freshly-fetched set (UI thread).
        private void MergeCameras(IReadOnlyList<CameraHealthRow> fresh, bool resetRanges)
        {
            var seen = new HashSet<Guid>();
            var newlyAdded = new List<CameraHealthRow>();
            var recovered = new List<CameraHealthRow>();

            foreach (var f in fresh)
            {
                seen.Add(f.Id);
                if (_cameraById.TryGetValue(f.Id, out var existing))
                {
                    bool wasReachable = existing.RecorderReachable;
                    existing.ApplyLiveFrom(f);
                    if (resetRanges)
                        existing.RangeStatus = CameraHealthRow.RangeState.NotLoaded;
                    else if (!wasReachable && existing.RecorderReachable
                             && existing.RangeStatus != CameraHealthRow.RangeState.Loaded
                             && existing.RangeStatus != CameraHealthRow.RangeState.Loading)
                        recovered.Add(existing); // recorder came back - its range was never loaded
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

            // On live ticks, load ranges for brand-new and recovered cameras on reachable recorders.
            if (!resetRanges)
            {
                var loadable = newlyAdded.Concat(recovered).Where(c => c.RecorderReachable).ToList();
                if (loadable.Count > 0) LoadRanges(loadable);
            }
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

        private void RenderErrors(SystemHealthSnapshot r)
        {
            if (r.Errors.Count > 0)
            {
                errorText.Text = string.Join(Environment.NewLine, r.Errors.Take(8))
                                 + (r.Errors.Count > 8 ? $"{Environment.NewLine}… and {r.Errors.Count - 8} more" : "");
                errorBox.Visibility = Visibility.Visible;
            }
            else errorBox.Visibility = Visibility.Collapsed;
        }

        private static void ReplaceList<T>(ObservableCollection<T> target, IReadOnlyList<T> source)
        {
            target.Clear();
            foreach (var x in source) target.Add(x);
        }

        // ── Header KPI chips ──────────────────────────────────────────────────
        // A storage at/above this fill counts as a "Storage Alert" (matches PercentToBrushConverter's orange band).
        private const double StorageAlertPercent = 90;

        // Recompute the header summary chips from the current in-memory collections. Cheap; called
        // after every merge.
        private void UpdateDashboard()
        {
            int offline = _cameras.Count(c => c.ConnectivityState == "Offline");

            int storageAlert = _storages.Count(s => s.HasTotal && s.UsedPercentValue >= StorageAlertPercent);

            int recorders = _recorderCount > 0
                ? _recorderCount
                : _storages.Select(s => s.RecorderHost).Distinct(StringComparer.OrdinalIgnoreCase).Count();

            kpiRecorders.Text = recorders.ToString();
            kpiCameras.Text = _cameras.Count.ToString();
            kpiOffline.Text = offline.ToString();
            kpiAlert.Text = storageAlert.ToString();

            // The "problem" chips appear only when there is something to report.
            chipOffline.Visibility = offline > 0 ? Visibility.Visible : Visibility.Collapsed;
            chipAlert.Visibility = storageAlert > 0 ? Visibility.Visible : Visibility.Collapsed;

            UpdateRecorderAggregates();
        }

        // Per recorder, sum live camera bandwidth and count online/total cameras, then push both onto
        // every storage row of that recorder so the servers table shows throughput and a "online/all"
        // camera count.
        private void UpdateRecorderAggregates()
        {
            var bpsByHost = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            var onlineByHost = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var totalByHost = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in _cameras)
            {
                if (string.IsNullOrEmpty(c.RecorderHost)) continue;
                bpsByHost.TryGetValue(c.RecorderHost, out var sum);
                bpsByHost[c.RecorderHost] = sum + c.BitrateValue;

                totalByHost.TryGetValue(c.RecorderHost, out var tot);
                totalByHost[c.RecorderHost] = tot + 1;
                if (c.ConnectivityState == "Online")
                {
                    onlineByHost.TryGetValue(c.RecorderHost, out var on);
                    onlineByHost[c.RecorderHost] = on + 1;
                }
            }
            foreach (var s in _storages)
            {
                var host = s.RecorderHost ?? "";
                bpsByHost.TryGetValue(host, out var bps);
                s.RecorderBandwidthValue = bps;
                onlineByHost.TryGetValue(host, out var online);
                totalByHost.TryGetValue(host, out var total);
                s.SetRecorderCameraCounts(online, total);
            }
        }

        private void OnAboutClick(object sender, RoutedEventArgs e)
        {
            var ver = GetType().Assembly.GetName().Version;
            MessageBox.Show(this,
                "System Health" + Environment.NewLine +
                "Live recorder, camera and storage overview for XProtect Smart Client." + Environment.NewLine +
                Environment.NewLine + "Version " + ver,
                "About System Health", MessageBoxButton.OK, MessageBoxImage.Information);
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
            foreach (var btn in new[] { fltAll, fltOnline, fltOffline })
                if (btn != null) btn.Visibility = statusVis;
            // Folder grouping is a cameras-only feature; hide its toggle in the streams view.
            if (folderButton != null) folderButton.Visibility = statusVis;
        }

        // ── Grouping ──────────────────────────────────────────────────────────
        // Two independent toggles for the cameras view: "Group" (by recorder) and "Folder" (by
        // device-tree folder). Both on => nested recorder -> folder grouping. The streams view only
        // honours "Group" (by camera); folder has no meaning there.
        private void OnGroupToggleClick(object sender, RoutedEventArgs e)
        {
            _grouping = !_grouping;
            HighlightGroupButton();
            RefreshGrouping();
        }

        private void OnFolderToggleClick(object sender, RoutedEventArgs e)
        {
            _foldering = !_foldering;
            HighlightFolderButton();
            RefreshGrouping();
        }

        private void RefreshGrouping()
        {
            if (_camView != null)
            {
                _camView.GroupDescriptions.Clear();
                if (_grouping) _camView.GroupDescriptions.Add(new PropertyGroupDescription("RecorderHost"));
                if (_foldering) _camView.GroupDescriptions.Add(new PropertyGroupDescription("FolderGroup"));
            }
            if (_streamView != null)
            {
                _streamView.GroupDescriptions.Clear();
                if (_grouping) _streamView.GroupDescriptions.Add(new PropertyGroupDescription("CameraName"));
            }
        }

        private void HighlightGroupButton()
        {
            groupButton.Background = _grouping ? (Brush)FindResource("ScAccent") : Brushes.Transparent;
            groupButton.Foreground = _grouping ? Brushes.White : (Brush)FindResource("ScSubtle");
        }

        private void HighlightFolderButton()
        {
            folderButton.Background = _foldering ? (Brush)FindResource("ScAccent") : Brushes.Transparent;
            folderButton.Foreground = _foldering ? Brushes.White : (Brush)FindResource("ScSubtle");
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
            foreach (var b in new[] { fltAll, fltOnline, fltOffline })
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
                { s.RecorderHost, s.State, s.Kind, s.StorageName, s.Path, s.RecorderCamerasText, s.RecorderBandwidthText, s.Used, s.Free, s.Total, s.UsedPercent, s.AvailableText });
            ExportSafely("recording-servers", new[] { "Recorder", "State", "Kind", "Storage", "Path", "Cameras", "Bandwidth", "Used", "Free", "Total", "Used %", "Available" }, rows);
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
