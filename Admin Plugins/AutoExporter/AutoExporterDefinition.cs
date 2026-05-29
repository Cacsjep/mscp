using CommunitySDK;
using FontAwesome5;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using AutoExporter.Admin;
using AutoExporter.Background;
using VideoOS.Platform;
using VideoOS.Platform.Admin;
using VideoOS.Platform.Background;
using VideoOS.Platform.RuleAction;

namespace AutoExporter
{
    public class AutoExporterDefinition : PluginDefinition
    {
        internal static readonly Guid PluginId             = new Guid("BB8298C5-8877-4073-BE24-68F67DE4694D");
        internal static readonly Guid ExecutionsKindId     = new Guid("453DF150-37CB-408B-B919-66EA1FCE4332");
        internal static readonly Guid JobKindId            = new Guid("1425AA67-4D80-42BA-8B1B-15FA60B3331C");
        internal static readonly Guid BackgroundPluginId   = new Guid("BA8ABBB6-7F34-477B-925C-B92FC20D8635");

        internal static readonly Guid EventGroupId         = new Guid("138E90D8-E2A2-4ACE-A28D-CC7EA7FCB447");
        internal static readonly Guid EvtJobStartedId      = new Guid("3F7C9A21-5D84-4B2E-9C16-7A0E2D6B4F58");
        internal static readonly Guid EvtJobSucceededId    = new Guid("A6314293-89AB-4855-8BC7-4C9E841AA2AD");
        internal static readonly Guid EvtJobFailedId       = new Guid("0C21BC12-B4D1-4CD8-A429-27021561CB3E");
        internal static readonly Guid StateGroupId         = new Guid("29CE359F-1E1F-46F9-976A-2CF5F6416BAE");

        // Fixed FQID.ObjectId for the singleton item so it round-trips deterministically.
        internal static readonly Guid ExecutionsSingletonId    = new Guid("CFAFE824-6071-45FB-8B26-510AB392702D");

        // Read/Edit permission pair per item kind, mirroring the PKI plugin. Surfaces
        // as Read + Edit checkboxes under Security > Roles > [Role] > MIP > Auto Exporter.
        private static readonly List<SecurityAction> KindSecurityActions = new List<SecurityAction>
        {
            new SecurityAction("GENERIC_READ",  "Read"),
            new SecurityAction("GENERIC_WRITE", "Edit"),
        };

        private List<BackgroundPlugin> _backgroundPlugins = new List<BackgroundPlugin>();
        private List<ItemNode> _itemNodes;
        private AutoExporterActionManager _actionManager;

        private Image _pluginIcon     = PluginIcon.FallbackIcon;
        private Image _executionsIcon = PluginIcon.FallbackIcon;
        private Image _statusIcon     = PluginIcon.FallbackIcon;
        private Image _folderIcon     = PluginIcon.FallbackIcon;
        private Image _jobIcon        = PluginIcon.FallbackIcon;

        private static readonly PluginLog _log = new PluginLog("AutoExporter - PluginDefinition");

        public override Guid Id => PluginId;
        public override string Name => "Auto Exporter";
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
                    _pluginIcon     = PluginIcon.Render(EFontAwesomeIcon.Solid_FileExport);
                    _executionsIcon = PluginIcon.Render(EFontAwesomeIcon.Solid_ListAlt);
                    _statusIcon     = PluginIcon.Render(EFontAwesomeIcon.Solid_TachometerAlt);
                    _folderIcon     = PluginIcon.Render(EFontAwesomeIcon.Solid_FileExport);
                    _jobIcon        = PluginIcon.Render(EFontAwesomeIcon.Solid_Clock);
                }
                catch (Exception ex)
                {
                    _log.Error($"Failed to render FA icons: {ex.Message}");
                    try
                    {
                        var images = VideoOS.Platform.UI.Util.ImageList.Images;
                        _pluginIcon     = images[VideoOS.Platform.UI.Util.PluginIx];
                        _executionsIcon = images[VideoOS.Platform.UI.Util.PluginIx];
                        _statusIcon     = images[VideoOS.Platform.UI.Util.PluginIx];
                        _folderIcon     = images[VideoOS.Platform.UI.Util.FolderIconIx];
                        _jobIcon        = images[VideoOS.Platform.UI.Util.PluginIx];
                    }
                    catch (Exception ex2)
                    {
                        _log.Error($"Fallback to SDK icons also failed: {ex2.Message}");
                    }
                }

                _itemNodes = null;
            }

            if (env == EnvironmentType.Service)
            {
                _log.Info("Registering AutoExporterBackgroundPlugin (Event Server)");
                _backgroundPlugins.Add(new AutoExporterBackgroundPlugin());
            }

            _actionManager = new AutoExporterActionManager();
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
                if (env != EnvironmentType.Administration && env != EnvironmentType.Service)
                    return null;

                if (_itemNodes != null) return _itemNodes;

                _itemNodes = new List<ItemNode>
                {
                    // Status + Executions are merged into one page (two tables).
                    new ItemNode(
                        ExecutionsKindId, Guid.Empty,
                        "Status and Executions", _executionsIcon,
                        "Status and Executions", _executionsIcon,
                        Category.Text, true, ItemsAllowed.One,
                        new ExecutionsItemManager(ExecutionsKindId),
                        null,
                        new List<SecurityAction>(KindSecurityActions)),

                    new ItemNode(
                        JobKindId, Guid.Empty,
                        "Job", _folderIcon,
                        "Jobs", _folderIcon,
                        Category.Text, true, ItemsAllowed.Many,
                        new JobItemManager(JobKindId),
                        null,
                        new List<SecurityAction>(KindSecurityActions))
                };
                return _itemNodes;
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
