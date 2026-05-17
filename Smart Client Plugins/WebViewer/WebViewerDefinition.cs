using System;
using System.Collections.Generic;
using CommunitySDK;
using FontAwesome5;
using VideoOS.Platform;
using VideoOS.Platform.Client;
using VideoOS.Platform.UI.Controls;

namespace WebViewer
{
    public class WebViewerDefinition : PluginDefinition
    {
        private static readonly PluginLog _log = new PluginLog("WebViewer");
        private static readonly VideoOSIconSourceBase _pluginIcon;

        internal static Guid WebViewerPluginId = new Guid("7A1B2C3D-4E5F-6789-ABCD-EF0123456789");

        static WebViewerDefinition()
        {
            try { _pluginIcon = PluginIcon.RenderIconSource(EFontAwesomeIcon.Solid_Globe); } catch { }
        }

        internal static VideoOSIconSourceBase PluginIconSource => _pluginIcon;

        public override Guid Id => WebViewerPluginId;
        public override string Name => "Web Viewer";
        public override string Manufacturer => "MSC Community Plugins";

        public override System.Drawing.Image Icon
            => VideoOS.Platform.UI.Util.ImageList.Images[VideoOS.Platform.UI.Util.PluginIx];

        private List<ViewItemPlugin> _viewItemPlugins = new List<ViewItemPlugin>();

        public override List<ViewItemPlugin> ViewItemPlugins => _viewItemPlugins;

        public override void Init()
        {
            _log.Info($"[Plugin] Init env={EnvironmentManager.Instance.EnvironmentType}");
            if (EnvironmentManager.Instance.EnvironmentType == EnvironmentType.SmartClient)
                _viewItemPlugins.Add(new Client.WebViewerViewItemPlugin());
        }

        public override void Close()
        {
            _log.Info("[Plugin] Close");
            _viewItemPlugins.Clear();
        }
    }
}
