using System;
using System.Collections.Generic;
using VideoOS.Platform;
using VideoOS.Platform.Client;

namespace CertWatchdog.Client
{
    public class CertWatchdogWorkspacePlugin : WorkSpacePlugin
    {
        public override Guid Id => CertWatchdogDefinition.WorkspacePluginId;
        public override string Name => "Certificates";

        public override void Init()
        {
            ViewAndLayoutItem.InsertViewItemPlugin(0, new CertWatchdogViewItemPlugin(), null);
        }

        public override void Close()
        {
        }
    }
}
