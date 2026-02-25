using VideoOS.Platform.Client;

namespace RDP.Client
{
    public partial class RDPPropertiesWpfUserControl : PropertiesWpfUserControl
    {
        private readonly RDPViewItemManager _viewItemManager;

        public RDPPropertiesWpfUserControl(RDPViewItemManager viewItemManager)
        {
            _viewItemManager = viewItemManager;
            InitializeComponent();
        }

        public override void Init()
        {
            nameTextBox.Text = _viewItemManager.ConnectionName;
            ipTextBox.Text = _viewItemManager.IPAddress;
            usernameTextBox.Text = _viewItemManager.Username;
            enableNlaCheckBox.IsChecked = _viewItemManager.EnableNLA;
            enableClipboardCheckBox.IsChecked = _viewItemManager.EnableClipboard;
        }

        public override void Close()
        {
            _viewItemManager.ConnectionName = nameTextBox.Text.Trim();
            _viewItemManager.IPAddress = ipTextBox.Text.Trim();
            _viewItemManager.Username = usernameTextBox.Text.Trim();
            _viewItemManager.EnableNLA = enableNlaCheckBox.IsChecked == true;
            _viewItemManager.EnableClipboard = enableClipboardCheckBox.IsChecked == true;
            _viewItemManager.Save();
        }
    }
}
