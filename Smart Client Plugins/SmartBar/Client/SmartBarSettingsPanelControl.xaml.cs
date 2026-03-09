using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace SmartBar.Client
{
    public partial class SmartBarSettingsPanelControl : UserControl
    {
        private readonly ObservableCollection<ProgramEntry> _programs = new ObservableCollection<ProgramEntry>();
        private static readonly Regex ExePathRegex = new Regex(
            @"^(?:[a-zA-Z]:\\|\\\\)(?:[^<>:""/\\|?*\x00-\x1F]+\\)*[^<>:""/\\|?*\x00-\x1F]+\.\w+$|^[^<>:""/\\|?*\x00-\x1F]+\.\w+$");
        private DispatcherTimer _savedTimer;
        private Key _invokeKey;
        private ModifierKeys _invokeModifiers;
        private bool _recordingKey;

        public SmartBarSettingsPanelControl()
        {
            InitializeComponent();

            for (int i = 5; i <= 30; i += 5)
                maxHistoryCombo.Items.Add(i);
            for (int i = 5; i <= 20; i += 5)
                maxRecentCombo.Items.Add(i);

            SmartBarConfig.Load();
            maxHistoryCombo.SelectedItem = SmartBarConfig.MaxHistory;
            maxRecentCombo.SelectedItem = SmartBarConfig.MaxRecent;

            _invokeKey = SmartBarConfig.InvokeKey;
            _invokeModifiers = SmartBarConfig.InvokeModifiers;
            keyRecorderText.Text = FormatKeyCombo(_invokeModifiers, _invokeKey);

            showOutputsCheck.IsChecked = SmartBarConfig.ShowOutputs;
            showEventsCheck.IsChecked = SmartBarConfig.ShowEvents;
            showCommandsCheck.IsChecked = SmartBarConfig.ShowCommands;
            showRecentCheck.IsChecked = SmartBarConfig.ShowRecent;

            foreach (var p in SmartBarConfig.Programs)
            {
                var args = p.Args ?? string.Empty;
                AddTrackedProgram(new ProgramEntry { Name = p.Name, Path = p.Path, Args = args, ArgsVisible = !string.IsNullOrEmpty(args) });
            }

            _programs.CollectionChanged += (s, ev) => ValidateSave();

            programList.ItemsSource = _programs;
            ValidateSave();
        }

        private void AddTrackedProgram(ProgramEntry entry)
        {
            entry.PropertyChanged += (s, ev) => ValidateSave();
            _programs.Add(entry);
        }

        private void ValidateSave()
        {
            string hint = null;
            foreach (var p in _programs)
            {
                if (string.IsNullOrWhiteSpace(p.Name) || string.IsNullOrWhiteSpace(p.Path))
                {
                    hint = "Fill or remove empty entries";
                    break;
                }
                if (!ExePathRegex.IsMatch(p.Path))
                {
                    hint = "Invalid file path: " + p.Path;
                    break;
                }
            }

            saveButton.IsEnabled = hint == null;
            if (hint != null)
            {
                validationHint.Text = hint;
                validationHint.Visibility = Visibility.Visible;
            }
            else
            {
                validationHint.Visibility = Visibility.Collapsed;
            }
        }

        private void OnAddProgram(object sender, RoutedEventArgs e)
        {
            AddTrackedProgram(new ProgramEntry { Name = "", Path = "" });
        }

        private void OnRemoveProgram(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            var entry = btn?.DataContext as ProgramEntry;
            if (entry != null)
                _programs.Remove(entry);
        }

        private void OnToggleArgs(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            var entry = btn?.DataContext as ProgramEntry;
            if (entry == null) return;

            entry.ArgsVisible = !entry.ArgsVisible;
            if (!entry.ArgsVisible)
                entry.Args = string.Empty;
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
            }
        }

        private void OnKeyRecorderClick(object sender, RoutedEventArgs e)
        {
            _recordingKey = true;
            keyRecorderText.Text = "Press a key...";
            keyRecorderBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0x33, 0x99, 0xFF));
            keyRecorderBorder.Focus();
        }

        private static bool IsModifierOnly(Key key)
        {
            return key == Key.LeftShift || key == Key.RightShift
                || key == Key.LeftCtrl || key == Key.RightCtrl
                || key == Key.LeftAlt || key == Key.RightAlt
                || key == Key.LWin || key == Key.RWin;
        }

        private static bool IsReservedBareKey(Key key)
        {
            // Only reserved when pressed WITHOUT Ctrl/Alt modifier
            if (key >= Key.A && key <= Key.Z) return true;
            if (key >= Key.D0 && key <= Key.D9) return true;
            if (key >= Key.NumPad0 && key <= Key.NumPad9) return true;
            if (key == Key.Escape || key == Key.Enter || key == Key.Return) return true;
            if (key == Key.Up || key == Key.Down || key == Key.Tab) return true;
            if (key == Key.Back || key == Key.Delete) return true;
            return false;
        }

        private static string FormatKeyCombo(ModifierKeys mods, Key key)
        {
            var parts = new System.Collections.Generic.List<string>();
            if ((mods & ModifierKeys.Control) != 0) parts.Add("Ctrl");
            if ((mods & ModifierKeys.Alt) != 0) parts.Add("Alt");
            if ((mods & ModifierKeys.Shift) != 0) parts.Add("Shift");
            parts.Add(key.ToString());
            return string.Join("+", parts);
        }

        private void OnKeyRecorderKeyDown(object sender, KeyEventArgs e)
        {
            if (!_recordingKey) return;
            e.Handled = true;

            var key = e.Key == Key.System ? e.SystemKey : e.Key;

            // Ignore modifier-only presses — wait for the actual key
            if (IsModifierOnly(key)) return;

            // Strip Win key from modifiers (we don't support it)
            var mods = Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift);

            if (key == Key.Escape && mods == ModifierKeys.None)
            {
                // Cancel — restore previous
                keyRecorderText.Text = FormatKeyCombo(_invokeModifiers, _invokeKey);
            }
            else if (mods == ModifierKeys.None && IsReservedBareKey(key))
            {
                keyRecorderText.Text = key + " (reserved)";
                return; // stay in recording mode
            }
            else
            {
                _invokeModifiers = mods;
                _invokeKey = key;
                keyRecorderText.Text = FormatKeyCombo(mods, key);
            }

            _recordingKey = false;
            keyRecorderBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44));
        }

        private void OnKeyRecorderLostFocus(object sender, RoutedEventArgs e)
        {
            if (!_recordingKey) return;
            _recordingKey = false;
            keyRecorderText.Text = FormatKeyCombo(_invokeModifiers, _invokeKey);
            keyRecorderBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44));
        }

        private void OnSave(object sender, RoutedEventArgs e)
        {
            Save();
            savedHint.Visibility = Visibility.Visible;
            if (_savedTimer != null) _savedTimer.Stop();
            _savedTimer = new DispatcherTimer { Interval = System.TimeSpan.FromSeconds(3) };
            _savedTimer.Tick += (s2, e2) =>
            {
                _savedTimer.Stop();
                savedHint.Visibility = Visibility.Collapsed;
            };
            _savedTimer.Start();
        }

        public void Save()
        {
            SmartBarConfig.MaxHistory = maxHistoryCombo.SelectedItem is int val ? val : 20;
            SmartBarConfig.MaxRecent = maxRecentCombo.SelectedItem is int mr ? mr : 10;
            SmartBarConfig.InvokeKey = _invokeKey;
            SmartBarConfig.InvokeModifiers = _invokeModifiers;
            SmartBarConfig.ShowOutputs = showOutputsCheck.IsChecked == true;
            SmartBarConfig.ShowEvents = showEventsCheck.IsChecked == true;
            SmartBarConfig.ShowCommands = showCommandsCheck.IsChecked == true;
            SmartBarConfig.ShowRecent = showRecentCheck.IsChecked == true;

            SmartBarConfig.Programs.Clear();
            foreach (var p in _programs)
            {
                if (!string.IsNullOrWhiteSpace(p.Name) && !string.IsNullOrWhiteSpace(p.Path))
                    SmartBarConfig.Programs.Add(new ProgramEntry { Name = p.Name, Path = p.Path, Args = p.Args?.Trim() ?? string.Empty });
            }

            SmartBarConfig.Save();
            SmartBarHistory.ApplyMaxHistory(SmartBarConfig.MaxHistory);
        }
    }
}
