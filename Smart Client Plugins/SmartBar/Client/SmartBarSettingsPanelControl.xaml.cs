using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;

namespace SmartBar.Client
{
    public partial class SmartBarSettingsPanelControl : UserControl
    {
        private readonly ObservableCollection<ProgramEntry> _programs = new ObservableCollection<ProgramEntry>();

        public SmartBarSettingsPanelControl()
        {
            InitializeComponent();

            // Max history combo: 5, 10, 15, 20, 25, 30
            for (int i = 5; i <= 30; i += 5)
                maxHistoryCombo.Items.Add(i);

            SmartBarConfig.Load();
            maxHistoryCombo.SelectedItem = SmartBarConfig.MaxHistory;

            foreach (var p in SmartBarConfig.Programs)
                _programs.Add(new ProgramEntry { Name = p.Name, Path = p.Path });

            programList.ItemsSource = _programs;
        }

        private void OnAddProgram(object sender, RoutedEventArgs e)
        {
            _programs.Add(new ProgramEntry { Name = "", Path = "" });
        }

        private void OnRemoveProgram(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            var entry = btn?.DataContext as ProgramEntry;
            if (entry != null)
                _programs.Remove(entry);
        }

        public void Save()
        {
            SmartBarConfig.MaxHistory = maxHistoryCombo.SelectedItem is int val ? val : 20;

            SmartBarConfig.Programs.Clear();
            foreach (var p in _programs)
            {
                if (!string.IsNullOrWhiteSpace(p.Name) && !string.IsNullOrWhiteSpace(p.Path))
                    SmartBarConfig.Programs.Add(new ProgramEntry { Name = p.Name, Path = p.Path });
            }

            SmartBarConfig.Save();

            // Apply max history immediately
            SmartBarHistory.ApplyMaxHistory(SmartBarConfig.MaxHistory);
        }
    }
}
