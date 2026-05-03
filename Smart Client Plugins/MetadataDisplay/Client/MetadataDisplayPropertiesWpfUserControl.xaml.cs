using System;
using System.Windows;
using System.Windows.Interop;
using CommunitySDK;
using VideoOS.Platform.Client;

namespace MetadataDisplay.Client
{
    public partial class MetadataDisplayPropertiesWpfUserControl : PropertiesWpfUserControl
    {
        private static readonly PluginLog _log = new PluginLog("MetadataDisplay");
        private readonly MetadataDisplayViewItemManager _viewItemManager;

        public MetadataDisplayPropertiesWpfUserControl(MetadataDisplayViewItemManager viewItemManager)
        {
            _viewItemManager = viewItemManager;
            InitializeComponent();
        }

        public override void Init()
        {
            RefreshSummary();
        }

        public override void Close()
        {
            // Outer pane is read-only; all edits happen in the configuration window.
        }

        private void RefreshSummary()
        {
            summaryChannel.Text = string.IsNullOrEmpty(_viewItemManager.MetadataName)
                ? "(not set)"
                : _viewItemManager.MetadataName;
            summaryTopic.Text = string.IsNullOrEmpty(_viewItemManager.Topic)
                ? "(not set)"
                : _viewItemManager.Topic;
            summaryKey.Text = string.IsNullOrEmpty(_viewItemManager.DataKey)
                ? "(not set)"
                : _viewItemManager.DataKey;
            summaryFilter.Text = string.IsNullOrEmpty(_viewItemManager.SourceFilters)
                ? "(none)"
                : _viewItemManager.SourceFilters;
            summaryRender.Text = string.IsNullOrEmpty(_viewItemManager.RenderType)
                ? "Lamp"
                : _viewItemManager.RenderType;
        }

        private void OnOpenConfigClick(object sender, RoutedEventArgs e)
        {
            _log.Info("[Properties] Open configuration clicked");
            var win = new MetadataDisplayConfigurationWindow(_viewItemManager);
            try
            {
                var owner = Window.GetWindow(this);
                if (owner != null)
                    win.Owner = owner;
                else
                    new WindowInteropHelper(win).Owner = NativeOwner();
            }
            catch { }

            var result = win.ShowDialog();
            _log.Info($"[Properties] Configuration window closed result={result}");
            if (result == true)
            {
                _viewItemManager.Save();
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
