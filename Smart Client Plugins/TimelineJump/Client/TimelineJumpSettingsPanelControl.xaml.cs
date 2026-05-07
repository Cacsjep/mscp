using System.Windows.Controls;

namespace TimelineJump.Client
{
    public partial class TimelineJumpSettingsPanelControl : UserControl
    {
        public TimelineJumpSettingsPanelControl()
        {
            InitializeComponent();
            JumpToCurrentCheck.IsChecked = TimelineJumpConfig.JumpToCurrentOnPlayback;
        }

        public void Save()
        {
            TimelineJumpConfig.JumpToCurrentOnPlayback = JumpToCurrentCheck.IsChecked == true;
            TimelineJumpConfig.Save();
        }
    }
}
