using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Controls;
using System.Windows.Forms;

namespace Recorder.Client
{
    public partial class RecorderSettingsPanelControl : System.Windows.Controls.UserControl, INotifyPropertyChanged
    {
        public List<MonitorItem> Monitors { get; }

        private string _rtmpUrl;
        public string RtmpUrl
        {
            get => _rtmpUrl;
            set { _rtmpUrl = value; OnPropertyChanged(); }
        }

        public RecorderSettingsPanelControl()
        {
            InitializeComponent();
            DataContext = this;

            var config = RecorderConfig.Load();
            RtmpUrl = config.RtmpUrl;

            Monitors = Screen.AllScreens.Select((s, i) => new MonitorItem
            {
                DeviceName = s.DeviceName,
                DisplayName = $"Monitor {i + 1}",
                Resolution = $"{s.Bounds.Width} x {s.Bounds.Height}",
                Position = $"({s.Bounds.X}, {s.Bounds.Y})",
                PrimaryLabel = s.Primary ? "(Primary)" : "",
                IsEnabled = config.IsMonitorEnabled(s),
            }).ToList();

            MonitorList.ItemsSource = Monitors;
        }

        public bool Save(out string error)
        {
            error = string.Empty;

            var config = new RecorderConfig
            {
                RtmpUrl = RtmpUrl ?? "",
            };

            var enabled = Monitors.Where(m => m.IsEnabled).Select(m => m.DeviceName).ToList();
            if (enabled.Count < Monitors.Count)
                config.EnabledMonitors = new HashSet<string>(enabled);

            config.Save();
            return true;
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
        public string Position { get; set; }
        public string PrimaryLabel { get; set; }

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
