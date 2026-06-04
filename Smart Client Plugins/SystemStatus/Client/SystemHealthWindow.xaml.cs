using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SystemStatus.Background;

namespace SystemStatus.Client
{
    public partial class SystemHealthWindow : Window
    {
        private bool _closing;
        private bool _loading;

        public SystemHealthWindow()
        {
            InitializeComponent();

            // ~70% of the working area, centered.
            var wa = SystemParameters.WorkArea;
            Width = Math.Max(MinWidth, wa.Width * 0.7);
            Height = Math.Max(MinHeight, wa.Height * 0.7);

            Loaded += (_, __) => Reload();
            PreviewKeyDown += OnKeyDown;
        }

        private async void Reload()
        {
            if (_loading) return;
            _loading = true;
            refreshButton.IsEnabled = false;
            ShowOverlay("Loading…");

            var plugin = SystemStatusBackgroundPlugin.Instance;
            SystemHealthSnapshot result;
            try
            {
                if (plugin == null)
                    result = new SystemHealthSnapshot(null, null, null, new[] { "Background plugin not available." }, 0);
                else
                    result = await Task.Run(() => plugin.FetchSystemHealth());
            }
            catch (Exception ex)
            {
                result = new SystemHealthSnapshot(null, null, null, new[] { "Fetch failed: " + ex.Message }, 0);
            }

            Render(result);
            _loading = false;
            refreshButton.IsEnabled = true;
        }

        private void Render(SystemHealthSnapshot r)
        {
            serversGrid.ItemsSource = r.Storages;
            camerasGrid.ItemsSource = r.Cameras;
            usersGrid.ItemsSource = r.Users;

            serversSummary.Text = r.ServersSummary;
            camerasSummary.Text = r.CamerasSummary;
            usersSummary.Text = r.UsersSummary;
            overallSummary.Text = $"{r.RecorderCount} recorder(s)  ·  {r.Cameras.Count} cameras  ·  {r.Users.Count} users";

            if (r.Errors.Count > 0)
            {
                errorText.Text = string.Join(Environment.NewLine, r.Errors.Take(8))
                                 + (r.Errors.Count > 8 ? $"{Environment.NewLine}… and {r.Errors.Count - 8} more" : "");
                errorBox.Visibility = Visibility.Visible;
            }
            else errorBox.Visibility = Visibility.Collapsed;

            if (r.Storages.Count == 0 && r.Cameras.Count == 0 && r.Users.Count == 0)
                ShowOverlay(r.Errors.Count > 0 ? "No data — see details below." : "No data.");
            else
                HideOverlay();
        }

        // Lazy-load first/last recording timestamps when a camera row is selected (its detail panel opens).
        private void OnCameraSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!(camerasGrid.SelectedItem is CameraHealthRow row)) return;
            if (row.RangeStatus != CameraHealthRow.RangeState.NotLoaded) return;

            var plugin = SystemStatusBackgroundPlugin.Instance;
            if (plugin == null) return;

            row.RangeStatus = CameraHealthRow.RangeState.Loading;
            var id = row.Id;
            Task.Run(() => plugin.FetchRecordingRange(id)).ContinueWith(t =>
            {
                var res = t.Status == TaskStatus.RanToCompletion ? t.Result : RecordingRangeResult.Failed;
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (res != null && res.Ok) row.SetRange(res.First, res.Last);
                    else row.RangeStatus = CameraHealthRow.RangeState.Failed;
                }));
            });
        }

        private void ShowOverlay(string text)
        {
            overlayText.Text = text;
            overlay.Visibility = Visibility.Visible;
        }

        private void HideOverlay() => overlay.Visibility = Visibility.Collapsed;

        private void OnRefreshClick(object sender, RoutedEventArgs e) => Reload();

        private void OnHeaderMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is DependencyObject src)
            {
                DependencyObject d = src;
                while (d != null)
                {
                    if (d is System.Windows.Controls.Primitives.ButtonBase) return;
                    d = System.Windows.Media.VisualTreeHelper.GetParent(d);
                }
            }
            try { DragMove(); } catch { }
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape) { SafeClose(); e.Handled = true; }
            else if (e.Key == Key.F5) { Reload(); e.Handled = true; }
        }

        private void OnCloseClick(object sender, RoutedEventArgs e) => SafeClose();

        private void SafeClose()
        {
            if (_closing) return;
            _closing = true;
            try { Close(); } catch { }
        }
    }
}
