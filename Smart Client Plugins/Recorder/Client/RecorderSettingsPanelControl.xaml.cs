using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Controls;
using System.Windows.Forms;

namespace Recorder.Client
{
    public partial class RecorderSettingsPanelControl : System.Windows.Controls.UserControl
    {
        public List<MonitorItem> Monitors { get; }

        public RecorderSettingsPanelControl()
        {
            InitializeComponent();

            var config = RecorderConfig.Load();
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
            var config = new RecorderConfig();
            var enabled = Monitors.Where(m => m.IsEnabled).Select(m => m.DeviceName).ToList();

            // If all selected, store empty set (= capture all)
            if (enabled.Count < Monitors.Count)
                config.EnabledMonitors = new System.Collections.Generic.HashSet<string>(enabled);

            config.Save();
            return true;
        }
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
