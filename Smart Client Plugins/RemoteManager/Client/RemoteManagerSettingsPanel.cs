using System;
using System.Windows.Controls;
using VideoOS.Platform.Client;

namespace RemoteManager.Client
{
    class RemoteManagerSettingsPanel : SettingsPanelPlugin
    {
        private RemoteManagerSettingsPanelControl _control;

        public override Guid Id => new Guid("3F2A6B1E-4C5D-4E7A-9B22-1A8E5D6C0F11");

        public override string Title => "Remote Manager";

        public override UserControl GenerateUserControl()
        {
            _control = new RemoteManagerSettingsPanelControl();
            return _control;
        }

        public override bool TrySaveChanges(out string errorMessage)
        {
            errorMessage = null;
            _control?.Save();
            return true;
        }

        public override void CloseUserControl()
        {
            _control = null;
        }

        public override void Init() { }
        public override void Close() { }
    }
}
