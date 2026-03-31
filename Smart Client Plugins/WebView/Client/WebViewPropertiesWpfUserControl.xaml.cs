using VideoOS.Platform.Client;

namespace WebView.Client
{
    public partial class WebViewPropertiesWpfUserControl : PropertiesWpfUserControl
    {
        private readonly WebViewViewItemManager _viewItemManager;

        public WebViewPropertiesWpfUserControl(WebViewViewItemManager viewItemManager)
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
