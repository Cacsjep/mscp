using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

namespace SmartBar.Client
{
    public partial class SmartBarSettingsPanelControl : UserControl
    {
        private readonly ObservableCollection<ProgramEntry> _programs = new ObservableCollection<ProgramEntry>();
        private readonly ObservableCollection<CategoryConfig> _categories = new ObservableCollection<CategoryConfig>();
        private static readonly Regex ExePathRegex = new Regex(
            @"^(?:[a-zA-Z]:\\|\\\\)(?:[^<>:""/\\|?*\x00-\x1F]+\\)*[^<>:""/\\|?*\x00-\x1F]+\.\w+$|^[^<>:""/\\|?*\x00-\x1F]+\.\w+$");
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

            // Layout
            columnLayoutCheck.IsChecked = SmartBarConfig.ColumnLayout;
            paletteWidthBox.Text = SmartBarConfig.PaletteWidth.ToString();
            paletteHeightBox.Text = SmartBarConfig.PaletteHeight.ToString();
            paletteWidthBox.TextChanged += (s, ev) => ValidateSave();
            paletteHeightBox.TextChanged += (s, ev) => ValidateSave();
            UpdateColumnSettingsVisibility();

            // Categories
            foreach (var cat in SmartBarConfig.Categories.OrderBy(c => c.Column).ThenBy(c => c.Order))
            {
                var cc = new CategoryConfig
                {
                    Category = cat.Category,
                    Enabled = cat.Enabled,
                    Column = cat.Column,
                    Order = cat.Order
                };
                cc.PropertyChanged += OnCategoryPropertyChanged;
                _categories.Add(cc);
            }
            categoryList.ItemsSource = CollectionViewSource.GetDefaultView(_categories);

            // Programs
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

            if (!int.TryParse(paletteWidthBox.Text, out var pw) || pw < 10 || pw > 100)
                hint = "Smart Bar width must be 10\u2013100%";
            else if (!int.TryParse(paletteHeightBox.Text, out var ph) || ph < 10 || ph > 100)
                hint = "Smart Bar max height must be 10\u2013100%";

            if (hint == null)
            {
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
            }

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

        private void OnColumnLayoutChanged(object sender, RoutedEventArgs e)
        {
            UpdateColumnSettingsVisibility();
            ValidateSave();
        }

        private void UpdateColumnSettingsVisibility()
        {
            var colChecked = columnLayoutCheck.IsChecked == true;

            if (colHeaderText != null)
                colHeaderText.Visibility = colChecked ? Visibility.Visible : Visibility.Collapsed;

            // Toggle grouping and sorting
            var view = CollectionViewSource.GetDefaultView(_categories);
            if (view != null)
            {
                view.GroupDescriptions.Clear();
                view.SortDescriptions.Clear();
                if (colChecked)
                {
                    view.GroupDescriptions.Add(new PropertyGroupDescription("Column"));
                    view.SortDescriptions.Add(new SortDescription("Column", ListSortDirection.Ascending));
                }
                view.SortDescriptions.Add(new SortDescription("Order", ListSortDirection.Ascending));
                view.Refresh();
            }
        }


        private void OnCategoryPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "Column")
                CollectionViewSource.GetDefaultView(_categories).Refresh();
        }

        private void OnMoveCategoryUp(object sender, RoutedEventArgs e)
        {
            var entry = ((Button)sender).DataContext as CategoryConfig;
            if (entry == null) return;
            bool colMode = columnLayoutCheck.IsChecked == true;
            var candidates = colMode
                ? _categories.Where(c => c.Column == entry.Column && c.Order < entry.Order)
                : _categories.Where(c => c.Order < entry.Order);
            var sibling = candidates.OrderByDescending(c => c.Order).FirstOrDefault();
            if (sibling == null) return;
            var tmp = entry.Order;
            entry.Order = sibling.Order;
            sibling.Order = tmp;
            CollectionViewSource.GetDefaultView(_categories).Refresh();
        }

        private void OnMoveCategoryDown(object sender, RoutedEventArgs e)
        {
            var entry = ((Button)sender).DataContext as CategoryConfig;
            if (entry == null) return;
            bool colMode = columnLayoutCheck.IsChecked == true;
            var candidates = colMode
                ? _categories.Where(c => c.Column == entry.Column && c.Order > entry.Order)
                : _categories.Where(c => c.Order > entry.Order);
            var sibling = candidates.OrderBy(c => c.Order).FirstOrDefault();
            if (sibling == null) return;
            var tmp = entry.Order;
            entry.Order = sibling.Order;
            sibling.Order = tmp;
            CollectionViewSource.GetDefaultView(_categories).Refresh();
        }

        private void OnRestoreDefaults(object sender, RoutedEventArgs e)
        {
            // General
            _invokeKey = Key.Space;
            _invokeModifiers = ModifierKeys.None;
            keyRecorderText.Text = FormatKeyCombo(_invokeModifiers, _invokeKey);

            // History
            maxHistoryCombo.SelectedItem = 20;
            maxRecentCombo.SelectedItem = 10;

            // Layout - unhook event to avoid premature refresh
            columnLayoutCheck.Checked -= OnColumnLayoutChanged;
            columnLayoutCheck.Unchecked -= OnColumnLayoutChanged;
            columnLayoutCheck.IsChecked = false;
            paletteWidthBox.Text = "50";
            paletteHeightBox.Text = "60";

            // Categories - assign sequential Order for flat display
            _categories.Clear();
            var defaults = SmartBarConfig.GetDefaultCategories()
                .OrderBy(c => c.Column).ThenBy(c => c.Order).ToList();
            for (int i = 0; i < defaults.Count; i++)
            {
                defaults[i].Order = i + 1;
                defaults[i].PropertyChanged += OnCategoryPropertyChanged;
                _categories.Add(defaults[i]);
            }

            // Re-hook and refresh
            columnLayoutCheck.Checked += OnColumnLayoutChanged;
            columnLayoutCheck.Unchecked += OnColumnLayoutChanged;
            UpdateColumnSettingsVisibility();

            // Programs
            _programs.Clear();
            AddTrackedProgram(new ProgramEntry { Name = "Notepad", Path = "notepad.exe" });

            ValidateSave();
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

            if (IsModifierOnly(key)) return;

            var mods = Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift);

            if (key == Key.Escape && mods == ModifierKeys.None)
            {
                keyRecorderText.Text = FormatKeyCombo(_invokeModifiers, _invokeKey);
            }
            else if (mods == ModifierKeys.None && IsReservedBareKey(key))
            {
                keyRecorderText.Text = key + " (reserved)";
                return;
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

        public void Save()
        {
            SmartBarConfig.MaxHistory = maxHistoryCombo.SelectedItem is int val ? val : 20;
            SmartBarConfig.MaxRecent = maxRecentCombo.SelectedItem is int mr ? mr : 10;
            SmartBarConfig.InvokeKey = _invokeKey;
            SmartBarConfig.InvokeModifiers = _invokeModifiers;

            SmartBarConfig.ColumnLayout = columnLayoutCheck.IsChecked == true;
            SmartBarConfig.PaletteWidth = int.TryParse(paletteWidthBox.Text, out var pw) ? pw : 50;
            SmartBarConfig.PaletteHeight = int.TryParse(paletteHeightBox.Text, out var ph) ? ph : 60;

            SmartBarConfig.Categories.Clear();
            foreach (var cat in _categories)
            {
                SmartBarConfig.Categories.Add(new CategoryConfig
                {
                    Category = cat.Category,
                    Enabled = cat.Enabled,
                    Column = cat.Column,
                    Order = cat.Order
                });
            }

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
