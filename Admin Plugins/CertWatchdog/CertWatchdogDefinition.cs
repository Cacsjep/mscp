using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using CertWatchdog.Admin;
using CertWatchdog.Client;
using VideoOS.Platform;
using VideoOS.Platform.Admin;
using VideoOS.Platform.Background;
using VideoOS.Platform.Client;
using VideoOS.Platform.UI.Controls;

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

        private Image _pluginIcon;
        private Image _folderIcon;
        private static VideoOSIconSourceBase _pluginIconSource;

        static CertWatchdogDefinition()
        {
            var packString = $"pack://application:,,,/{Assembly.GetExecutingAssembly().GetName().Name};component/Resources/PluginIcon.png";
            try
            {
                _pluginIconSource = new VideoOSIconUriSource { Uri = new Uri(packString) };
            }
            catch
            {
                // Icon not available in Service environment
            }
        }

        internal static VideoOSIconSourceBase PluginIconSource => _pluginIconSource;

        public override Guid Id => PluginId;
        public override string Name => "Certificate Watchdog";
        public override string SharedNodeName => "Certificate Watchdog";
        public override string VersionString => "1.0.0.0";
        public override string Manufacturer => "https://github.com/Cacsjep";

        public override Image Icon => _pluginIcon;

        public override void Init()
        {
            var images = VideoOS.Platform.UI.Util.ImageList.Images;
            _pluginIcon = images[VideoOS.Platform.UI.Util.PluginIx];
            _folderIcon = images[VideoOS.Platform.UI.Util.FolderIconIx];

            var env = EnvironmentManager.Instance.EnvironmentType;

            if (env == EnvironmentType.Service)
            {
                _backgroundPlugins.Add(new Background.CertWatchdogBackgroundPlugin());
            }

            if (env == EnvironmentType.SmartClient)
            {
                _viewItemPlugins.Add(new CertWatchdogViewItemPlugin());
                _workSpacePlugins.Add(new CertWatchdogWorkspacePlugin());
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
            if (EnvironmentManager.Instance.EnvironmentType == EnvironmentType.Administration)
            {
                return new CertWatchdogAdminUserControl();
            }
            return null;
        }

        public override List<BackgroundPlugin> BackgroundPlugins => _backgroundPlugins;
        public override List<ViewItemPlugin> ViewItemPlugins => _viewItemPlugins;
        public override List<WorkSpacePlugin> WorkSpacePlugins => _workSpacePlugins;
    }
}
