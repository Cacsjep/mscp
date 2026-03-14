using System;
using System.Collections.Generic;
using CommunitySDK;
using FontAwesome5;
using VideoOS.Platform;
using VideoOS.Platform.Client;
using VideoOS.Platform.UI.Controls;

namespace Weather
{
    public class WeatherDefinition : PluginDefinition
    {
        private static readonly VideoOSIconSourceBase _pluginIcon;

        internal static Guid WeatherPluginId = new Guid("E1F2A3B4-C5D6-7890-ABCD-EF1234500001");
        internal static Guid WeatherViewItemKind = new Guid("E1F2A3B4-C5D6-7890-ABCD-EF1234500002");

        static WeatherDefinition()
        {
            _pluginIcon = PluginIcon.RenderIconSource(EFontAwesomeIcon.Solid_CloudSun);
        }

        internal static VideoOSIconSourceBase PluginIconSource => _pluginIcon;

        public override Guid Id => WeatherPluginId;

        public override string Name => "Weather Plugin";

        public override string Manufacturer => "Sample Manufacturer";

        

        public override System.Drawing.Image Icon
            => VideoOS.Platform.UI.Util.ImageList.Images[VideoOS.Platform.UI.Util.PluginIx];

        public override List<ViewItemPlugin> ViewItemPlugins
            => new List<ViewItemPlugin> { new Client.WeatherViewItemPlugin() };
    }
}
