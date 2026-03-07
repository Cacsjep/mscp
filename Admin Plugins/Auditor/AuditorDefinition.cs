using Auditor.Admin;
using Auditor.Client;
using CommunitySDK;
using FontAwesome5;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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
        internal static readonly Guid StateGroupId = new Guid("B2C3D4E5-F6A7-4890-AB12-6EB000000030");

        private List<BackgroundPlugin> _backgroundPlugins = new List<BackgroundPlugin>();
        private List<ItemNode> _itemNodes;

        private Image _pluginIcon;
        private Image _folderIcon;
        private static readonly PluginLog _log = new PluginLog("Auditor - PluginDefinition");

        public override Guid Id => PluginId;
        public override string Name => "Auditor";
        public override string SharedNodeName => "Auditor";
        public override string VersionString => "1.0.0.0";
        public override string Manufacturer => "https://github.com/Cacsjep";

        public override Image Icon => _pluginIcon;

        public override void Init()
        {
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

            var env = EnvironmentManager.Instance.EnvironmentType;
            _log.Info($"PluginDefinition Init - environment: {env}");

            try
            {
                _pluginIcon = RenderFaIcon(EFontAwesomeIcon.Solid_ShieldAlt, 70, 130, 180);
                _folderIcon = RenderFaIcon(EFontAwesomeIcon.Solid_FolderOpen, 180, 150, 50);
                _log.Info("FontAwesome icons rendered");
            }
            catch (Exception ex)
            {
                _log.Error($"Failed to render FA icons, falling back to defaults: {ex.Message}");
                var images = VideoOS.Platform.UI.Util.ImageList.Images;
                _pluginIcon = images[VideoOS.Platform.UI.Util.PluginIx];
                _folderIcon = images[VideoOS.Platform.UI.Util.FolderIconIx];
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

        private static Image RenderFaIcon(EFontAwesomeIcon icon, byte r, byte g, byte b, int size = 24)
        {
            var awesome = new ImageAwesome
            {
                Icon = icon,
                Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(r, g, b)),
                Width = size,
                Height = size
            };
            awesome.Measure(new System.Windows.Size(size, size));
            awesome.Arrange(new System.Windows.Rect(0, 0, size, size));

            var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(awesome);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(rtb));
            // MemoryStream intentionally not disposed - Bitmap requires the stream to remain open
            var ms = new MemoryStream();
            encoder.Save(ms);
            ms.Position = 0;
            return new Bitmap(ms);
        }
    }
}
