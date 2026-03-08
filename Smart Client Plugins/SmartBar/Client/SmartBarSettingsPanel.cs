using System;
using System.Windows.Controls;
using VideoOS.Platform.Client;

namespace SmartBar.Client
{
    class SmartBarSettingsPanel : SettingsPanelPlugin
    {
        private SmartBarSettingsPanelControl _control;

        public override Guid Id => new Guid("A7B8C9D0-E1F2-3456-7890-ABCDEF123460");

        public override string Title => "Smart Bar";

        public override UserControl GenerateUserControl()
        {
            _control = new SmartBarSettingsPanelControl();
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
