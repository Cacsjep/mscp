using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using CommunitySDK;
using FontAwesome5;
using MetadataViewer.Admin;
using VideoOS.Platform;
using VideoOS.Platform.Admin;

namespace MetadataViewer
{
    public class MetadataViewerDefinition : PluginDefinition
    {
        internal static readonly Guid PluginId = new Guid("69067167-C20F-4959-8D24-7FA401C0CCBA");
        internal static readonly Guid MetadataViewerKindId = new Guid("B0D66D0E-5358-4026-8919-838B4EB29DEA");

        private List<ItemNode> _itemNodes;
        private Image _pluginIcon = PluginIcon.FallbackIcon;
        private Image _itemIcon = PluginIcon.FallbackIcon;

        private static readonly PluginLog _log = new PluginLog("MetadataViewer - PluginDefinition");

        public override Guid Id => PluginId;
        public override string Name => "Metadata Viewer";
        public override string Manufacturer => "https://github.com/Cacsjep";
        public override Image Icon => _pluginIcon ?? PluginIcon.FallbackIcon;

        public override void Init()
        {
            var env = EnvironmentManager.Instance.EnvironmentType;
            _log.Info($"PluginDefinition Init - environment: {env}");

            if (env == EnvironmentType.Administration)
            {
                try
                {
                    _pluginIcon = PluginIcon.Render(EFontAwesomeIcon.Solid_Stream);
                    _itemIcon = PluginIcon.Render(EFontAwesomeIcon.Solid_Eye);
                }
                catch (Exception ex)
                {
                    _log.Error($"Failed to render FA icons: {ex.Message}");
                    try
                    {
                        var images = VideoOS.Platform.UI.Util.ImageList.Images;
                        _pluginIcon = images[VideoOS.Platform.UI.Util.PluginIx];
                        _itemIcon = images[VideoOS.Platform.UI.Util.PluginIx];
                    }
                    catch { }
                }
                _itemNodes = null;
            }
        }

        public override void Close()
        {
            _itemNodes = null;
        }

        public override List<ItemNode> ItemNodes
        {
            get
            {
                if (EnvironmentManager.Instance.EnvironmentType != EnvironmentType.Administration)
                    return null;

                if (_itemNodes == null)
                {
                    _itemNodes = new List<ItemNode>
                    {
                        new ItemNode(
                            MetadataViewerKindId,
                            Guid.Empty,
                            "Metadata Viewer",
                            _pluginIcon,
                            "Metadata Viewers",
                            _itemIcon,
                            Category.Text,
                            true,
                            ItemsAllowed.Many,
                            new MetadataViewerItemManager(MetadataViewerKindId),
                            null)
                    };
                }
                return _itemNodes;
            }
        }

        public override UserControl GenerateUserControl()
        {
            return new HtmlHelpUserControl(
                System.Reflection.Assembly.GetExecutingAssembly(), "Admin", "HelpPage.html");
        }
    }
}
