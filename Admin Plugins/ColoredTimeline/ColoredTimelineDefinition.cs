using ColoredTimeline.Admin;
using ColoredTimeline.Background;
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

namespace ColoredTimeline
{
    public class ColoredTimelineDefinition : PluginDefinition
    {
        internal static readonly Guid PluginId = new Guid("5BA1BAC4-11A5-405F-AB86-5EC9D5A6A3DE");
        internal static readonly Guid TimelineRuleKindId = new Guid("E87D5697-C99E-4D4B-9EA8-4F617CD665A9");
        internal static readonly Guid SmartClientBackgroundPluginId = new Guid("7F50BD2C-BA15-4E04-9DE3-5EF1A5598FE7");

        private readonly List<BackgroundPlugin> _backgroundPlugins = new List<BackgroundPlugin>();
        private List<ItemNode> _itemNodes;

        private Image _pluginIcon = PluginIcon.FallbackIcon;
        private Image _itemIcon = PluginIcon.FallbackIcon;
        private static readonly PluginLog _log = new PluginLog("ColoredTimeline - PluginDefinition");

        public override Guid Id => PluginId;
        public override string Name => "Colored Timeline";
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
                    _pluginIcon = PluginIcon.Render(EFontAwesomeIcon.Solid_PaintBrush);
                    _itemIcon = PluginIcon.Render(EFontAwesomeIcon.Solid_Stream);
                    _log.Info("FontAwesome icons rendered");
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
                    catch (Exception ex2)
                    {
                        _log.Error($"Fallback to SDK icons also failed: {ex2.Message}");
                    }
                }

                _itemNodes = null;
            }

            if (env == EnvironmentType.SmartClient)
            {
                _log.Info("Registering ColoredTimelineSmartClientBackgroundPlugin");
                _backgroundPlugins.Add(new ColoredTimelineSmartClientBackgroundPlugin());
            }

            if (env == EnvironmentType.Administration)
            {
                EventTypeCache.StartLoad();
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
                                TimelineRuleKindId,
                                Guid.Empty,
                                "Timeline Rule",
                                _itemIcon,
                                "Timeline Rules",
                                _pluginIcon,
                                Category.VideoIn,
                                true,
                                ItemsAllowed.Many,
                                new TimelineRuleItemManager(TimelineRuleKindId),
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
    }
}
