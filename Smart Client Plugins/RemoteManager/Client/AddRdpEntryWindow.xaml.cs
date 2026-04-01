using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using RemoteManager.Models;

namespace RemoteManager.Client
{
    public partial class AddRdpEntryWindow : Window
    {
        public string EntryName { get; private set; }
        public string EntryHost { get; private set; }
        public int EntryPort { get; private set; } = 3389;
        public string EntryUsername { get; private set; }
        public string EntryPassword { get; private set; }
        public bool EntryEnableNLA { get; private set; }
        public bool EntryEnableClipboard { get; private set; } = true;
        public List<string> EntryTags { get; private set; } = new List<string>();

        private readonly RdpConnectionInfo _editEntry;
        private readonly HashSet<string> _tags = new HashSet<string>();

        public AddRdpEntryWindow(RdpConnectionInfo editEntry = null, List<string> existingTags = null)
        {
            _editEntry = editEntry;
            InitializeComponent();

            if (existingTags != null)
            {
                foreach (var t in existingTags)
                    _tags.Add(t);
            }

            if (_editEntry != null)
            {
                Title = "Edit RDP Connection";
                SetOkButtonContent("Save", FontAwesome5.EFontAwesomeIcon.Solid_Save);
                nameBox.Text = _editEntry.Name ?? "";
                hostBox.Text = _editEntry.Host ?? "";
                portBox.Text = _editEntry.Port.ToString();
                userBox.Text = _editEntry.Username ?? "";
                if (!string.IsNullOrEmpty(_editEntry.Password))
                    passBox.Password = _editEntry.Password;
                nlaCheckBox.IsChecked = _editEntry.EnableNLA;
                clipboardCheckBox.IsChecked = _editEntry.EnableClipboard;
            }

            RebuildTagPanel();
            nameBox.Focus();
        }

        private void OnOk(object sender, RoutedEventArgs e)
        {
            errorText.Visibility = Visibility.Collapsed;

            var name = nameBox.Text?.Trim();
            var host = hostBox.Text?.Trim();

            if (string.IsNullOrEmpty(name))
            {
                ShowError("Name is required.");
                nameBox.Focus();
                return;
            }

            if (string.IsNullOrEmpty(host))
            {
                ShowError("Host / IP is required.");
                hostBox.Focus();
                return;
            }

            int port = 3389;
            var portText = portBox.Text?.Trim();
            if (!string.IsNullOrEmpty(portText))
            {
                if (!int.TryParse(portText, out port) || port < 1 || port > 65535)
                {
                    ShowError("Port must be between 1 and 65535.");
                    portBox.Focus();
                    return;
                }
            }

            EntryName = name;
            EntryHost = host;
            EntryPort = port;
            EntryUsername = string.IsNullOrWhiteSpace(userBox.Text) ? null : userBox.Text.Trim();
            EntryPassword = passBox.Password?.Length > 0 ? passBox.Password : null;
            EntryEnableNLA = nlaCheckBox.IsChecked == true;
            EntryEnableClipboard = clipboardCheckBox.IsChecked == true;
            EntryTags = _tags.ToList();
            DialogResult = true;
        }

        private void ShowError(string message)
        {
            errorText.Text = message;
            errorText.Visibility = Visibility.Visible;
        }

        private void OnCancel(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private void OnPortPreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !Regex.IsMatch(e.Text, @"^\d$");
        }

        private void SetOkButtonContent(string text, FontAwesome5.EFontAwesomeIcon icon)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            panel.Children.Add(new FontAwesome5.ImageAwesome
            {
                Icon = icon,
                Foreground = new SolidColorBrush(
                    (Color)ColorConverter.ConvertFromString("#FF2196F3")),
                Width = 10, Height = 10,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0)
            });
            panel.Children.Add(new TextBlock
            {
                Text = text,
                VerticalAlignment = VerticalAlignment.Center
            });
            okButton.Content = panel;
        }

        #region Tag Management

        private void OnAddTag(object sender, RoutedEventArgs e)
        {
            AddTagFromInput();
        }

        private void OnTagKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
                AddTagFromInput();
        }

        private void AddTagFromInput()
        {
            var tag = newTagBox.Text?.Trim();
            if (!string.IsNullOrEmpty(tag) && _tags.Add(tag))
            {
                newTagBox.Text = "";
                RebuildTagPanel();
            }
        }

        private void RebuildTagPanel()
        {
            tagPanel.Children.Clear();
            foreach (var tag in _tags.OrderBy(t => t))
            {
                var chip = CreateRemovableTagChip(tag);
                tagPanel.Children.Add(chip);
            }
        }

        private Border CreateRemovableTagChip(string tag)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal };

            panel.Children.Add(new FontAwesome5.ImageAwesome
            {
                Icon = FontAwesome5.EFontAwesomeIcon.Solid_Check,
                Width = 8,
                Height = 8,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF1C2326")),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 5, 0),
            });

            panel.Children.Add(new TextBlock
            {
                Text = tag,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF1C2326")),
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
            });

            var border = new Border
            {
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFC107")),
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFC107")),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(10, 5, 10, 5),
                Margin = new Thickness(0, 0, 6, 6),
                Cursor = Cursors.Hand,
                Child = panel,
            };

            border.MouseLeftButtonDown += (s, e) =>
            {
                _tags.Remove(tag);
                RebuildTagPanel();
            };

            return border;
        }

        #endregion
    }
}
