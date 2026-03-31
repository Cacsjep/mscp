using System;
using VideoOS.Platform.Client;

namespace Timelapse.Client
{
    public class TimelapseWorkspacePlugin : WorkSpacePlugin
    {
        public override Guid Id => TimelapseDefinition.WorkspacePluginId;
        public override string Name => "Timelapse";

        public override void Init()
        {
            ViewAndLayoutItem.InsertViewItemPlugin(0, new TimelapseViewItemPlugin(), null);
        }

        public override void Close()
        {
        }
    }
}
