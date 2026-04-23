using CommunitySDK;
using FontAwesome5;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using VideoOS.Platform;
using VideoOS.Platform.Admin;
using VideoOS.Platform.Background;

namespace BarcodeReader
{
    public class BarcodeReaderDefinition : PluginDefinition
    {
        internal static readonly Guid PluginId          = new Guid("59D9A0FB-5327-4E9F-87E7-80278256FAF7");
        internal static readonly Guid PluginKindId      = new Guid("36C8BC15-9A59-4178-B80D-FBC4AB9E348C");
        internal static readonly Guid BackgroundPluginId = new Guid("BFEAC914-FF08-41A2-837E-4754D316AB3D");

        private List<BackgroundPlugin> _backgroundPlugins = new List<BackgroundPlugin>();
        private List<ItemNode> _itemNodes;
        private Image _pluginIcon = PluginIcon.FallbackIcon;
        private Image _folderIcon = PluginIcon.FallbackIcon;
        private Image _channelIcon = PluginIcon.FallbackIcon;
        private static readonly PluginLog _log = new PluginLog("BarcodeReader - PluginDefinition");

        public override Guid Id => PluginId;
        public override string Name => "Barcode Reader";
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
                    _pluginIcon  = PluginIcon.Render(EFontAwesomeIcon.Solid_Qrcode);
                    _folderIcon  = PluginIcon.Render(EFontAwesomeIcon.Solid_FolderOpen);
                    _channelIcon = PluginIcon.Render(EFontAwesomeIcon.Solid_Barcode);
                }
                catch (Exception ex)
                {
                    _log.Error($"Failed to render FA icons: {ex.Message}");
                    try
                    {
                        var images = VideoOS.Platform.UI.Util.ImageList.Images;
                        _pluginIcon  = images[VideoOS.Platform.UI.Util.PluginIx];
                        _folderIcon  = images[VideoOS.Platform.UI.Util.FolderIconIx];
                        _channelIcon = images[VideoOS.Platform.UI.Util.CameraIconIx];
                    }
                    catch { }
                }
                _itemNodes = null;
            }

            if (env == EnvironmentType.Service)
            {
                _backgroundPlugins.Add(new Background.BarcodeReaderBackgroundPlugin());
            }
        }

        public override void Close()
        {
            _itemNodes = null;
            _backgroundPlugins.Clear();
        }

        private List<ItemNode> BuildItemNodes()
        {
            return new List<ItemNode>
            {
                new ItemNode(
                    PluginKindId,
                    Guid.Empty,
                    "Barcode Channel",
                    _channelIcon,
                    "Barcode Channels",
                    _folderIcon,
                    Category.Text,
                    true,
                    ItemsAllowed.Many,
                    new Admin.BarcodeChannelItemManager(PluginKindId),
                    null
                )
            };
        }

        public override List<ItemNode> ItemNodes
        {
            get
            {
                if (_itemNodes == null) _itemNodes = BuildItemNodes();
                return _itemNodes;
            }
        }

        public override UserControl GenerateUserControl()
        {
            return new HtmlHelpUserControl(
                System.Reflection.Assembly.GetExecutingAssembly(), "Admin", "HelpPage.html");
        }

        public override List<BackgroundPlugin> BackgroundPlugins => _backgroundPlugins;
    }
}
