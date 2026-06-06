using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows;
using SystemStatus.Background;
using VideoOS.Platform.Client;

namespace SystemStatus.Client
{
    /// <summary>
    /// Renders the two live lists - camera folders (online/total devices) and roles (logged-in/total
    /// users) - from the SystemStatus background plugin, refreshing whenever its snapshot changes.
    /// </summary>
    public partial class CameraUserStatusViewItemWpfUserControl : ViewItemWpfUserControl
    {
        private readonly ObservableCollection<FolderStatusRow> _folders = new ObservableCollection<FolderStatusRow>();
        private readonly ObservableCollection<RoleStatusRow> _roles = new ObservableCollection<RoleStatusRow>();
        private readonly CameraUserStatusViewItemManager _manager;
        private SystemStatusBackgroundPlugin _plugin;

        public CameraUserStatusViewItemWpfUserControl(CameraUserStatusViewItemManager manager)
        {
            _manager = manager;
            InitializeComponent();
            folderList.ItemsSource = _folders;
            roleList.ItemsSource = _roles;
        }

        public override void Init()
        {
            _plugin = SystemStatusBackgroundPlugin.Instance;
            if (_plugin != null) _plugin.StatusChanged += OnStatusChanged;
            Refresh();
        }

        public override void Close()
        {
            if (_plugin != null) _plugin.StatusChanged -= OnStatusChanged;
            _plugin = null;
        }

        private void OnStatusChanged(object sender, StatusChangedEventArgs e)
        {
            // Raised on a background thread - marshal to the UI thread before touching the collections.
            Dispatcher.BeginInvoke(new Action(Refresh));
        }

        private void Refresh()
        {
            var plugin = _plugin ?? SystemStatusBackgroundPlugin.Instance;
            if (plugin == null) return;

            bool includePrefix =
                !string.Equals(_manager?.ShowServerPrefix, "false", StringComparison.OrdinalIgnoreCase);
            ReplaceList(_folders, plugin.GetFolderCameraStatus(includePrefix));
            ReplaceList(_roles, plugin.GetRoleUserStatus());

            bool empty = _folders.Count == 0 && _roles.Count == 0;
            emptyLabel.Text = "No data.";
            emptyLabel.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        }

        private static void ReplaceList<T>(ObservableCollection<T> target, IReadOnlyList<T> source)
        {
            target.Clear();
            if (source == null) return;
            foreach (var x in source) target.Add(x);
        }

        public override bool Maximizable => true;
        public override bool Selectable => true;
        public override bool ShowToolbar => false;
    }
}
