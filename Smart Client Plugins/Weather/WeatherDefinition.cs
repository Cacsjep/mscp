using System;
using System.Collections.Generic;
using System.Reflection;
using Weather.Background;
using Weather.Client;
using VideoOS.Platform;
using VideoOS.Platform.Background;
using VideoOS.Platform.Client;
using VideoOS.Platform.UI.Controls;

namespace Weather
{
    public class WeatherDefinition : PluginDefinition
    {
        private static readonly VideoOSIconSourceBase _pluginIcon;

        internal static Guid WeatherPluginId = new Guid("E1F2A3B4-C5D6-7890-ABCD-EF1234500001");
        internal static Guid WeatherViewItemKind = new Guid("E1F2A3B4-C5D6-7890-ABCD-EF1234500002");
        internal static Guid WeatherBackgroundPluginId = new Guid("E1F2A3B4-C5D6-7890-ABCD-EF1234500003");

        static WeatherDefinition()
        {
            var packString = $"pack://application:,,,/{Assembly.GetExecutingAssembly().GetName().Name};component/Resources/PluginIcon.png";
            _pluginIcon = new VideoOSIconUriSource { Uri = new Uri(packString) };
        }

        internal static VideoOSIconSourceBase PluginIcon => _pluginIcon;

        public override Guid Id => WeatherPluginId;

        public override string Name => "Weather Plugin";

        public override string Manufacturer => "Sample Manufacturer";

        public override string VersionString => "1.0.0.0";

        public override System.Drawing.Image Icon
            => VideoOS.Platform.UI.Util.ImageList.Images[VideoOS.Platform.UI.Util.PluginIx];

        public override List<ViewItemPlugin> ViewItemPlugins
            => new List<ViewItemPlugin> { new WeatherViewItemPlugin() };

        public override List<BackgroundPlugin> BackgroundPlugins
            => new List<BackgroundPlugin> { new WeatherBackgroundPlugin() };
    }
}
