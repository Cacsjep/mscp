using System;
using System.Collections.Generic;
using CommunitySDK;
using FontAwesome5;
using LiveExporter.Client;
using VideoOS.Platform;
using VideoOS.Platform.Client;
using VideoOS.Platform.UI.Controls;

namespace LiveExporter
{
    public class LiveExporterDefinition : PluginDefinition
    {
        internal static readonly PluginLog Log = new PluginLog("LiveExporter");

        internal static readonly Guid PluginId = new Guid("C616AA54-A72F-40D4-B5D2-BC3C2E1EDDFD");
        internal static readonly Guid ToolbarPluginId = new Guid("07AC2FE3-DF83-4454-8264-39A4F93D4CDD");

        private static VideoOSIconSourceBase _pluginIcon;
        private readonly List<WorkSpaceToolbarPlugin> _toolbarPlugins = new List<WorkSpaceToolbarPlugin>();

        internal static VideoOSIconSourceBase PluginIconSource => _pluginIcon;

        public override Guid Id => PluginId;
        public override string Name => "Live Exporter";
        public override string Manufacturer => "MSCP Community";

        public override System.Drawing.Image Icon
            => VideoOS.Platform.UI.Util.ImageList.Images[VideoOS.Platform.UI.Util.PluginIx];

        public override void Init()
        {
            if (EnvironmentManager.Instance.EnvironmentType != EnvironmentType.SmartClient)
                return;

            try
            {
                _pluginIcon = PluginIcon.RenderIconSource(EFontAwesomeIcon.Solid_FileExport);
            }
            catch
            {
                _pluginIcon = new VideoOSIconBuiltInSource { Icon = VideoOSIconBuiltInSource.Icons.Selection_Hand };
            }

            HotspotCameraSource.Init();
            _toolbarPlugins.Add(new LiveExporterToolbarPlugin());
            Log.Info("Plugin initialized");
        }

        public override void Close()
        {
            HotspotCameraSource.Close();
            _toolbarPlugins.Clear();
        }

        public override List<WorkSpaceToolbarPlugin> WorkSpaceToolbarPlugins => _toolbarPlugins;
    }
}
