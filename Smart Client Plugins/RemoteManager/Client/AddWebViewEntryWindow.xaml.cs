using System;
using System.Windows;
using System.Windows.Controls;
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

        private readonly HardwareDeviceInfo _editEntry;

        public AddWebViewEntryWindow(HardwareDeviceInfo editEntry = null)
        {
            _editEntry = editEntry;
            InitializeComponent();

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
    }
}
