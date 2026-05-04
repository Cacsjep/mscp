using System;
using VideoOS.Platform.Client;
using VideoOS.Platform.UI.Controls;

namespace WebViewer.Client
{
    public class WebViewerViewItemPlugin : ViewItemPlugin
    {
        public override Guid Id => new Guid("7B2C3D4E-5F60-7891-BCDE-F01234567890");

        public override string Name => "Web Viewer";

        public override VideoOSIconSourceBase IconSource
        {
            get => WebViewerDefinition.PluginIconSource;
            protected set => base.IconSource = value;
        }

        public override ViewItemManager GenerateViewItemManager()
            => new WebViewerViewItemManager();

        public override void Init() { }
        public override void Close() { }
    }
}
