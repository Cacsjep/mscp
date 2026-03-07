using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace SmartBar.Client
{
    public partial class SmartBarSettingsPanelControl : UserControl
    {
        private readonly ObservableCollection<ProgramEntry> _programs = new ObservableCollection<ProgramEntry>();

        public SmartBarSettingsPanelControl()
        {
            InitializeComponent();

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

        private void OnBrowseProgram(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            var entry = btn?.DataContext as ProgramEntry;
            if (entry == null) return;

            var dlg = new OpenFileDialog
            {
                Title = "Select executable",
                Filter = "Executables (*.exe)|*.exe|All files (*.*)|*.*",
                CheckFileExists = true
            };

            if (dlg.ShowDialog() == true)
            {
                entry.Path = dlg.FileName;
                if (string.IsNullOrWhiteSpace(entry.Name))
                    entry.Name = System.IO.Path.GetFileNameWithoutExtension(dlg.FileName);

                // Refresh the list to show updated values
                var idx = _programs.IndexOf(entry);
                if (idx >= 0)
                {
                    _programs.RemoveAt(idx);
                    _programs.Insert(idx, entry);
                }
            }
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
            SmartBarHistory.ApplyMaxHistory(SmartBarConfig.MaxHistory);
        }
    }
}
