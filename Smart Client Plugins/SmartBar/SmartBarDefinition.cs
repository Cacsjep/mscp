using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunitySDK;
using FontAwesome5;
using VideoOS.Platform;
using VideoOS.Platform.Background;
using VideoOS.Platform.Client;
using VideoOS.Platform.UI.Controls;

namespace SmartBar
{
    public class SmartBarDefinition : PluginDefinition
    {
        private static readonly VideoOSIconSourceBase _pluginIcon;
        internal static readonly PluginLog Log = new PluginLog("SmartBar");

        internal static Guid SmartBarPluginId = new Guid("A7B8C9D0-E1F2-3456-7890-ABCDEF123456");
        internal static Guid SmartBarToolbarId = new Guid("A7B8C9D0-E1F2-3456-7890-ABCDEF123457");
        internal static Guid SmartBarBackButtonId = new Guid("A7B8C9D0-E1F2-3456-7890-ABCDEF123458");
        internal static Guid SmartBarBackgroundPluginId = new Guid("A7B8C9D0-E1F2-3456-7890-ABCDEF123459");

        private readonly List<WorkSpaceToolbarPlugin> _workSpaceToolbarPlugins = new List<WorkSpaceToolbarPlugin>();
        private readonly List<BackgroundPlugin> _backgroundPlugins = new List<BackgroundPlugin>();

        static SmartBarDefinition()
        {
            _pluginIcon = PluginIcon.RenderIconSource(EFontAwesomeIcon.Solid_Toolbox);
        }

        internal static VideoOSIconSourceBase PluginIconSource => _pluginIcon;

        public override Guid Id => SmartBarPluginId;

        public override string Name => "Smart Bar";

        public override string Manufacturer => "MSCP Community";

        

        public override System.Drawing.Image Icon
            => VideoOS.Platform.UI.Util.ImageList.Images[VideoOS.Platform.UI.Util.PluginIx];

        public override void Init()
        {
            if (EnvironmentManager.Instance.EnvironmentType == EnvironmentType.SmartClient)
            {
                SmartBarConfig.Load();
                Client.SmartBarHistory.ApplyMaxHistory(SmartBarConfig.MaxHistory);

                _workSpaceToolbarPlugins.Add(new Client.SmartBarToolbarPlugin());
                _backgroundPlugins.Add(new Background.SmartBarBackgroundPlugin());
                Client.SmartBarKeyHandler.Install();
                Client.SmartBarHistory.Install();
                try { Client.SmartBarWindow.EnsureSmartBarViews(); }
                catch (Exception ex) { Log.Error("EnsureSmartBarViews failed", ex); }

                Log.Info("Plugin initialized");
            }
        }

        public override void Close()
        {
            Client.SmartBarKeyHandler.Uninstall();
            Client.SmartBarHistory.Uninstall();
            _workSpaceToolbarPlugins.Clear();
            _backgroundPlugins.Clear();
        }

        public override List<WorkSpaceToolbarPlugin> WorkSpaceToolbarPlugins => _workSpaceToolbarPlugins;

        public override List<BackgroundPlugin> BackgroundPlugins => _backgroundPlugins;

        public override Collection<SettingsPanelPlugin> SettingsPanelPlugins
            => new Collection<SettingsPanelPlugin> { new Client.SmartBarSettingsPanel() };
    }
}
