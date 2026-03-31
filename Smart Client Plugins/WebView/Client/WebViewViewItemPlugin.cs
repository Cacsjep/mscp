using System;
using VideoOS.Platform.Client;
using VideoOS.Platform.UI.Controls;

namespace WebView.Client
{
    public class WebViewViewItemPlugin : ViewItemPlugin
    {
        public override Guid Id => WebViewDefinition.ViewItemPluginId;
        public override string Name => "WebView";
        public override bool HideSetupItem => true;

        public override VideoOSIconSourceBase IconSource
        {
            get => WebViewDefinition.PluginIconSource;
            protected set => base.IconSource = value;
        }

        public override ViewItemManager GenerateViewItemManager()
            => new WebViewViewItemManager();

        public override void Init() { }
        public override void Close() { }
    }
}
