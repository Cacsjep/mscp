using CommunitySDK;
using FontAwesome5;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using VideoOS.Platform;
using VideoOS.Platform.Admin;
using VideoOS.Platform.Background;

namespace RTMPStreamer
{
    public class RTMPStreamerDefinition : PluginDefinition
    {
        internal static readonly Guid PluginId = new Guid("ABA1B2C3-D4E5-6789-ABCD-EF0123456789");
        internal static readonly Guid PluginKindId = new Guid("ABA1B2C3-D4E5-6789-ABCD-EF0123456780");
        internal static readonly Guid BackgroundPluginId = new Guid("ABA1B2C3-D4E5-6789-ABCD-EF0123456781");

        private List<BackgroundPlugin> _backgroundPlugins = new List<BackgroundPlugin>();
        private List<ItemNode> _itemNodes;
        private Image _pluginIcon = PluginIcon.FallbackIcon;
        private Image _folderIcon = PluginIcon.FallbackIcon;
        private Image _cameraIcon = PluginIcon.FallbackIcon;
        private static readonly PluginLog _log = new PluginLog("RTMPStreamer - PluginDefinition");

        public override Guid Id => PluginId;
        public override string Name => "RTMP Streamer";
        
        public override string Manufacturer => "https://github.com/Cacsjep";

        public override Image Icon => _pluginIcon ?? PluginIcon.FallbackIcon;

        public override void Init()
        {
            var env = EnvironmentManager.Instance.EnvironmentType;
            _log.Info($"PluginDefinition Init - environment: {env}");

            if (env != EnvironmentType.Service)
            {
                try
                {
                    _pluginIcon = PluginIcon.Render(EFontAwesomeIcon.Solid_BroadcastTower);
                    _folderIcon = PluginIcon.Render(EFontAwesomeIcon.Solid_FolderOpen);
                    _cameraIcon = PluginIcon.Render(EFontAwesomeIcon.Solid_Video);
                    _log.Info("FontAwesome icons rendered");
                }
                catch (Exception ex)
                {
                    _log.Error($"Failed to render FA icons: {ex.Message}");
                    try
                    {
                        var images = VideoOS.Platform.UI.Util.ImageList.Images;
                        _pluginIcon = images[VideoOS.Platform.UI.Util.PluginIx];
                        _folderIcon = images[VideoOS.Platform.UI.Util.FolderIconIx];
                        _cameraIcon = images[VideoOS.Platform.UI.Util.CameraIconIx];
                        _log.Info("Fallback to SDK icons succeeded");
                    }
                    catch (Exception ex2)
                    {
                        _log.Error($"Fallback to SDK icons also failed: {ex2.Message}");
                    }
                }

                // Reset cached ItemNodes so they pick up the real icons
                _itemNodes = null;
            }

            if (env == EnvironmentType.Service)
            {
                _backgroundPlugins.Add(new Background.RTMPStreamerBackgroundPlugin());
            }
        }

        private List<ItemNode> BuildItemNodes()
        {
            return new List<ItemNode>
            {
                new ItemNode(
                    PluginKindId,
                    Guid.Empty,
                    "RTMP Stream",
                    _cameraIcon,
                    "RTMP Streams",
                    _folderIcon,
                    Category.Text,
                    true,
                    ItemsAllowed.Many,
                    new Admin.RTMPStreamerItemManager(PluginKindId),
                    null
                )
            };
        }

        public override void Close()
        {
            _itemNodes = null;
            _backgroundPlugins.Clear();
        }

        public override List<ItemNode> ItemNodes
        {
            get
            {
                if (_itemNodes == null)
                {
                    _itemNodes = BuildItemNodes();
                }
                return _itemNodes;
            }
        }

        public override UserControl GenerateUserControl()
        {
            return new CommunitySDK.HtmlHelpUserControl(
                System.Reflection.Assembly.GetExecutingAssembly(), "Admin", "HelpPage.html");
        }

        public override List<BackgroundPlugin> BackgroundPlugins => _backgroundPlugins;
    }
}
