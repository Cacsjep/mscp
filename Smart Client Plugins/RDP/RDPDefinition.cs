using System;
using System.Collections.Generic;
using System.Reflection;
using RDP.Background;
using RDP.Client;
using VideoOS.Platform;
using VideoOS.Platform.Background;
using VideoOS.Platform.Client;
using VideoOS.Platform.UI.Controls;

namespace RDP
{
    public class RDPDefinition : PluginDefinition
    {
        private static readonly VideoOSIconSourceBase _pluginIcon;

        internal static Guid RDPPluginId = new Guid("A1B2C3D4-E5F6-7890-ABCD-EF1234567890");
        internal static Guid RDPViewItemKind = new Guid("B2C3D4E5-F6A7-8901-BCDE-F12345678901");
        internal static Guid RDPBackgroundPluginId = new Guid("C3D4E5F6-A7B8-9012-CDEF-123456789012");

        static RDPDefinition()
        {
            var packString = $"pack://application:,,,/{Assembly.GetExecutingAssembly().GetName().Name};component/Resources/PluginIcon.png";
            _pluginIcon = new VideoOSIconUriSource { Uri = new Uri(packString) };
        }

        internal static VideoOSIconSourceBase PluginIcon => _pluginIcon;

        public override Guid Id => RDPPluginId;

        public override string Name => "RDP Plugin";

        public override string Manufacturer => "Sample Manufacturer";

        public override string VersionString => "1.0.0.0";

        public override System.Drawing.Image Icon
            => VideoOS.Platform.UI.Util.ImageList.Images[VideoOS.Platform.UI.Util.PluginIx];

        public override List<ViewItemPlugin> ViewItemPlugins
            => new List<ViewItemPlugin> { new RDPViewItemPlugin() };

        public override List<BackgroundPlugin> BackgroundPlugins
            => new List<BackgroundPlugin> { new RDPBackgroundPlugin() };
    }
}
