using System.Windows.Input;
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
            portTextBox.Text = _viewItemManager.Port.ToString();
            usernameTextBox.Text = _viewItemManager.Username;
            enableNlaCheckBox.IsChecked = _viewItemManager.EnableNLA;
            enableClipboardCheckBox.IsChecked = _viewItemManager.EnableClipboard;

            portTextBox.PreviewTextInput += (s, e) =>
            {
                e.Handled = !int.TryParse(e.Text, out _);
            };
        }

        public override void Close()
        {
            _viewItemManager.ConnectionName = nameTextBox.Text.Trim();
            _viewItemManager.IPAddress = ipTextBox.Text.Trim();
            _viewItemManager.Username = usernameTextBox.Text.Trim();
            _viewItemManager.EnableNLA = enableNlaCheckBox.IsChecked == true;
            _viewItemManager.EnableClipboard = enableClipboardCheckBox.IsChecked == true;

            if (int.TryParse(portTextBox.Text.Trim(), out var port) && port >= 1 && port <= 65535)
                _viewItemManager.Port = port;
            else
                _viewItemManager.Port = 3389;

            _viewItemManager.Save();
        }
    }
}
