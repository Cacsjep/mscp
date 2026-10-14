using System;
using System.Windows.Controls;
using VideoOS.Platform.Client;

namespace LowLatencyVideoWall.Client
{
    /// <summary>
    /// The plugin's page in the Smart Client's own Settings dialog. Everything it offers is a
    /// display choice, so it lives here rather than in the Management Client: two operators
    /// looking at the same wall view on two machines may legitimately want different chrome.
    /// </summary>
    internal sealed class WallSettingsPanel : SettingsPanelPlugin
    {
        private WallSettingsPanelControl _control;

        public override Guid Id => new Guid("9C2F0A47-2E6B-4F58-9D11-7A4B6E3C5D82");

        public override string Title => "Low Latency Video Wall";

        public override UserControl GenerateUserControl()
        {
            _control = new WallSettingsPanelControl();
            return _control;
        }

        public override bool TrySaveChanges(out string errorMessage)
        {
            errorMessage = null;
            _control?.Save();
            return true;
        }

        public override void CloseUserControl() => _control = null;

        public override void Init() { }

        public override void Close() { }
    }
}
