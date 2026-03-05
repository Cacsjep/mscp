using System;
using System.Windows.Controls;
using VideoOS.Platform.Client;

namespace Recorder.Client
{
    public class RecorderSettingsPanel : SettingsPanelPlugin
    {
        private RecorderSettingsPanelControl _control;

        public override Guid Id => new Guid("DB052E27-E683-4410-B6A2-BCEF835D2EF3");

        public override string Title => "Recorder";

        public override void Init() { }

        public override void Close() { }

        public override UserControl GenerateUserControl()
        {
            _control = new RecorderSettingsPanelControl();
            return _control;
        }

        public override void CloseUserControl()
        {
            _control = null;
        }

        public override bool TrySaveChanges(out string errorMessage)
        {
            if (_control != null)
                return _control.Save(out errorMessage);

            errorMessage = string.Empty;
            return true;
        }
    }
}
