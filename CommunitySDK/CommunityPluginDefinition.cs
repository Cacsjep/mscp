using System;
using System.Drawing;
using FontAwesome5;
using VideoOS.Platform;

namespace CommunitySDK
{
    /// <summary>
    /// Container PluginDefinition that creates the top-level "Community Plugins" node
    /// in the Management Client tree. Other admin plugins share this node via SharedNodeId.
    /// This plugin has no ItemNodes, BackgroundPlugins, or other functionality —
    /// it only exists to own the shared node with a custom icon.
    /// </summary>
    public class CommunityPluginDefinition : PluginDefinition
    {
        public static readonly Guid CommunityPluginId = new Guid("6F440BFA-F9F2-426E-9A49-C963AC196D4D");
        public static readonly Guid CommunitySharedNodeId = new Guid("6F440BFA-F9F2-426E-9A49-C963AC196D4E");
        public const string SharedNodeDisplayName = "Community Plugins";

        private Image _pluginIcon = PluginIcon.FallbackIcon;

        public override Guid Id => CommunityPluginId;
        public override string Name => "Community Plugins";
        
        public override string Manufacturer => "MSC Community Plugins";
        public override Image Icon => _pluginIcon ?? PluginIcon.FallbackIcon;

        public override void Init()
        {
            try
            {
                _pluginIcon = PluginIcon.Render(EFontAwesomeIcon.Solid_Cubes);
            }
            catch { }
        }

        public override void Close() { }
    }
}
