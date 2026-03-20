using Auditor.Admin;
using Auditor.Client;
using CommunitySDK;
using FontAwesome5;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using VideoOS.Platform;
using VideoOS.Platform.Admin;
using VideoOS.Platform.Background;
using VideoOS.Platform.Client;

namespace Auditor
{
    public class AuditorDefinition : PluginDefinition
    {
        internal static readonly Guid PluginId = new Guid("B2C3D4E5-F6A7-4890-AB12-6EB000000001");
        internal static readonly Guid BackgroundPluginId = new Guid("B2C3D4E5-F6A7-4890-AB12-6EB000000005");
        internal static readonly Guid AuditRuleKindId = new Guid("B2C3D4E5-F6A7-4890-AB12-6EB000000010");
        internal static readonly Guid EventServerBgPluginId = new Guid("B2C3D4E5-F6A7-4890-AB12-6EB000000011");
        internal static readonly Guid EventGroupId = new Guid("B2C3D4E5-F6A7-4890-AB12-6EB000000020");
        internal static readonly Guid EvtPlaybackId = new Guid("B2C3D4E5-F6A7-4890-AB12-6EB000000021");
        internal static readonly Guid EvtExportId = new Guid("B2C3D4E5-F6A7-4890-AB12-6EB000000022");
        internal static readonly Guid EvtIndepPlaybackId = new Guid("B2C3D4E5-F6A7-4890-AB12-6EB000000023");
        internal static readonly Guid EvtPlaybackActionId = new Guid("B2C3D4E5-F6A7-4890-AB12-6EB000000024");
        internal static readonly Guid StateGroupId = new Guid("B2C3D4E5-F6A7-4890-AB12-6EB000000030");

        private List<BackgroundPlugin> _backgroundPlugins = new List<BackgroundPlugin>();
        private List<ItemNode> _itemNodes;

        private Image _pluginIcon = PluginIcon.FallbackIcon;
        private Image _folderIcon = PluginIcon.FallbackIcon;
        private static readonly PluginLog _log = new PluginLog("Auditor - PluginDefinition");

        public override Guid Id => PluginId;
        public override string Name => "Auditor";
        public override string Manufacturer => "https://github.com/Cacsjep";

        public override Image Icon => _pluginIcon ?? PluginIcon.FallbackIcon;

        public override void Init()
        {
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

            var env = EnvironmentManager.Instance.EnvironmentType;
            _log.Info($"PluginDefinition Init - environment: {env}");

            if (env != EnvironmentType.Service)
            {
                try
                {
                    _pluginIcon = PluginIcon.Render(EFontAwesomeIcon.Solid_ShieldAlt);
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
                _log.Info("Registering AuditEventServerPlugin (Event Server)");
                _backgroundPlugins.Add(new Background.AuditEventServerPlugin());
            }

            if (env == EnvironmentType.SmartClient)
            {
                _log.Info("Registering AuditorBackgroundPlugin (Smart Client)");
                _backgroundPlugins.Add(new AuditorBackgroundPlugin());
            }

            if (env == EnvironmentType.Administration)
            {
                _log.Info("Running in Administration environment - ItemNodes available");
            }
        }

        public override void Close()
        {
            _itemNodes = null;
            _backgroundPlugins.Clear();
        }

        private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            try { _log.Error($"Unhandled exception: {e.ExceptionObject}"); }
            catch { }
        }

        private static void OnUnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
        {
            e.SetObserved();
            try { _log.Error($"Unobserved task exception: {e.Exception}"); }
            catch { }
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
                                AuditRuleKindId,
                                Guid.Empty,
                                "Audit Rule",
                                _pluginIcon,
                                "Audit Rules",
                                _folderIcon,
                                Category.Text,
                                true,
                                ItemsAllowed.Many,
                                new AuditRuleItemManager(AuditRuleKindId),
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

        public override bool IncludeInExport => true;

        public override ExportManager GenerateExportManager(ExportParameters exportParameters)
        {
            return new AuditorExportManager(exportParameters);
        }

    }
}
