using System;
using VideoOS.Platform.Client;

namespace SnapReport.Client
{
    public class SnapReportWorkspacePlugin : WorkSpacePlugin
    {
        public override Guid Id => SnapReportDefinition.WorkspacePluginId;
        public override string Name => "Snap Report";

        public override void Init()
        {
            ViewAndLayoutItem.InsertViewItemPlugin(0, new SnapReportViewItemPlugin(), null);
        }

        public override void Close()
        {
        }
    }
}
