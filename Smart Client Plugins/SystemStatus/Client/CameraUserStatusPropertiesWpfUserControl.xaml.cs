using System;
using System.Windows;
using System.Windows.Interop;
using VideoOS.Platform.Client;

namespace SystemStatus.Client
{
    /// <summary>
    /// Setup-mode properties for the Folder &amp; Role view item: a read-only summary plus a button that
    /// opens the split configuration window (settings + live preview). All editing happens there.
    /// </summary>
    public partial class CameraUserStatusPropertiesWpfUserControl : PropertiesWpfUserControl
    {
        private readonly CameraUserStatusViewItemManager _manager;

        public CameraUserStatusPropertiesWpfUserControl(CameraUserStatusViewItemManager manager)
        {
            _manager = manager;
            InitializeComponent();
        }

        public override void Init()
        {
            RefreshSummary();
        }

        public override void Close()
        {
            // Read-only pane; all edits are committed by the configuration window.
        }

        private void RefreshSummary()
        {
            var s = CameraUserStatusSettings.FromManager(_manager);

            summarySelection.Text = s.IndividualSelection
                ? $"{s.SelectedFolders.Count} folders, {s.SelectedRoles.Count} roles"
                : "All";

            string sections =
                s.ShowFolders && s.ShowRoles ? (s.FoldersFirst ? "Folders + Roles" : "Roles + Folders") :
                s.ShowFolders ? "Folders only" :
                s.ShowRoles ? "Roles only" : "(none)";
            summarySections.Text = sections;

            summaryPrefix.Text = s.ShowServerPrefix ? "On" : "Off";
            summaryTextSize.Text = ((int)s.TextSize).ToString();
        }

        private void OnOpenConfigClick(object sender, RoutedEventArgs e)
        {
            var win = new CameraUserStatusConfigurationWindow(_manager);
            try
            {
                var owner = Window.GetWindow(this);
                if (owner != null) win.Owner = owner;
                else new WindowInteropHelper(win).Owner = NativeOwner();
            }
            catch { }

            var result = win.ShowDialog();
            if (result == true)
            {
                _manager.Save();
                RefreshSummary();
            }
        }

        private static IntPtr NativeOwner()
        {
            try { return System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle; }
            catch { return IntPtr.Zero; }
        }
    }
}
