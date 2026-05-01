using System;
using System.Collections.Generic;
using CommunitySDK;
using FontAwesome5;
using TimelineJump.Client;
using VideoOS.Platform;
using VideoOS.Platform.Client;
using VideoOS.Platform.UI.Controls;

namespace TimelineJump
{
    public class TimelineJumpDefinition : PluginDefinition
    {
        internal static readonly PluginLog Log = new PluginLog("TimelineJump");

        internal static readonly Guid PluginId = new Guid("D4F8A726-3B91-4C5E-A0F8-7B2D9E4C1A35");
        internal static readonly Guid ToolbarPluginId = new Guid("D4F8A726-3B91-4C5E-A0F8-7B2D9E4C1A36");

        private static VideoOSIconSourceBase _pluginIcon;
        private readonly List<WorkSpaceToolbarPlugin> _toolbarPlugins = new List<WorkSpaceToolbarPlugin>();

        internal static VideoOSIconSourceBase PluginIconSource => _pluginIcon;

        public override Guid Id => PluginId;
        public override string Name => "Timeline Jump";
        public override string Manufacturer => "MSCP Community";

        public override System.Drawing.Image Icon
            => VideoOS.Platform.UI.Util.ImageList.Images[VideoOS.Platform.UI.Util.PluginIx];

        public override void Init()
        {
            if (EnvironmentManager.Instance.EnvironmentType != EnvironmentType.SmartClient)
                return;

            try
            {
                _pluginIcon = PluginIcon.RenderIconSource(EFontAwesomeIcon.Solid_History);
            }
            catch
            {
                _pluginIcon = new VideoOSIconBuiltInSource { Icon = VideoOSIconBuiltInSource.Icons.Selection_Hand };
            }

            TimelineJump.ImageViewerHelper.Init();
            _toolbarPlugins.Add(new TimelineJumpToolbarPlugin());
            Log.Info("Plugin initialized");
        }

        public override void Close()
        {
            TimelineJump.ImageViewerHelper.Close();
            _toolbarPlugins.Clear();
        }

        public override List<WorkSpaceToolbarPlugin> WorkSpaceToolbarPlugins => _toolbarPlugins;
    }
}
