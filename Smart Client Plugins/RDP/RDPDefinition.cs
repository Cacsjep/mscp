using System;
using System.Collections.Generic;
using CommunitySDK;
using FontAwesome5;
using RDP.Client;
using VideoOS.Platform;
using VideoOS.Platform.Client;
using VideoOS.Platform.UI.Controls;

namespace RDP
{
    public class RDPDefinition : PluginDefinition
    {
        private static readonly VideoOSIconSourceBase _pluginIcon;

        internal static Guid RDPPluginId = new Guid("A1B2C3D4-E5F6-7890-ABCD-EF1234567890");
        internal static Guid RDPViewItemKind = new Guid("B2C3D4E5-F6A7-8901-BCDE-F12345678901");

        static RDPDefinition()
        {
            _pluginIcon = PluginIcon.RenderIconSource(EFontAwesomeIcon.Solid_Desktop);
        }

        internal static VideoOSIconSourceBase PluginIconSource => _pluginIcon;

        public override Guid Id => RDPPluginId;

        public override string Name => "RDP Plugin";

        public override string Manufacturer => "Sample Manufacturer";


        public override System.Drawing.Image Icon
            => VideoOS.Platform.UI.Util.ImageList.Images[VideoOS.Platform.UI.Util.PluginIx];

        public override List<ViewItemPlugin> ViewItemPlugins
            => new List<ViewItemPlugin> { new RDPViewItemPlugin() };
    }
}
