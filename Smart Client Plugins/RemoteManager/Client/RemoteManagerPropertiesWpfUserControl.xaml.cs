using VideoOS.Platform.Client;

namespace RemoteManager.Client
{
    public partial class RemoteManagerPropertiesWpfUserControl : PropertiesWpfUserControl
    {
        private readonly RemoteManagerViewItemManager _viewItemManager;

        public RemoteManagerPropertiesWpfUserControl(RemoteManagerViewItemManager viewItemManager)
        {
            _viewItemManager = viewItemManager;
            InitializeComponent();
        }

        public override void Init()
        {
            autoAcceptCertsCheckBox.IsChecked = _viewItemManager.AutoAcceptCerts;
        }

        public override void Close()
        {
            _viewItemManager.AutoAcceptCerts = autoAcceptCertsCheckBox.IsChecked == true;
            _viewItemManager.Save();
        }
    }
}
