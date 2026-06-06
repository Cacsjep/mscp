using System;
using System.Windows;
using System.Windows.Input;
using SystemStatus.Background;
using VideoOS.Platform;
using VideoOS.Platform.Client;
using VideoOS.Platform.Messaging;

namespace SystemStatus.Client
{
    /// <summary>
    /// The Folder &amp; Role tile. In live/playback it renders the two status lists from the SystemStatus
    /// background plugin; in setup mode it shows a centered card with a summary + "Open configuration..."
    /// button (mirrors the MetadataDisplay widget) so the operator configures it in place.
    /// </summary>
    public partial class CameraUserStatusViewItemWpfUserControl : ViewItemWpfUserControl
    {
        private readonly CameraUserStatusViewItemManager _manager;
        private SystemStatusBackgroundPlugin _plugin;
        private object _modeChangedReceiver;
        private bool _subscribed;

        public CameraUserStatusViewItemWpfUserControl(CameraUserStatusViewItemManager manager)
        {
            _manager = manager;
            InitializeComponent();
        }

        public override void Init()
        {
            _plugin = SystemStatusBackgroundPlugin.Instance;
            _modeChangedReceiver = EnvironmentManager.Instance.RegisterReceiver(
                new MessageReceiver(OnModeChanged),
                new MessageIdFilter(MessageId.System.ModeChangedIndication));
            ApplyMode(EnvironmentManager.Instance.Mode);
        }

        public override void Close()
        {
            if (_modeChangedReceiver != null)
            {
                EnvironmentManager.Instance.UnRegisterReceiver(_modeChangedReceiver);
                _modeChangedReceiver = null;
            }
            Unsubscribe();
            _plugin = null;
        }

        private object OnModeChanged(Message message, FQID destination, FQID sender)
        {
            Dispatcher.BeginInvoke(new Action(() => ApplyMode((Mode)message.Data)));
            return null;
        }

        private void ApplyMode(Mode mode)
        {
            if (mode == Mode.ClientSetup)
            {
                Unsubscribe();
                ApplySetupSummary();
                setupPanel.Visibility = Visibility.Visible;
                listView.Visibility = Visibility.Collapsed;
            }
            else
            {
                setupPanel.Visibility = Visibility.Collapsed;
                listView.Visibility = Visibility.Visible;
                Subscribe();
                Refresh();
            }
        }

        private void Subscribe()
        {
            if (_subscribed) return;
            _plugin = _plugin ?? SystemStatusBackgroundPlugin.Instance;
            if (_plugin != null) { _plugin.StatusChanged += OnStatusChanged; _subscribed = true; }
        }

        private void Unsubscribe()
        {
            if (_subscribed && _plugin != null) _plugin.StatusChanged -= OnStatusChanged;
            _subscribed = false;
        }

        private void OnStatusChanged(object sender, StatusChangedEventArgs e)
        {
            // Raised on a background thread - marshal to the UI thread before touching the visuals.
            Dispatcher.BeginInvoke(new Action(Refresh));
        }

        private void Refresh()
        {
            var plugin = _plugin ?? SystemStatusBackgroundPlugin.Instance;
            if (plugin == null) { listView.ShowMessage("No data."); return; }

            var settings = CameraUserStatusSettings.FromManager(_manager);
            listView.Render(settings, settings.BuildFolders(plugin), settings.BuildRoles(plugin));
        }

        private void ApplySetupSummary()
        {
            var s = CameraUserStatusSettings.FromManager(_manager);
            summarySelection.Text = s.IndividualSelection
                ? $"{s.SelectedFolders.Count} folders, {s.SelectedRoles.Count} roles"
                : "All folders and roles";
            summarySections.Text =
                s.ShowFolders && s.ShowRoles ? (s.FoldersFirst ? "Folders + Roles" : "Roles + Folders") :
                s.ShowFolders ? "Cameras only" :
                s.ShowRoles ? "Roles only" : "(none)";
            summaryPrefix.Text = s.ShowServerPrefix ? "On" : "Off";
        }

        // Let a click anywhere on the tile select the view item (so the toolbar acts on it in setup).
        private void OnMouseLeftUp(object sender, MouseButtonEventArgs e) => FireClickEvent();

        private void OnOpenConfigClick(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            try
            {
                var win = new CameraUserStatusConfigurationWindow(_manager);
                var owner = Window.GetWindow(this);
                if (owner != null) win.Owner = owner;
                if (win.ShowDialog() == true)
                {
                    _manager.Save();
                    ApplySetupSummary();   // setup card stays visible; refresh its summary
                }
            }
            catch { }
        }

        public override bool Maximizable => true;
        public override bool Selectable => true;
        public override bool ShowToolbar => false;
    }
}
