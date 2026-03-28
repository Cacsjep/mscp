using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Threading.Tasks;
using CommunitySDK;
using FontAwesome5;
using Timelapse.Client;
using VideoOS.Platform;
using VideoOS.Platform.Client;
using VideoOS.Platform.UI.Controls;

namespace Timelapse
{
    public class TimelapseDefinition : PluginDefinition
    {
        internal static readonly Guid PluginId = new Guid("71A3E5C0-B2D4-4F6E-90AB-5EA900100001");
        internal static readonly Guid ViewItemKind = new Guid("71A3E5C0-B2D4-4F6E-90AB-5EA900100002");
        internal static readonly Guid ViewItemPluginId = new Guid("71A3E5C0-B2D4-4F6E-90AB-5EA900100003");
        internal static readonly Guid WorkspacePluginId = new Guid("71A3E5C0-B2D4-4F6E-90AB-5EA900100004");

        private List<ViewItemPlugin> _viewItemPlugins = new List<ViewItemPlugin>();
        private List<WorkSpacePlugin> _workSpacePlugins = new List<WorkSpacePlugin>();

        private Image _pluginIcon;
        private static VideoOSIconSourceBase _pluginIconSource;

        static TimelapseDefinition()
        {
            try
            {
                _pluginIconSource = PluginIcon.RenderIconSource(EFontAwesomeIcon.Solid_Video);
            }
            catch { }
        }

        internal static VideoOSIconSourceBase PluginIconSource => _pluginIconSource;

        public override Guid Id => PluginId;
        public override string Name => "Timelapse";
        public override string Manufacturer => "https://github.com/Cacsjep";
        public override Image Icon => _pluginIcon;

        public override void Init()
        {
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

            try
            {
                _pluginIcon = PluginIcon.Render(EFontAwesomeIcon.Solid_Video);
            }
            catch
            {
                _pluginIcon = VideoOS.Platform.UI.Util.ImageList.Images[VideoOS.Platform.UI.Util.PluginIx];
            }

            if (EnvironmentManager.Instance.EnvironmentType == EnvironmentType.SmartClient)
            {
                _viewItemPlugins.Add(new TimelapseViewItemPlugin());
                _workSpacePlugins.Add(new TimelapseWorkspacePlugin());
            }
        }

        private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            try { Debug.WriteLine($"[Timelapse] Unhandled exception: {e.ExceptionObject}"); } catch { }
        }

        private static void OnUnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
        {
            e.SetObserved();
            try { Debug.WriteLine($"[Timelapse] Unobserved task exception: {e.Exception}"); } catch { }
        }

        public override void Close()
        {
            _viewItemPlugins.Clear();
            _workSpacePlugins.Clear();
        }

        public override List<ViewItemPlugin> ViewItemPlugins => _viewItemPlugins;
        public override List<WorkSpacePlugin> WorkSpacePlugins => _workSpacePlugins;
    }
}
