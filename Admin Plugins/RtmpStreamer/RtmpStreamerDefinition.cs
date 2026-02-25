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
        private Image _pluginIcon;
        private Image _folderIcon;
        private Image _cameraIcon;

        public override Guid Id => PluginId;
        public override string Name => "RTMP Streamer";
        public override string SharedNodeName => "RTMP Streamer";
        public override string VersionString => "1.0.0.0";
        public override string Manufacturer => "https://github.com/Cacsjep";

        public override Image Icon => _pluginIcon;

        public override void Init()
        {
            var images = VideoOS.Platform.UI.Util.ImageList.Images;
            _pluginIcon = images[VideoOS.Platform.UI.Util.PluginIx];
            _folderIcon = images[VideoOS.Platform.UI.Util.FolderIconIx];
            _cameraIcon = images[VideoOS.Platform.UI.Util.CameraIconIx];

            if (EnvironmentManager.Instance.EnvironmentType == EnvironmentType.Service)
            {
                _backgroundPlugins.Add(new Background.RTMPStreamerBackgroundPlugin());
            }
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
                    _itemNodes = new List<ItemNode>
                    {
                        new ItemNode(
                            PluginKindId,
                            Guid.Empty,
                            "RTMP Stream",          // singular item name
                            _cameraIcon,            // node image
                            "RTMP Streams",         // plural/group name
                            _folderIcon,            // group image
                            Category.Text,
                            true,                   // includeInExport
                            ItemsAllowed.Many,
                            new Admin.RTMPStreamerItemManager(PluginKindId),
                            null
                        )
                    };
                }
                return _itemNodes;
            }
        }

        public override UserControl GenerateUserControl()
        {
            return new Admin.HtmlHelpUserControl();
        }

        public override List<BackgroundPlugin> BackgroundPlugins => _backgroundPlugins;
    }
}
