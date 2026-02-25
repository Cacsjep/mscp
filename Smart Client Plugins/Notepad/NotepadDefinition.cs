using System;
using System.Collections.Generic;
using System.Reflection;
using Notepad.Background;
using Notepad.Client;
using VideoOS.Platform;
using VideoOS.Platform.Background;
using VideoOS.Platform.Client;
using VideoOS.Platform.UI.Controls;

namespace Notepad
{
    public class NotepadDefinition : PluginDefinition
    {
        private static readonly VideoOSIconSourceBase _pluginIcon;

        internal static Guid NotepadPluginId = new Guid("F1A2B3C4-D5E6-7890-ABCD-EF1234600001");
        internal static Guid NotepadViewItemKind = new Guid("F1A2B3C4-D5E6-7890-ABCD-EF1234600002");
        internal static Guid NotepadBackgroundPluginId = new Guid("F1A2B3C4-D5E6-7890-ABCD-EF1234600003");

        static NotepadDefinition()
        {
            var packString = $"pack://application:,,,/{Assembly.GetExecutingAssembly().GetName().Name};component/Resources/PluginIcon.png";
            _pluginIcon = new VideoOSIconUriSource { Uri = new Uri(packString) };
        }

        internal static VideoOSIconSourceBase PluginIcon => _pluginIcon;

        public override Guid Id => NotepadPluginId;

        public override string Name => "Notepad Plugin";

        public override string Manufacturer => "Sample Manufacturer";

        public override string VersionString => "1.0.0.0";

        public override System.Drawing.Image Icon
            => VideoOS.Platform.UI.Util.ImageList.Images[VideoOS.Platform.UI.Util.PluginIx];

        public override List<ViewItemPlugin> ViewItemPlugins
            => new List<ViewItemPlugin> { new NotepadViewItemPlugin() };

        public override List<BackgroundPlugin> BackgroundPlugins
            => new List<BackgroundPlugin> { new NotepadBackgroundPlugin() };
    }
}
