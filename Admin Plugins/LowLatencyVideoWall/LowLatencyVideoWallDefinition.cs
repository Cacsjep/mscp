using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunitySDK;
using FontAwesome5;
using LowLatencyVideoWall.Admin;
using LowLatencyVideoWall.Background;
using LowLatencyVideoWall.Client;
using VideoOS.Platform;
using VideoOS.Platform.Admin;
using VideoOS.Platform.Background;
using VideoOS.Platform.Client;
using VideoOS.Platform.UI.Controls;

namespace LowLatencyVideoWall
{
    /// <summary>
    /// Low Latency Video Wall.
    ///
    /// Pulls H.264 straight off Axis cameras over RTSP and renders it on the GPU, bypassing
    /// the Image Server protocol entirely. Camera addresses and credentials come from the
    /// Milestone configuration, so nothing has to be typed twice.
    ///
    /// The plugin loads into three environments and does something different in each:
    ///   Administration - the three configuration nodes
    ///   Service        - the Event Server side: the rule action that switches a wall
    ///   SmartClient    - the workspace that actually renders, and the message listener
    /// </summary>
    public class LowLatencyVideoWallDefinition : PluginDefinition
    {
        internal static readonly Guid PluginId = new Guid("63D24401-1CDA-4EDB-BE06-486568EEEDF4");

        // Configuration item kinds. These are the storage keys for everything the plugin owns.
        internal static readonly Guid ViewProfileKind = new Guid("518FB235-E649-4D7C-9FE3-5B76250F4D2A");
        internal static readonly Guid AssignmentKind = new Guid("14D0D3A4-468C-40FA-A78F-1310BDCDAB25");
        internal static readonly Guid SettingsKind = new Guid("BFD7C857-3613-46F3-95FF-CF3C2682639D");

        /// <summary>
        /// Numbered pane positions. A configuration item kind only because the rule wizard
        /// has no way to type a number, see WallPositionsItemManager.
        /// </summary>
        internal static readonly Guid WallPositionKind = new Guid("4898612D-284F-4510-AF0D-9DBF121B5592");

        // Management Client tree nodes.
        internal static readonly Guid OverviewNodeId = new Guid("186F23B2-0727-4926-B241-FB0670475D92");
        internal static readonly Guid ViewsNodeId = new Guid("EBC755FE-8231-4F0C-A193-3C4289D4A3CC");
        internal static readonly Guid AssignmentNodeId = new Guid("12164B10-C5DC-42B7-9ADD-867A4E5BFB22");
        internal static readonly Guid PositionsNodeId = new Guid("1EF44E25-AF8A-4F52-8CB1-63F69B890EFA");
        internal static readonly Guid TopNodeId = new Guid("29E06354-A5F4-44E2-9C31-D22127456B15");

        // Smart Client surfaces.
        internal static readonly Guid WorkspacePluginId = new Guid("66CCF43B-F03D-4323-B133-9381C192373F");
        internal static readonly Guid ViewItemPluginId = new Guid("14D44371-760C-41F1-BB94-112EC5018F83");
        internal static readonly Guid BackgroundPluginId = new Guid("F6B16BAA-12F1-4C9C-A2EF-C9A648D95BAA");

        // Rule actions.
        internal static readonly Guid SetWallViewActionId = new Guid("B8042297-5A06-40CC-90D2-2C665CC1E12B");
        internal static readonly Guid ClearWallActionId = new Guid("C3BE9925-E689-49C5-B46D-344328AA1EB1");
        internal static readonly Guid ShowCamerasActionId = new Guid("7EFA76A2-6B8F-43BA-B50E-52FED03842E2");
        internal static readonly Guid ShowCameraAtActionId = new Guid("441115CB-F578-43FC-AFF7-AC26B002BC9B");

        internal static readonly PluginLog Log = new PluginLog("LowLatencyVideoWall");

        /// <summary>Milestone blue, so the plugin reads as part of the product.</summary>
        private static readonly System.Windows.Media.Color IconColor =
            System.Windows.Media.Color.FromRgb(0x00, 0x99, 0xDA);

        private static VideoOSIconSourceBase _pluginIconSource;

        // ItemNode wants System.Drawing.Image, not the WPF icon source the Smart Client uses,
        // so the admin tree gets its own icons. FallbackIcon keeps the tree usable if
        // rendering fails, which it can on a server with no font stack.
        private static System.Drawing.Image _pluginTreeIcon = PluginIcon.FallbackIcon;
        private static System.Drawing.Image _overviewIcon = PluginIcon.FallbackIcon;
        private static System.Drawing.Image _viewIcon = PluginIcon.FallbackIcon;
        private static System.Drawing.Image _assignmentIcon = PluginIcon.FallbackIcon;
        private static System.Drawing.Image _positionIcon = PluginIcon.FallbackIcon;

        private readonly List<ItemNode> _itemNodes = new List<ItemNode>();
        private readonly List<ViewItemPlugin> _viewItemPlugins = new List<ViewItemPlugin>();
        private readonly List<WorkSpacePlugin> _workSpacePlugins = new List<WorkSpacePlugin>();
        private readonly List<BackgroundPlugin> _backgroundPlugins = new List<BackgroundPlugin>();

        static LowLatencyVideoWallDefinition()
        {
            try
            {
                _pluginIconSource = PluginIcon.RenderIconSource(EFontAwesomeIcon.Solid_Th, IconColor);
                _pluginTreeIcon = PluginIcon.Render(EFontAwesomeIcon.Solid_Th, IconColor, 24);
                _overviewIcon = PluginIcon.Render(EFontAwesomeIcon.Solid_Table, IconColor);
                _viewIcon = PluginIcon.Render(EFontAwesomeIcon.Solid_Th, IconColor);
                _assignmentIcon = PluginIcon.Render(EFontAwesomeIcon.Solid_UserCog, IconColor);
                _positionIcon = PluginIcon.Render(EFontAwesomeIcon.Solid_Hashtag, IconColor);
            }
            catch { }
        }

        internal static VideoOSIconSourceBase PluginIconSource => _pluginIconSource;

        public override Guid Id => PluginId;
        public override string Name => "Low Latency Video Wall";
        public override string Manufacturer => "MSC Community Plugins";

        /// <summary>
        /// The icon on the plugin's own root node in the Management Client tree. The SDK
        /// default here is the generic puzzle piece, so it is rendered instead, matching the
        /// three child nodes below it.
        /// </summary>
        public override System.Drawing.Image Icon => _pluginTreeIcon;

        public override void Init()
        {
            var env = EnvironmentManager.Instance.EnvironmentType;
            Log.Info($"PluginDefinition Init - environment: {env}");

            // Both environments, not just Administration. The Event Server is what evaluates
            // rules, so it has to know these item kinds too: a kind that exists only in the
            // Management Client leaves the rule wizard's target picker empty, which is exactly
            // what happened the first time this shipped. The managers' detail controls are
            // never asked for in Service, only their item lists.
            if (env == EnvironmentType.Administration || env == EnvironmentType.Service)
            {
                RegisterAdminNodes();
                _backgroundPlugins.Add(new WallEventServerPlugin());
            }

            if (env == EnvironmentType.SmartClient)
            {
                // Read before the first wall is built, so a pane never flashes the default
                // chrome and then changes its mind.
                Config.WallClientSettings.Load();

                _viewItemPlugins.Add(new WallViewItemPlugin());
                _workSpacePlugins.Add(new WallWorkspacePlugin());
                _backgroundPlugins.Add(new WallClientBackgroundPlugin());
                Log.Info("Registered Smart Client workspace, view item and background plugin");
            }
        }

        private void RegisterAdminNodes()
        {
            // Category is Text for all three, not Layout for the views. The SDK says the
            // category exists to filter what a picker dialog offers, and the rule wizard's
            // target picker is one of those dialogs, so an exotic category is a way to be
            // quietly left out of it.
            //
            // The first argument of an ItemNode is the configuration item KIND, not an
            // arbitrary node id. That is what makes the node a container of items of that
            // kind, and it is also what populates the item pickers in the rule wizard: the
            // rule action's two inputs are pickers constrained to ViewProfileKind and
            // AssignmentKind, so if these nodes do not expose those kinds with
            // ItemsAllowed.Many, both dropdowns come up empty.
            _itemNodes.Add(new ItemNode(
                OverviewNodeId, Guid.Empty,
                "Camera overview", _overviewIcon,
                "Camera overview", _overviewIcon,
                Category.Text, true, ItemsAllowed.None,
                new OverviewItemManager(), null));

            _itemNodes.Add(new ItemNode(
                ViewProfileKind, Guid.Empty,
                "Wall view", _viewIcon,
                "Wall views", _viewIcon,
                Category.Text, true, ItemsAllowed.Many,
                new ViewsItemManager(ViewProfileKind), null));

            _itemNodes.Add(new ItemNode(
                AssignmentKind, Guid.Empty,
                "Wall assignment", _assignmentIcon,
                "Wall assignments", _assignmentIcon,
                Category.Text, true, ItemsAllowed.Many,
                new AssignmentItemManager(AssignmentKind), null));

            // ItemsAllowed.None: the positions are generated, so there is nothing for an
            // administrator to add or remove. The node exists so the kind is registered and
            // the rule wizard can offer the numbers.
            _itemNodes.Add(new ItemNode(
                WallPositionKind, Guid.Empty,
                "Wall position", _positionIcon,
                "Wall positions", _positionIcon,
                Category.Text, true, ItemsAllowed.None,
                new WallPositionsItemManager(WallPositionKind), null));

            Log.Info($"Registered item nodes in {EnvironmentManager.Instance.EnvironmentType}");
        }

        public override void Close()
        {
            // The VAPIX layer pools one HttpClient per camera, so they have to be released
            // here or the sockets outlive the plugin.
            try { Vapix.VapixClient.Shutdown(); } catch { }

            _itemNodes.Clear();
            _viewItemPlugins.Clear();
            _workSpacePlugins.Clear();
            _backgroundPlugins.Clear();
        }

        /// <summary>
        /// The rule engine reads the action manager from the plugin definition, not from the
        /// background plugin, so the Event Server component exposes its singleton for us to
        /// hand over here. Without this the action never appears in the rule wizard.
        /// </summary>
        public override VideoOS.Platform.RuleAction.ActionManager ActionManager
            => WallEventServerPlugin.Actions;

        public override Collection<SettingsPanelPlugin> SettingsPanelPlugins
            => EnvironmentManager.Instance.EnvironmentType == EnvironmentType.SmartClient
                ? new Collection<SettingsPanelPlugin> { new WallSettingsPanel() }
                : new Collection<SettingsPanelPlugin>();

        public override List<ItemNode> ItemNodes => _itemNodes;
        public override List<ViewItemPlugin> ViewItemPlugins => _viewItemPlugins;
        public override List<WorkSpacePlugin> WorkSpacePlugins => _workSpacePlugins;
        public override List<BackgroundPlugin> BackgroundPlugins => _backgroundPlugins;
    }
}
