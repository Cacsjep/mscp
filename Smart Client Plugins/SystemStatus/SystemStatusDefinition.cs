using System;
using System.Collections.Generic;
using CommunitySDK;
using FontAwesome5;
using SystemStatus.Background;
using SystemStatus.Client;
using VideoOS.Platform;
using VideoOS.Platform.Background;
using VideoOS.Platform.Client;
using VideoOS.Platform.UI.Controls;

namespace SystemStatus
{
    public class SystemStatusDefinition : PluginDefinition
    {
        internal static readonly PluginLog Log = new PluginLog("SystemStatus");

        internal static readonly Guid PluginId = new Guid("A170C671-FFE7-43F7-ADC2-F442168632A2");
        internal static readonly Guid BackgroundPluginId = new Guid("53D638ED-F000-4F06-8530-F3EBAD0493DD");
        internal static readonly Guid ToolbarPluginId = new Guid("7617C9EF-AC26-45C6-BB14-8EA677D7C276");

        private static VideoOSIconSourceBase _pluginIcon;
        private readonly List<BackgroundPlugin> _backgroundPlugins = new List<BackgroundPlugin>();
        private readonly List<WorkSpaceToolbarPlugin> _toolbarPlugins = new List<WorkSpaceToolbarPlugin>();
        private readonly List<ViewItemPlugin> _viewItemPlugins = new List<ViewItemPlugin>();

        internal static VideoOSIconSourceBase PluginIconSource => _pluginIcon;

        public override Guid Id => PluginId;
        public override string Name => "System Status";
        public override string Manufacturer => "MSCP Community";

        public override System.Drawing.Image Icon
            => VideoOS.Platform.UI.Util.ImageList.Images[VideoOS.Platform.UI.Util.PluginIx];

        public override void Init()
        {
            if (EnvironmentManager.Instance.EnvironmentType != EnvironmentType.SmartClient)
                return;

            try
            {
                _pluginIcon = PluginIcon.RenderIconSource(EFontAwesomeIcon.Solid_Heartbeat);
            }
            catch
            {
                _pluginIcon = new VideoOSIconBuiltInSource { Icon = VideoOSIconBuiltInSource.Icons.Selection_Hand };
            }

            _backgroundPlugins.Add(new SystemStatusBackgroundPlugin());
            _toolbarPlugins.Add(new SystemStatusToolbarPlugin());
            _viewItemPlugins.Add(new CameraUserStatusViewItemPlugin());
            Log.Info("Plugin initialized");
        }

        public override void Close()
        {
            _backgroundPlugins.Clear();
            _toolbarPlugins.Clear();
            _viewItemPlugins.Clear();
        }

        public override List<BackgroundPlugin> BackgroundPlugins => _backgroundPlugins;
        public override List<WorkSpaceToolbarPlugin> WorkSpaceToolbarPlugins => _toolbarPlugins;
        public override List<ViewItemPlugin> ViewItemPlugins => _viewItemPlugins;
    }
}
