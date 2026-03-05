using System;
using System.Windows.Controls;
using VideoOS.Platform.Client;

namespace MonitorRTMPStreamer.Client
{
    public class StreamerSettingsPanel : SettingsPanelPlugin
    {
        private StreamerSettingsPanelControl _control;

        public override Guid Id => new Guid("DB052E27-E683-4410-B6A2-BCEF835D2EF3");

        public override string Title => "Monitor RTMP Streamer";

        public override void Init() { }

        public override void Close() { }

        public override UserControl GenerateUserControl()
        {
            _control = new StreamerSettingsPanelControl();
            return _control;
        }

        public override void CloseUserControl()
        {
            _control?.StopTimer();
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
