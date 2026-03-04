using VideoOS.Platform.Client;

namespace SnapReport.Client
{
    public class SnapReportViewItemManager : ViewItemManager
    {
        public SnapReportViewItemManager()
            : base("SnapReportViewItemManager")
        {
        }

        public override void PropertiesLoaded()
        {
        }

        public override ViewItemWpfUserControl GenerateViewItemWpfUserControl()
        {
            return new SnapReportViewItemWpfUserControl();
        }
    }
}
