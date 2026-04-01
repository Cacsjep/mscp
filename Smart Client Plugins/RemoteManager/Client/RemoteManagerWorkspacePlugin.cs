using System;
using VideoOS.Platform.Client;

namespace RemoteManager.Client
{
    public class RemoteManagerWorkspacePlugin : WorkSpacePlugin
    {
        public override Guid Id => RemoteManagerDefinition.WorkspacePluginId;
        public override string Name => "Remote Manager";

        public override void Init()
        {
            ViewAndLayoutItem.InsertViewItemPlugin(0, new RemoteManagerViewItemPlugin(), null);
        }

        public override void Close()
        {
        }
    }
}
