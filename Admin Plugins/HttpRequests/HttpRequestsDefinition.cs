using CommunitySDK;
using FontAwesome5;
using HttpRequests.Admin;
using HttpRequests.Background;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using VideoOS.Platform;
using VideoOS.Platform.Admin;
using VideoOS.Platform.Background;
using VideoOS.Platform.RuleAction;

namespace HttpRequests
{
    public class HttpRequestsDefinition : PluginDefinition
    {
        internal static readonly Guid PluginId = new Guid("C4A1B2D3-E5F6-4789-AB01-23456789A001");
        internal static readonly Guid FolderKindId = new Guid("C4A1B2D3-E5F6-4789-AB01-23456789A010");
        internal static readonly Guid RequestKindId = new Guid("C4A1B2D3-E5F6-4789-AB01-23456789A020");
        internal static readonly Guid BackgroundPluginId = new Guid("C4A1B2D3-E5F6-4789-AB01-23456789A030");

        // Event registration GUIDs
        internal static readonly Guid EventGroupId = new Guid("C4A1B2D3-E5F6-4789-AB01-23456789A040");
        internal static readonly Guid EvtRequestExecutedId = new Guid("C4A1B2D3-E5F6-4789-AB01-23456789A041");
        internal static readonly Guid EvtRequestFailedId = new Guid("C4A1B2D3-E5F6-4789-AB01-23456789A042");
        internal static readonly Guid StateGroupId = new Guid("C4A1B2D3-E5F6-4789-AB01-23456789A050");

        private List<BackgroundPlugin> _backgroundPlugins = new List<BackgroundPlugin>();
        private List<ItemNode> _itemNodes;
        private HttpRequestsActionManager _actionManager;
        private Image _pluginIcon = PluginIcon.FallbackIcon;
        private Image _folderIcon = PluginIcon.FallbackIcon;
        private Image _requestIcon = PluginIcon.FallbackIcon;

        private static readonly PluginLog _log = new PluginLog("HttpRequests - PluginDefinition");

        public override Guid Id => PluginId;
        public override string Name => "HTTP Requests";
        
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
                    _pluginIcon = PluginIcon.Render(EFontAwesomeIcon.Solid_PaperPlane);
                    _folderIcon = PluginIcon.Render(EFontAwesomeIcon.Solid_FolderOpen);
                    _requestIcon = PluginIcon.Render(EFontAwesomeIcon.Solid_Globe);
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
                        _requestIcon = images[VideoOS.Platform.UI.Util.PluginIx];
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
                _log.Info("Registering HttpRequestsBackgroundPlugin (Event Server)");
                _backgroundPlugins.Add(new HttpRequestsBackgroundPlugin());
            }

            _actionManager = new HttpRequestsActionManager();
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
                var env = EnvironmentManager.Instance.EnvironmentType;
                if (env == EnvironmentType.Administration || env == EnvironmentType.Service)
                {
                    if (_itemNodes == null)
                    {
                        _itemNodes = new List<ItemNode>
                        {
                            // Top-level: Request Folders, with child Requests
                            new ItemNode(
                                FolderKindId,
                                Guid.Empty,
                                "Request Folder",
                                _folderIcon,
                                "Request Folders",
                                _folderIcon,
                                Category.Text,
                                true,
                                ItemsAllowed.Many,
                                new HttpFolderItemManager(FolderKindId),
                                new List<ItemNode>
                                {
                                    new ItemNode(
                                        RequestKindId,
                                        FolderKindId,
                                        "HTTP Request",
                                        _requestIcon,
                                        "HTTP Requests",
                                        _requestIcon,
                                        Category.Text,
                                        true,
                                        ItemsAllowed.Many,
                                        new HttpRequestItemManager(RequestKindId),
                                        null
                                    )
                                }
                            )
                        };
                    }
                    return _itemNodes;
                }
                return null;
            }
        }

        public override UserControl GenerateUserControl()
        {
            return new HtmlHelpUserControl(
                System.Reflection.Assembly.GetExecutingAssembly(), "Admin", "HelpPage.html");
        }

        public override List<BackgroundPlugin> BackgroundPlugins => _backgroundPlugins;

        public override ActionManager ActionManager => _actionManager;
    }
}
