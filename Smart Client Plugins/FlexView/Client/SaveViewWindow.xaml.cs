using System.Windows;
using VideoOS.Platform;

namespace FlexView.Client
{
    public partial class SaveViewWindow : Window
    {
        public string ViewName => nameBox.Text?.Trim();
        public Item SelectedFolder { get; private set; }

        public SaveViewWindow()
        {
            InitializeComponent();
        }

        public SaveViewWindow(string defaultName, Item defaultFolder) : this()
        {
            if (!string.IsNullOrEmpty(defaultName))
                nameBox.Text = defaultName;
            if (defaultFolder != null)
            {
                SelectedFolder = defaultFolder;
                folderLabel.Text = defaultFolder.Name;
            }
        }

        private void OnBrowseFolder(object sender, RoutedEventArgs e)
        {
            var browser = new ViewBrowserWindow(BrowseMode.SelectFolder);
            browser.Owner = this;
            if (browser.ShowDialog() == true && browser.SelectedItem != null)
            {
                SelectedFolder = browser.SelectedItem;
                folderLabel.Text = browser.SelectedItem.Name;
            }
        }

        private void OnSaveClick(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(ViewName))
            {
                MessageBox.Show("Please enter a view name.", "FlexView",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (SelectedFolder == null)
            {
                MessageBox.Show("Please select a destination folder.", "FlexView",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            DialogResult = true;
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
