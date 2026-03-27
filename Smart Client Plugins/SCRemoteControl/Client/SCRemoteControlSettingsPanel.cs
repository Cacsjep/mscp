using System;
using System.Windows.Controls;
using VideoOS.Platform.Client;

namespace SCRemoteControl.Client
{
    class SCRemoteControlSettingsPanel : SettingsPanelPlugin
    {
        private SCRemoteControlSettingsPanelControl _control;

        public override Guid Id => new Guid("B205587D-3940-4B1C-AD56-292F6B1C3B5A");

        public override string Title => "Remote Control";

        public override UserControl GenerateUserControl()
        {
            _control = new SCRemoteControlSettingsPanelControl();
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
            _control?.Cleanup();
            _control = null;
        }

        public override void Init() { }
        public override void Close() { }
    }
}
