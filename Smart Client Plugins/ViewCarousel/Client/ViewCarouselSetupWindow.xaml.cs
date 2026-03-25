using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using VideoOS.Platform;
using VideoOS.Platform.Client;

namespace ViewCarousel.Client
{
    public partial class ViewCarouselSetupWindow : Window
    {
        private const int MinTime = 5;
        private const int MaxTime = 300;

        private readonly ObservableCollection<ViewEntryDisplay> _selectedViews = new ObservableCollection<ViewEntryDisplay>();
        private int _defaultTime;
        private bool _suppressSelectionEvents;

        public List<CarouselViewEntry> ResultEntries { get; private set; }
        public int ResultDefaultTime { get; private set; }

        public ViewCarouselSetupWindow(List<CarouselViewEntry> currentEntries, int defaultTime)
        {
            InitializeComponent();

            _defaultTime = defaultTime;
            txtDefaultTime.Text = defaultTime.ToString();

            foreach (var entry in currentEntries)
            {
                _selectedViews.Add(new ViewEntryDisplay
                {
                    ViewId = entry.ViewId,
                    ViewName = entry.ViewName,
                    CustomTime = entry.CustomTime,
                    DefaultTime = _defaultTime
                });
            }

            selectedList.ItemsSource = _selectedViews;
            LoadTree();
        }

        private void LoadTree()
        {
            List<Item> groups;
            try
            {
                groups = ClientControl.Instance.GetViewGroupItems();
            }
            catch
            {
                return;
            }
            if (groups == null) return;

            foreach (var group in groups)
            {
                var node = CreateTreeNode(group);
                if (node != null)
                    viewTree.Items.Add(node);
            }
        }

        private TreeViewItem CreateTreeNode(Item item)
        {
            bool isFolder = item.FQID.FolderType != FolderType.No;

            if (isFolder)
            {
                var configItem = item as ConfigItem;
                var children = configItem?.GetChildren();
                if (children == null || children.Count == 0)
                    return null;

                string icon = "\U0001F4C1 ";
                var node = new TreeViewItem
                {
                    Header = icon + item.Name,
                    Tag = item,
                    IsExpanded = true
                };

                foreach (var child in children)
                {
                    var childNode = CreateTreeNode(child);
                    if (childNode != null)
                        node.Items.Add(childNode);
                }

                // Hide folder if all children were filtered out
                if (node.Items.Count == 0)
                    return null;

                return node;
            }
            else
            {
                string icon = "\U0001F4CB ";
                return new TreeViewItem
                {
                    Header = icon + item.Name,
                    Tag = item,
                    IsExpanded = false
                };
            }
        }

        private void ViewTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            var selected = viewTree.SelectedItem as TreeViewItem;
            if (selected?.Tag is Item item)
            {
                btnAdd.IsEnabled = item.FQID.FolderType == FolderType.No;
            }
            else
            {
                btnAdd.IsEnabled = false;
            }
        }

        private void OnAddClick(object sender, RoutedEventArgs e)
        {
            var selected = viewTree.SelectedItem as TreeViewItem;
            if (selected?.Tag is Item item && item.FQID.FolderType == FolderType.No)
            {
                var viewId = item.FQID.ObjectId.ToString();
                if (_selectedViews.Any(v => v.ViewId == viewId))
                    return;

                _selectedViews.Add(new ViewEntryDisplay
                {
                    ViewId = viewId,
                    ViewName = item.Name,
                    CustomTime = -1,
                    DefaultTime = _defaultTime
                });
            }
        }

        private void OnRemoveClick(object sender, RoutedEventArgs e)
        {
            if (selectedList.SelectedItem is ViewEntryDisplay entry)
                _selectedViews.Remove(entry);
        }

        private void OnRemoveAllClick(object sender, RoutedEventArgs e)
        {
            _selectedViews.Clear();
        }

        private void OnMoveUpClick(object sender, RoutedEventArgs e)
        {
            int idx = selectedList.SelectedIndex;
            if (idx > 0)
            {
                _selectedViews.Move(idx, idx - 1);
                selectedList.SelectedIndex = idx - 1;
            }
        }

        private void OnMoveDownClick(object sender, RoutedEventArgs e)
        {
            int idx = selectedList.SelectedIndex;
            if (idx >= 0 && idx < _selectedViews.Count - 1)
            {
                _selectedViews.Move(idx, idx + 1);
                selectedList.SelectedIndex = idx + 1;
            }
        }

        private void SelectedList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressSelectionEvents) return;

            var hasSelection = selectedList.SelectedItem != null;
            btnRemove.IsEnabled = hasSelection;
            btnMoveUp.IsEnabled = hasSelection && selectedList.SelectedIndex > 0;
            btnMoveDown.IsEnabled = hasSelection && selectedList.SelectedIndex < _selectedViews.Count - 1;

            if (selectedList.SelectedItem is ViewEntryDisplay entry)
            {
                _suppressSelectionEvents = true;
                if (entry.CustomTime > 0)
                {
                    rbCustom.IsChecked = true;
                    txtCustomTime.Text = entry.CustomTime.ToString();
                    txtCustomTime.IsEnabled = true;
                }
                else
                {
                    rbDefault.IsChecked = true;
                    txtCustomTime.Text = _defaultTime.ToString();
                    txtCustomTime.IsEnabled = false;
                }
                _suppressSelectionEvents = false;
            }
        }

        private static int ClampTime(int value)
        {
            if (value < MinTime) return MinTime;
            if (value > MaxTime) return MaxTime;
            return value;
        }

        private void OnTimeRadioChanged(object sender, RoutedEventArgs e)
        {
            if (txtCustomTime == null || _suppressSelectionEvents) return;

            bool isCustom = rbCustom.IsChecked == true;
            txtCustomTime.IsEnabled = isCustom;

            if (selectedList.SelectedItem is ViewEntryDisplay entry)
            {
                if (!isCustom)
                {
                    entry.CustomTime = -1;
                }
                else
                {
                    if (int.TryParse(txtCustomTime.Text, out int seconds))
                        entry.CustomTime = ClampTime(seconds);
                }
            }
        }

        private void OnCustomTimeLostFocus(object sender, RoutedEventArgs e)
        {
            if (selectedList.SelectedItem is ViewEntryDisplay entry && rbCustom.IsChecked == true)
            {
                if (int.TryParse(txtCustomTime.Text, out int seconds))
                {
                    entry.CustomTime = ClampTime(seconds);
                    txtCustomTime.Text = entry.CustomTime.ToString();
                }
                else
                {
                    entry.CustomTime = _defaultTime;
                    txtCustomTime.Text = _defaultTime.ToString();
                }
            }
        }

        private void OnDefaultTimeChanged(object sender, TextChangedEventArgs e)
        {
            if (int.TryParse(txtDefaultTime.Text, out int seconds) && seconds > 0)
            {
                _defaultTime = ClampTime(seconds);
                foreach (var v in _selectedViews)
                    v.DefaultTime = _defaultTime;
            }
        }

        private void OnDefaultTimeLostFocus(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(txtDefaultTime.Text, out int seconds) || seconds < MinTime || seconds > MaxTime)
            {
                _defaultTime = ClampTime(int.TryParse(txtDefaultTime.Text, out int v) ? v : 10);
                txtDefaultTime.Text = _defaultTime.ToString();
            }
        }

        private void OnTimePreviewTextInput(object sender, System.Windows.Input.TextCompositionEventArgs e)
        {
            foreach (char c in e.Text)
            {
                if (!char.IsDigit(c))
                {
                    e.Handled = true;
                    return;
                }
            }
        }

        private void OnOkClick(object sender, RoutedEventArgs e)
        {
            // Commit any pending custom time edit
            if (selectedList.SelectedItem is ViewEntryDisplay entry && rbCustom.IsChecked == true)
            {
                if (int.TryParse(txtCustomTime.Text, out int seconds))
                    entry.CustomTime = ClampTime(seconds);
            }

            if (!int.TryParse(txtDefaultTime.Text, out int defTime) || defTime < MinTime)
                defTime = 10;
            ResultDefaultTime = ClampTime(defTime);

            ResultEntries = _selectedViews.Select(v => new CarouselViewEntry
            {
                ViewId = v.ViewId,
                ViewName = v.ViewName,
                CustomTime = v.CustomTime
            }).ToList();

            DialogResult = true;
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }

    internal class ViewEntryDisplay : INotifyPropertyChanged
    {
        private int _customTime;
        private int _defaultTime = 10;

        public string ViewId { get; set; }
        public string ViewName { get; set; }

        public int CustomTime
        {
            get => _customTime;
            set { _customTime = value; OnPropertyChanged(); OnPropertyChanged(nameof(TimeDisplay)); }
        }

        public int DefaultTime
        {
            get => _defaultTime;
            set { _defaultTime = value; OnPropertyChanged(); OnPropertyChanged(nameof(TimeDisplay)); }
        }

        public string TimeDisplay =>
            CustomTime > 0 ? $"{CustomTime} sec" : $"Default ({_defaultTime} sec)";

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
