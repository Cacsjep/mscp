using System;
using VideoOS.Platform.Client;

namespace WebView.Client
{
    public class WebViewWorkspacePlugin : WorkSpacePlugin
    {
        public override Guid Id => WebViewDefinition.WorkspacePluginId;
        public override string Name => "Web View";

        public override void Init()
        {
            ViewAndLayoutItem.InsertViewItemPlugin(0, new WebViewViewItemPlugin(), null);
        }

        public override void Close()
        {
        }
    }
}
