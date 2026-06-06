using System;
using VideoOS.Platform.Client;

namespace SystemStatus.Client
{
    /// <summary>Setup-mode properties for the Folder &amp; Role view item (server-prefix toggle).</summary>
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
            showPrefixCheckBox.IsChecked =
                !string.Equals(_manager.ShowServerPrefix, "false", StringComparison.OrdinalIgnoreCase);
        }

        public override void Close()
        {
            _manager.ShowServerPrefix = showPrefixCheckBox.IsChecked == true ? "true" : "false";
            _manager.Save();
        }
    }
}
