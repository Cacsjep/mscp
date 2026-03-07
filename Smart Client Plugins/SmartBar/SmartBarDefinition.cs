using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using CommunitySDK;
using FontAwesome5;
using VideoOS.Platform;
using VideoOS.Platform.Client;
using VideoOS.Platform.UI.Controls;

namespace SmartBar
{
    public class SmartBarDefinition : PluginDefinition
    {
        private static readonly VideoOSIconSourceBase _pluginIcon;

        internal static Guid SmartBarPluginId = new Guid("A7B8C9D0-E1F2-3456-7890-ABCDEF123456");
        internal static Guid SmartBarToolbarId = new Guid("A7B8C9D0-E1F2-3456-7890-ABCDEF123457");
        internal static Guid SmartBarBackButtonId = new Guid("A7B8C9D0-E1F2-3456-7890-ABCDEF123458");

        private readonly List<WorkSpaceToolbarPlugin> _workSpaceToolbarPlugins = new List<WorkSpaceToolbarPlugin>();

        static SmartBarDefinition()
        {
            _pluginIcon = PluginIcon.RenderIconSource(EFontAwesomeIcon.Solid_Toolbox);
        }

        internal static VideoOSIconSourceBase PluginIconSource => _pluginIcon;

        public override Guid Id => SmartBarPluginId;

        public override string Name => "Smart Bar";

        public override string Manufacturer => "MSCP Community";

        public override string VersionString => "1.0.0.0";

        public override System.Drawing.Image Icon
            => VideoOS.Platform.UI.Util.ImageList.Images[VideoOS.Platform.UI.Util.PluginIx];

        public override void Init()
        {
            if (EnvironmentManager.Instance.EnvironmentType == EnvironmentType.SmartClient)
            {
                _workSpaceToolbarPlugins.Add(new Client.SmartBarToolbarPlugin());
                Client.SmartBarKeyHandler.Install();
                Client.SmartBarHistory.Install();
                try { Client.SmartBarWindow.EnsureSmartBarViews(); }
                catch (System.Exception ex) { System.Diagnostics.Debug.WriteLine($"[SmartBar] EnsureSmartBarViews failed: {ex}"); }
            }
        }

        public override void Close()
        {
            Client.SmartBarKeyHandler.Uninstall();
            Client.SmartBarHistory.Uninstall();
            _workSpaceToolbarPlugins.Clear();
        }

        public override List<WorkSpaceToolbarPlugin> WorkSpaceToolbarPlugins => _workSpaceToolbarPlugins;
    }
}
