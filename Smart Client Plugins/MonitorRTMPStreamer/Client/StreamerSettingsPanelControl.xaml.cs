using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace MonitorRTMPStreamer.Client
{
    public partial class StreamerSettingsPanelControl : System.Windows.Controls.UserControl, INotifyPropertyChanged
    {
        public List<MonitorItem> Monitors { get; }
        private readonly DispatcherTimer _refreshTimer;
        private bool _waitingForRestart;

        private string _rtmpUrl;
        public string RtmpUrl
        {
            get => _rtmpUrl;
            set { _rtmpUrl = value; OnPropertyChanged(); }
        }

        public StreamerSettingsPanelControl()
        {
            InitializeComponent();
            DataContext = this;

            var config = StreamerConfig.Load();
            RtmpUrl = config.RtmpUrl;

            Monitors = Screen.AllScreens.Select((s, i) => new MonitorItem
            {
                DeviceName = s.DeviceName,
                DisplayName = $"{i + 1}",
                Resolution = $"{s.Bounds.Width} x {s.Bounds.Height}",
                IsPrimary = s.Primary,
                BoundsX = s.Bounds.X,
                BoundsY = s.Bounds.Y,
                BoundsW = s.Bounds.Width,
                BoundsH = s.Bounds.Height,
                IsEnabled = config.IsMonitorEnabled(s),
            }).ToList();

            for (int i = 1; i <= 10; i++)
                FpsCombo.Items.Add(new ComboBoxItem { Content = $"{i} FPS" });
            FpsCombo.SelectedIndex = Math.Min(config.Fps - 1, 9);

            Loaded += (s, e) => DrawMonitorLayout();
            MonitorCanvas.SizeChanged += (s, e) => DrawMonitorLayout();

            _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _refreshTimer.Tick += (s, e) => RefreshStatus();
            _refreshTimer.Start();
            RefreshStatus();
        }

        private void DrawMonitorLayout()
        {
            MonitorCanvas.Children.Clear();
            if (Monitors.Count == 0) return;

            var minX = Monitors.Min(m => m.BoundsX);
            var minY = Monitors.Min(m => m.BoundsY);
            var maxX = Monitors.Max(m => m.BoundsX + m.BoundsW);
            var maxY = Monitors.Max(m => m.BoundsY + m.BoundsH);

            var totalW = maxX - minX;
            var totalH = maxY - minY;

            var canvasW = MonitorCanvas.ActualWidth;
            if (canvasW <= 0) return;
            var canvasH = MonitorCanvas.Height;

            var padding = 15.0;
            var availW = canvasW - padding * 2;
            var availH = canvasH - padding * 2;

            var scale = Math.Min(availW / totalW, availH / totalH);

            var offsetX = padding + (availW - totalW * scale) / 2;
            var offsetY = padding + (availH - totalH * scale) / 2;

            foreach (var mon in Monitors)
            {
                var x = offsetX + (mon.BoundsX - minX) * scale;
                var y = offsetY + (mon.BoundsY - minY) * scale;
                var w = mon.BoundsW * scale;
                var h = mon.BoundsH * scale;

                var border = new Border
                {
                    Width = w - 3,
                    Height = h - 3,
                    CornerRadius = new CornerRadius(4),
                    BorderThickness = new Thickness(2),
                    BorderBrush = mon.IsEnabled ? new SolidColorBrush(Color.FromRgb(0x33, 0x99, 0xFF)) : Brushes.Gray,
                    Background = mon.IsEnabled ? new SolidColorBrush(Color.FromArgb(0x40, 0x33, 0x99, 0xFF)) : new SolidColorBrush(Color.FromArgb(0x20, 0x88, 0x88, 0x88)),
                    Cursor = System.Windows.Input.Cursors.Hand,
                    Tag = mon,
                };

                var label = new TextBlock
                {
                    Text = mon.DisplayName,
                    FontSize = 18,
                    FontWeight = FontWeights.Bold,
                    Foreground = mon.IsEnabled ? Brushes.White : Brushes.Gray,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                };

                var resLabel = new TextBlock
                {
                    Text = mon.Resolution,
                    FontSize = 10,
                    Foreground = mon.IsEnabled ? new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA)) : Brushes.DimGray,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    Margin = new Thickness(0, 2, 0, 0),
                };

                var stack = new StackPanel
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                };
                stack.Children.Add(label);
                stack.Children.Add(resLabel);

                border.Child = stack;

                border.MouseLeftButtonUp += (s, e) =>
                {
                    var m = (MonitorItem)((Border)s).Tag;
                    m.IsEnabled = !m.IsEnabled;
                    DrawMonitorLayout();
                };

                Canvas.SetLeft(border, x);
                Canvas.SetTop(border, y);
                MonitorCanvas.Children.Add(border);
            }
        }

        private void RefreshStatus()
        {
            var st = StreamerStatus.Instance;

            CaptureStatus.Text = st.IsCapturing
                ? $"{st.MonitorCount} monitor(s) active"
                : "Waiting...";

            ResolutionStatus.Text = st.StitchedWidth > 0
                ? $"{st.StitchedWidth} x {st.StitchedHeight}"
                : "-";

            CaptureModeStatus.Text = !string.IsNullOrEmpty(st.CaptureMethodName) ? st.CaptureMethodName : "-";

            PerfStatus.Text = st.IsCapturing
                ? $"capture {st.CaptureMs}ms + encode {st.EncodeMs}ms = {st.TotalMs}ms total"
                : "-";

            // Calculate and show max sustainable FPS based on measured cycle time
            if (st.IsCapturing && st.TotalMs > 0)
            {
                var maxFps = Math.Min(10, (int)(1000.0 / st.TotalMs));
                maxFps = Math.Max(1, maxFps);
                MaxFpsStatus.Text = $"~{maxFps} FPS (based on {st.TotalMs}ms per frame)";

                var selectedFps = FpsCombo.SelectedIndex + 1;
                var frameBudget = 1000 / selectedFps;
                if (st.TotalMs > frameBudget)
                    FpsHint.Text = $"Warning: {selectedFps} FPS needs < {frameBudget}ms, but cycle takes {st.TotalMs}ms";
                else
                    FpsHint.Text = $"{frameBudget - st.TotalMs}ms headroom per frame";
            }
            else
            {
                MaxFpsStatus.Text = "-";
                FpsHint.Text = "";
            }

            if (st.IsStreaming)
            {
                StreamStatus.Text = $"Connected to {st.RtmpUrl}";
                StreamStatus.Foreground = Brushes.LimeGreen;
            }
            else if (!string.IsNullOrWhiteSpace(st.RtmpUrl))
            {
                StreamStatus.Text = "Disconnected";
                StreamStatus.Foreground = Brushes.Orange;
            }
            else
            {
                StreamStatus.Text = "Disabled (no URL configured)";
                StreamStatus.Foreground = Brushes.Gray;
            }

            if (st.StreamStarted.HasValue)
            {
                var uptime = DateTime.Now - st.StreamStarted.Value;
                UptimeStatus.Text = uptime.TotalHours >= 1
                    ? $"{(int)uptime.TotalHours}h {uptime.Minutes}m {uptime.Seconds}s"
                    : uptime.TotalMinutes >= 1
                        ? $"{(int)uptime.TotalMinutes}m {uptime.Seconds}s"
                        : $"{uptime.Seconds}s";
            }
            else
            {
                UptimeStatus.Text = "-";
            }

            ErrorStatus.Text = string.IsNullOrEmpty(st.LastError) ? "" : st.LastError;

            if (_waitingForRestart && st.RestartCompleted)
            {
                st.RestartCompleted = false;
                _waitingForRestart = false;
                SaveButton.IsEnabled = true;
                SaveSpinner.Visibility = Visibility.Collapsed;
            }
        }

        private void Apply_Click(object sender, RoutedEventArgs e)
        {
            SaveConfig();
            SaveButton.IsEnabled = false;
            SaveSpinner.Visibility = Visibility.Visible;
            _waitingForRestart = true;
            StreamerStatus.Instance.RestartRequested = true;
        }

        public bool Save(out string error)
        {
            error = string.Empty;
            SaveConfig();
            StreamerStatus.Instance.RestartRequested = true;
            return true;
        }

        private void SaveConfig()
        {
            var config = new StreamerConfig
            {
                RtmpUrl = RtmpUrl ?? "",
                Fps = FpsCombo.SelectedIndex + 1,
            };

            var enabled = Monitors.Where(m => m.IsEnabled).Select(m => m.DeviceName).ToList();
            if (enabled.Count < Monitors.Count)
                config.EnabledMonitors = new HashSet<string>(enabled);

            config.Save();
        }

        public void StopTimer()
        {
            _refreshTimer?.Stop();
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class MonitorItem : INotifyPropertyChanged
    {
        public string DeviceName { get; set; }
        public string DisplayName { get; set; }
        public string Resolution { get; set; }
        public bool IsPrimary { get; set; }
        public int BoundsX { get; set; }
        public int BoundsY { get; set; }
        public int BoundsW { get; set; }
        public int BoundsH { get; set; }

        private bool _isEnabled;
        public bool IsEnabled
        {
            get => _isEnabled;
            set { _isEnabled = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
