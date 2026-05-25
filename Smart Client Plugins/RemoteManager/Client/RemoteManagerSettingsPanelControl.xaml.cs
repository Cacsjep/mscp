using System.Windows.Controls;

namespace RemoteManager.Client
{
    public partial class RemoteManagerSettingsPanelControl : UserControl
    {
        public RemoteManagerSettingsPanelControl()
        {
            InitializeComponent();

            RemoteManagerConfig.Load();
            HideCredentialBarCheck.IsChecked = RemoteManagerConfig.HideCredentialBar;
        }

        public void Save()
        {
            RemoteManagerConfig.HideCredentialBar = HideCredentialBarCheck.IsChecked == true;
            RemoteManagerConfig.Save();
        }
    }
}
