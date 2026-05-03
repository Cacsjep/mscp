using System;
using System.Collections.Generic;
using CommunitySDK;
using FontAwesome5;
using VideoOS.Platform;
using VideoOS.Platform.Client;
using VideoOS.Platform.UI.Controls;

namespace MetadataDisplay
{
    public class MetadataDisplayDefinition : PluginDefinition
    {
        private static readonly PluginLog _log = new PluginLog("MetadataDisplay");
        private static readonly VideoOSIconSourceBase _pluginIcon;

        internal static Guid MetadataDisplayPluginId = new Guid("0BABC493-B49E-43AD-A40D-76D1D6F702BC");
        internal static Guid MetadataDisplayViewItemKind = new Guid("91375E50-9D13-4088-A1F7-3CF9B9952FE9");

        static MetadataDisplayDefinition()
        {
            _pluginIcon = PluginIcon.RenderIconSource(EFontAwesomeIcon.Solid_TachometerAlt);
        }

        internal static VideoOSIconSourceBase PluginIconSource => _pluginIcon;

        public override Guid Id => MetadataDisplayPluginId;

        public override string Name => "Metadata Display";

        public override string Manufacturer => "MSC Community Plugins";

        public override System.Drawing.Image Icon
            => VideoOS.Platform.UI.Util.ImageList.Images[VideoOS.Platform.UI.Util.PluginIx];

        public override List<ViewItemPlugin> ViewItemPlugins
            => new List<ViewItemPlugin> { new Client.MetadataDisplayViewItemPlugin() };

        public override void Init()
        {
            _log.Info($"[Plugin] Init env={EnvironmentManager.Instance.EnvironmentType}");
        }

        public override void Close()
        {
            _log.Info("[Plugin] Close");
        }
    }
}
