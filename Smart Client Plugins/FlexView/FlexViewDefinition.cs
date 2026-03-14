using System;
using System.Collections.Generic;
using CommunitySDK;
using FontAwesome5;
using FlexView.Client;
using VideoOS.Platform;
using VideoOS.Platform.Client;
using VideoOS.Platform.UI.Controls;

namespace FlexView
{
    public class FlexViewDefinition : PluginDefinition
    {
        internal static readonly Guid FlexViewPluginId = new Guid("D4CE420C-9387-4AFA-A15A-21ABE59150C3");
        internal static readonly Guid FlexViewViewItemKind = new Guid("AFFAF409-E56B-4F70-ABD7-C46233839B5D");

        internal static readonly Guid ViewItemPluginId = new Guid("C77CD15E-56F4-4AB1-9580-3AC96DC1770C");
        internal static readonly Guid WorkspacePluginId = new Guid("621C7E7E-9A24-4CF2-88F3-F6D55EA61038");

        internal static readonly PluginLog Log = new PluginLog("FlexView");

        private static VideoOSIconSourceBase _pluginIconSource;

        private readonly List<ViewItemPlugin> _viewItemPlugins = new List<ViewItemPlugin>();
        private readonly List<WorkSpacePlugin> _workSpacePlugins = new List<WorkSpacePlugin>();

        static FlexViewDefinition()
        {
            try
            {
                _pluginIconSource = PluginIcon.RenderIconSource(EFontAwesomeIcon.Solid_ThLarge);
            }
            catch { }
        }

        internal static VideoOSIconSourceBase PluginIconSource => _pluginIconSource;

        public override Guid Id => FlexViewPluginId;
        public override string Name => "FlexView";
        public override string Manufacturer => "MSC Community Plugins";

        public override System.Drawing.Image Icon
            => VideoOS.Platform.UI.Util.ImageList.Images[VideoOS.Platform.UI.Util.PluginIx];

        public override void Init()
        {
            var env = EnvironmentManager.Instance.EnvironmentType;
            Log.Info($"PluginDefinition Init - environment: {env}");

            if (env == EnvironmentType.SmartClient)
            {
                _viewItemPlugins.Add(new FlexViewViewItemPlugin());
                _workSpacePlugins.Add(new FlexViewWorkspacePlugin());
                Log.Info("Registered ViewItemPlugin and WorkspacePlugin");
            }
        }

        public override void Close()
        {
            _viewItemPlugins.Clear();
            _workSpacePlugins.Clear();
        }

        public override List<ViewItemPlugin> ViewItemPlugins => _viewItemPlugins;
        public override List<WorkSpacePlugin> WorkSpacePlugins => _workSpacePlugins;
    }
}
