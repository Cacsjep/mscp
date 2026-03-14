using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunitySDK;
using FontAwesome5;
using MonitorRTMPStreamer.Background;
using MonitorRTMPStreamer.Client;
using VideoOS.Platform;
using VideoOS.Platform.Background;
using VideoOS.Platform.Client;
using VideoOS.Platform.UI.Controls;

namespace MonitorRTMPStreamer
{
    public class StreamerDefinition : PluginDefinition
    {
        private static readonly VideoOSIconSourceBase _pluginIcon;

        internal static Guid StreamerPluginId = new Guid("E948362B-D5F9-4B87-B8EF-9D33C04A9E2F");
        internal static Guid StreamerBackgroundPluginId = new Guid("7E3DCB6C-42B9-4D68-9A70-9D30A88AEF20");

        static StreamerDefinition()
        {
            _pluginIcon = PluginIcon.RenderIconSource(EFontAwesomeIcon.Solid_Video);
        }

        internal static VideoOSIconSourceBase PluginIconSource => _pluginIcon;

        public override Guid Id => StreamerPluginId;

        public override string Name => "Monitor RTMP Streamer";

        public override string Manufacturer => "MSC Community Plugins";


        public override System.Drawing.Image Icon
            => VideoOS.Platform.UI.Util.ImageList.Images[VideoOS.Platform.UI.Util.PluginIx];

        public override Collection<SettingsPanelPlugin> SettingsPanelPlugins
            => new Collection<SettingsPanelPlugin> { new StreamerSettingsPanel() };

        public override List<BackgroundPlugin> BackgroundPlugins
            => new List<BackgroundPlugin> { new StreamerBackgroundPlugin() };
    }
}
