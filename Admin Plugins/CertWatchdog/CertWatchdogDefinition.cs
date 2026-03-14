using CertWatchdog.Admin;
using CertWatchdog.Client;
using CommunitySDK;
using FontAwesome5;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using VideoOS.Platform;
using VideoOS.Platform.Admin;
using VideoOS.Platform.Background;
using VideoOS.Platform.Client;
using VideoOS.Platform.UI.Controls;
using VideoOS.Platform.Util;

namespace CertWatchdog
{
    public class CertWatchdogDefinition : PluginDefinition
    {
        // GUIDs
        internal static readonly Guid PluginId = new Guid("D1C2E3F4-A5B6-4789-90AB-CDEF01234567");
        internal static readonly Guid CertWatchdogKindId = new Guid("D1C2E3F4-A5B6-4789-90AB-CDEF01234568");
        internal static readonly Guid BackgroundPluginId = new Guid("D1C2E3F4-A5B6-4789-90AB-CDEF01234569");
        internal static readonly Guid ViewItemPluginId = new Guid("D1C2E3F4-A5B6-4789-90AB-CDEF0123456A");
        internal static readonly Guid ViewItemKind = new Guid("D1C2E3F4-A5B6-4789-90AB-CDEF0123456B");
        internal static readonly Guid WorkspacePluginId = new Guid("D1C2E3F4-A5B6-4789-90AB-CDEF0123456C");
        internal static readonly Guid EventGroupId = new Guid("D1C2E3F4-A5B6-4789-90AB-CDEF0123456D");
        internal static readonly Guid EventType60DaysId = new Guid("D1C2E3F4-A5B6-4789-90AB-CDEF0123456E");
        internal static readonly Guid EventType30DaysId = new Guid("D1C2E3F4-A5B6-4789-90AB-CDEF0123456F");
        internal static readonly Guid EventType15DaysId = new Guid("D1C2E3F4-A5B6-4789-90AB-CDEF01234570");
        internal static readonly Guid StateGroupId = new Guid("D1C2E3F4-A5B6-4789-90AB-CDEF01234571");

        // Device certificate events (source = camera/hardware)
        internal static readonly Guid DeviceEventGroupId = new Guid("D1C2E3F4-A5B6-4789-90AB-CDEF01234572");
        internal static readonly Guid DeviceEventType60DaysId = new Guid("D1C2E3F4-A5B6-4789-90AB-CDEF01234573");
        internal static readonly Guid DeviceEventType30DaysId = new Guid("D1C2E3F4-A5B6-4789-90AB-CDEF01234574");
        internal static readonly Guid DeviceEventType15DaysId = new Guid("D1C2E3F4-A5B6-4789-90AB-CDEF01234575");

        private List<BackgroundPlugin> _backgroundPlugins = new List<BackgroundPlugin>();
        private List<ViewItemPlugin> _viewItemPlugins = new List<ViewItemPlugin>();
        private List<WorkSpacePlugin> _workSpacePlugins = new List<WorkSpacePlugin>();
        private List<ItemNode> _itemNodes;

        private readonly List<SecurityAction> _securityActions = new List<SecurityAction>
        {
            new SecurityAction("GENERIC_READ", "Read"),
        };

        private Image _pluginIcon = PluginIcon.FallbackIcon;
        private Image _folderIcon = PluginIcon.FallbackIcon;
        private static VideoOSIconSourceBase _pluginIconSource;
        private static readonly PluginLog _log = new PluginLog("CertWatchdog - PluginDefinition");

        static CertWatchdogDefinition()
        {
            try
            {
                _pluginIconSource = PluginIcon.RenderIconSource(EFontAwesomeIcon.Solid_Certificate);
            }
            catch
            {
                // Icon not available in Service environment
            }
        }

        internal static VideoOSIconSourceBase PluginIconSource => _pluginIconSource;

        public override Guid Id => PluginId;
        public override string Name => "Certificate Watchdog";
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
                    _pluginIcon = PluginIcon.Render(EFontAwesomeIcon.Solid_Certificate);
                    _folderIcon = PluginIcon.Render(EFontAwesomeIcon.Solid_FolderOpen);
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
                _backgroundPlugins.Add(new Background.CertWatchdogBackgroundPlugin());
            }

            if (env == EnvironmentType.SmartClient)
            {
                if (HasReadPermission())
                {
                    _viewItemPlugins.Add(new CertWatchdogViewItemPlugin());
                    _workSpacePlugins.Add(new CertWatchdogWorkspacePlugin());
                }
            }
        }

        public override void Close()
        {
            _itemNodes = null;
            _backgroundPlugins.Clear();
            _viewItemPlugins.Clear();
            _workSpacePlugins.Clear();
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
                            new ItemNode(
                                CertWatchdogKindId,
                                Guid.Empty,
                                "Certificate Watchdog",
                                _pluginIcon,
                                "Certificate Watchdog",
                                _folderIcon,
                                Category.Text,
                                true,
                                ItemsAllowed.One,
                                new CertWatchdogItemManager(CertWatchdogKindId),
                                null
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
        public override List<ViewItemPlugin> ViewItemPlugins => _viewItemPlugins;
        public override List<WorkSpacePlugin> WorkSpacePlugins => _workSpacePlugins;

        public override List<SecurityAction> SecurityActions => _securityActions;

        private static bool HasReadPermission()
        {
            try
            {
                SecurityAccess.CheckPermission(PluginId, "GENERIC_READ");
                return true;
            }
            catch (NotAuthorizedMIPException)
            {
                return false;
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(ex.ToString());
                return false;
            }
        }
    }
}
