using System;
using System.Windows;

namespace WebView.Client
{
    public partial class AddUserEntryWindow : Window
    {
        public string EntryName { get; private set; }
        public string EntryUrl { get; private set; }
        public string EntryUsername { get; private set; }
        public string EntryPassword { get; private set; }

        public AddUserEntryWindow()
        {
            InitializeComponent();
            nameBox.Focus();
        }

        private void OnAdd(object sender, RoutedEventArgs e)
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
    }
}
