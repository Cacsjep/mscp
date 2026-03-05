using System;
using System.Windows.Controls;
using VideoOS.Platform.Client;

namespace Recorder.Client
{
    public class RecorderSettingsPanel : SettingsPanelPlugin
    {
        private UserControl _userControl;

        public override Guid Id => new Guid("DB052E27-E683-4410-B6A2-BCEF835D2EF3");

        public override string Title => "Recorder";

        public override void Init()
        {
            Console.WriteLine("");
        }

        public override void Close()
        {
        }

        public override UserControl GenerateUserControl()
        {
            _userControl = new RecorderSettingsPanelControl();
            return _userControl;
        }

        public override void CloseUserControl()
        {
            _userControl = null;
        }

        public override bool TrySaveChanges(out string errorMessage)
        {
            errorMessage = string.Empty;
            return true;
        }
    }
}
