using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SystemStatus.Background;

namespace SystemStatus.Client
{
    public partial class StatusFlyoutWindow : Window
    {
        private bool _subscribed;
        private bool _closing;
        private bool _showOnlyOffline;
        private StatusSnapshot _last = StatusSnapshot.Empty;

        public StatusFlyoutWindow()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            ContentRendered += (_, __) => PositionAtCursor();
            PreviewKeyDown += OnKeyDown;
            Closed += OnClosed;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            var plugin = SystemStatusBackgroundPlugin.Instance;
            if (plugin != null)
            {
                Render(plugin.CurrentSnapshot);
                plugin.StatusChanged += OnStatusChanged;
                _subscribed = true;
            }
            else
            {
                Render(StatusSnapshot.Empty);
            }
            PositionAtCursor();
        }

        private void OnClosed(object sender, EventArgs e)
        {
            if (_subscribed && SystemStatusBackgroundPlugin.Instance != null)
            {
                SystemStatusBackgroundPlugin.Instance.StatusChanged -= OnStatusChanged;
                _subscribed = false;
            }
        }

        private void OnStatusChanged(object sender, StatusChangedEventArgs e)
        {
            // Fired on a MIP communication thread - marshal to the UI thread.
            try { Dispatcher.BeginInvoke(new Action(() => Render(e.Snapshot))); }
            catch { /* dispatcher may be shutting down */ }
        }

        private void Render(StatusSnapshot snap)
        {
            if (snap == null) snap = StatusSnapshot.Empty;
            _last = snap;
            usersLabel.Text = $"USERS   (CONNECTED {snap.UserCount})";
            usersList.ItemsSource = snap.Users;
            ApplyCameraFilter();
        }

        private void ApplyCameraFilter()
        {
            // Counts always reflect the full set; the list may be filtered to offline-only.
            var offline = _last.EnabledCount - _last.OnlineCount;
            camerasLabel.Text = _showOnlyOffline
                ? $"CAMERAS   (OFFLINE {offline} / {_last.EnabledCount})"
                : $"CAMERAS   (ONLINE {_last.OnlineCount} / {_last.EnabledCount})";

            camerasList.ItemsSource = _showOnlyOffline
                ? _last.Cameras.Where(c => !c.Online).ToList()
                : (System.Collections.IEnumerable)_last.Cameras;
        }

        private void OnToggleOffline(object sender, RoutedEventArgs e)
        {
            _showOnlyOffline = !_showOnlyOffline;
            offlineToggle.Content = _showOnlyOffline ? "Show all" : "Show only offline";
            offlineToggle.Background = _showOnlyOffline
                ? (System.Windows.Media.Brush)FindResource("ScAccent")
                : System.Windows.Media.Brushes.Transparent;
            offlineToggle.Foreground = _showOnlyOffline
                ? System.Windows.Media.Brushes.White
                : (System.Windows.Media.Brush)FindResource("ScSubtle");
            ApplyCameraFilter();
        }

        private void OnHeaderMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is DependencyObject src)
            {
                DependencyObject d = src;
                while (d != null)
                {
                    if (d is Button) return;
                    d = System.Windows.Media.VisualTreeHelper.GetParent(d);
                }
            }
            try { DragMove(); } catch { }
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                SafeClose();
                e.Handled = true;
            }
        }

        private void OnCloseClick(object sender, RoutedEventArgs e) => SafeClose();

        private void SafeClose()
        {
            if (_closing) return;
            _closing = true;
            try { Close(); } catch { }
        }

        private void PositionAtCursor()
        {
            try
            {
                var pos = System.Windows.Forms.Cursor.Position;
                var screen = System.Windows.Forms.Screen.FromPoint(pos).WorkingArea;

                var width = ActualWidth > 0 ? ActualWidth : 380;
                var height = ActualHeight > 0 ? ActualHeight : 360;

                double left = pos.X - width / 2;
                double top = pos.Y + 8;

                if (left < screen.Left + 8) left = screen.Left + 8;
                if (left + width > screen.Right - 8) left = screen.Right - width - 8;
                if (top + height > screen.Bottom - 8) top = pos.Y - height - 8;
                if (top < screen.Top + 8) top = screen.Top + 8;

                Left = left;
                Top = top;
            }
            catch { }
        }
    }
}
