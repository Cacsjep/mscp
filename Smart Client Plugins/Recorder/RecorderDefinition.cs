using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reflection;
using Recorder.Background;
using Recorder.Client;
using VideoOS.Platform;
using VideoOS.Platform.Background;
using VideoOS.Platform.Client;
using VideoOS.Platform.UI.Controls;

namespace Recorder
{
    public class RecorderDefinition : PluginDefinition
    {
        private static readonly VideoOSIconSourceBase _pluginIcon;

        internal static Guid RecorderPluginId = new Guid("E948362B-D5F9-4B87-B8EF-9D33C04A9E2F");
        internal static Guid RecorderBackgroundPluginId = new Guid("7E3DCB6C-42B9-4D68-9A70-9D30A88AEF20");

        static RecorderDefinition()
        {
            var packString = $"pack://application:,,,/{Assembly.GetExecutingAssembly().GetName().Name};component/Resources/PluginIcon.png";
            _pluginIcon = new VideoOSIconUriSource { Uri = new Uri(packString) };
        }

        internal static VideoOSIconSourceBase PluginIcon => _pluginIcon;

        public override Guid Id => RecorderPluginId;

        public override string Name => "Recorder Plugin";

        public override string Manufacturer => "MSC Community Plugins";

        public override string VersionString => "1.0.0.0";

        public override System.Drawing.Image Icon
            => VideoOS.Platform.UI.Util.ImageList.Images[VideoOS.Platform.UI.Util.PluginIx];

        public override Collection<SettingsPanelPlugin> SettingsPanelPlugins
            => new Collection<SettingsPanelPlugin> { new RecorderSettingsPanel() };

        public override List<BackgroundPlugin> BackgroundPlugins
            => new List<BackgroundPlugin> { new RecorderBackgroundPlugin() };
    }
}
