using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunitySDK;
using FontAwesome5;
using SCRemoteControl.Background;
using VideoOS.Platform;
using VideoOS.Platform.Background;
using VideoOS.Platform.Client;
using VideoOS.Platform.UI.Controls;

namespace SCRemoteControl
{
    public class SCRemoteControlDefinition : PluginDefinition
    {
        private static readonly VideoOSIconSourceBase _pluginIcon;
        internal static readonly PluginLog Log = new PluginLog("SCRemoteControl");

        internal static Guid SCRemoteControlPluginId = new Guid("794F7983-3036-499E-BDFD-13C71DCBB246");
        internal static Guid SCRemoteControlBackgroundPluginId = new Guid("184E7E63-038F-4063-BA54-56B5BD62A925");

        static SCRemoteControlDefinition()
        {
            _pluginIcon = PluginIcon.RenderIconSource(EFontAwesomeIcon.Solid_Satellite);
        }

        internal static VideoOSIconSourceBase PluginIconSource => _pluginIcon;

        public override Guid Id => SCRemoteControlPluginId;
        public override string Name => "SC Remote Control";
        public override string Manufacturer => "MSCP Community";

        public override System.Drawing.Image Icon
            => VideoOS.Platform.UI.Util.ImageList.Images[VideoOS.Platform.UI.Util.PluginIx];

        public override void Init()
        {
            if (EnvironmentManager.Instance.EnvironmentType == EnvironmentType.SmartClient)
            {
                SCRemoteControlConfig.Load();
                Log.Info("Plugin initialized");
            }
        }

        public override void Close()
        {
        }

        public override List<BackgroundPlugin> BackgroundPlugins
            => new List<BackgroundPlugin> { new SCRemoteControlBackgroundPlugin() };

        public override Collection<SettingsPanelPlugin> SettingsPanelPlugins
            => new Collection<SettingsPanelPlugin> { new Client.SCRemoteControlSettingsPanel() };
    }
}
