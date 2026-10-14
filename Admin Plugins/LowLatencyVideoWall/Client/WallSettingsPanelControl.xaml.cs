using System.Windows.Controls;
using LowLatencyVideoWall.Config;

namespace LowLatencyVideoWall.Client
{
    public partial class WallSettingsPanelControl : UserControl
    {
        public WallSettingsPanelControl()
        {
            InitializeComponent();

            WallClientSettings.Load();

            titleBarCheck.IsChecked = WallClientSettings.ShowTitleBar;
            viewSelectionCheck.IsChecked = WallClientSettings.ShowViewSelection;
            diagnosticsCheck.IsChecked = WallClientSettings.ShowDiagnostics;
            aspectRadio.IsChecked = WallClientSettings.Scaling == PaneScaling.AspectRatio;
            fillRadio.IsChecked = WallClientSettings.Scaling == PaneScaling.Fill;
        }

        /// <summary>
        /// Called by the Smart Client when the operator leaves the settings dialog. Saving
        /// raises WallClientSettings.Changed, which is how an open wall picks the new values
        /// up without being reloaded.
        /// </summary>
        internal void Save()
        {
            WallClientSettings.ShowTitleBar = titleBarCheck.IsChecked == true;
            WallClientSettings.ShowViewSelection = viewSelectionCheck.IsChecked == true;
            WallClientSettings.ShowDiagnostics = diagnosticsCheck.IsChecked == true;
            WallClientSettings.Scaling = fillRadio.IsChecked == true
                ? PaneScaling.Fill
                : PaneScaling.AspectRatio;

            WallClientSettings.Save();
        }
    }
}
