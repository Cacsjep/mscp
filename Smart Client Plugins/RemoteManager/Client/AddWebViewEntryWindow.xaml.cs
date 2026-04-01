using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using RemoteManager.Models;

namespace RemoteManager.Client
{
    public partial class AddWebViewEntryWindow : Window
    {
        public string EntryName { get; private set; }
        public string EntryUrl { get; private set; }
        public string EntryUsername { get; private set; }
        public string EntryPassword { get; private set; }
        public List<string> EntryTags { get; private set; } = new List<string>();

        private readonly HardwareDeviceInfo _editEntry;
        private readonly HashSet<string> _tags = new HashSet<string>();

        public AddWebViewEntryWindow(HardwareDeviceInfo editEntry = null, List<string> existingTags = null)
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
                Title = "Edit Web View";
                SetOkButtonContent("Save", FontAwesome5.EFontAwesomeIcon.Solid_Save);
                nameBox.Text = _editEntry.Name ?? "";
                urlBox.Text = _editEntry.Address ?? "https://";
                userBox.Text = _editEntry.Username ?? "";
                if (!string.IsNullOrEmpty(_editEntry.Password))
                    passBox.Password = _editEntry.Password;
            }

            RebuildTagPanel();
            nameBox.Focus();
        }

        private void OnOk(object sender, RoutedEventArgs e)
        {
            errorText.Visibility = Visibility.Collapsed;

            var name = nameBox.Text?.Trim();
            var url = urlBox.Text?.Trim();

            if (string.IsNullOrEmpty(name))
            {
                ShowError("Name is required.");
                nameBox.Focus();
                return;
            }

            if (string.IsNullOrEmpty(url) || url == "https://" || url == "http://")
            {
                ShowError("A valid URL is required.");
                urlBox.Focus();
                return;
            }

            if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed) ||
                (parsed.Scheme != "http" && parsed.Scheme != "https"))
            {
                ShowError("URL must start with http:// or https://");
                urlBox.Focus();
                return;
            }

            EntryName = name;
            EntryUrl = url;
            EntryUsername = string.IsNullOrWhiteSpace(userBox.Text) ? null : userBox.Text.Trim();
            EntryPassword = passBox.Password?.Length > 0 ? passBox.Password : null;
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
