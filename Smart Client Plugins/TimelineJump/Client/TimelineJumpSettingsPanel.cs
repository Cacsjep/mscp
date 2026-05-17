using System;
using System.Windows.Controls;
using VideoOS.Platform.Client;

namespace TimelineJump.Client
{
    class TimelineJumpSettingsPanel : SettingsPanelPlugin
    {
        private TimelineJumpSettingsPanelControl _control;

        public override Guid Id => new Guid("D4F8A726-3B91-4C5E-A0F8-7B2D9E4C1A37");

        public override string Title => "Timeline Jump";

        public override UserControl GenerateUserControl()
        {
            _control = new TimelineJumpSettingsPanelControl();
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
