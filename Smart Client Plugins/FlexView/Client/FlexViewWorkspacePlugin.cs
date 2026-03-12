using System;
using System.Drawing;
using VideoOS.Platform.Client;

namespace FlexView.Client
{
    public class FlexViewWorkspacePlugin : WorkSpacePlugin
    {
        public override Guid Id => FlexViewDefinition.WorkspacePluginId;
        public override string Name => "FlexView";

        public override void Init()
        {
            try
            {
                FlexViewDefinition.Log.Info($"WorkspacePlugin Init - ViewAndLayoutItem is {(ViewAndLayoutItem == null ? "null" : "not null")}");

                // Set layout to a single full-size slot so no stale camera slots remain
                ViewAndLayoutItem.Layout = new Rectangle[]
                {
                    new Rectangle(0, 0, 1000, 1000)
                };

                ViewAndLayoutItem.InsertViewItemPlugin(0, new FlexViewViewItemPlugin(), null);
                FlexViewDefinition.Log.Info("WorkspacePlugin InsertViewItemPlugin succeeded");
            }
            catch (Exception ex)
            {
                FlexViewDefinition.Log.Error($"WorkspacePlugin Init failed: {ex}");
            }
        }

        public override void Close()
        {
        }
    }
}
