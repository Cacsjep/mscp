using System;
using System.Collections.Generic;
using CommunitySDK;
using FontAwesome5;
using VideoOS.Platform;
using VideoOS.Platform.Client;
using VideoOS.Platform.UI.Controls;

namespace ViewCarousel
{
    public class ViewCarouselDefinition : PluginDefinition
    {
        private static readonly VideoOSIconSourceBase _pluginIcon;

        internal static Guid ViewCarouselPluginId = new Guid("EC6E33FF-139F-4533-9058-5229DAE2F4E4");
        internal static Guid ViewCarouselViewItemKind = new Guid("EC6E33FF-139F-4533-9058-5229DAE2F4E4");

        internal static readonly PluginLog Log = new PluginLog("ViewCarousel");

        static ViewCarouselDefinition()
        {
            _pluginIcon = PluginIcon.RenderIconSource(EFontAwesomeIcon.Solid_SyncAlt);
        }

        internal static VideoOSIconSourceBase PluginIconSource => _pluginIcon;

        public override Guid Id => ViewCarouselPluginId;
        public override string Name => "View Carousel";
        public override string Manufacturer => "MSC Community Plugins";

        public override System.Drawing.Image Icon
            => VideoOS.Platform.UI.Util.ImageList.Images[VideoOS.Platform.UI.Util.PluginIx];

        public override List<ViewItemPlugin> ViewItemPlugins
            => new List<ViewItemPlugin> { new Client.ViewCarouselViewItemPlugin() };
    }
}
