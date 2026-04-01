using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Threading.Tasks;
using CommunitySDK;
using RemoteManager.Client;
using FontAwesome5;
using VideoOS.Platform;
using VideoOS.Platform.Client;
using VideoOS.Platform.UI.Controls;

namespace RemoteManager
{
    public class RemoteManagerDefinition : PluginDefinition
    {
        internal static readonly Guid PluginId = new Guid("50839541-C3AD-4B90-B361-C881189E9BB3");
        internal static readonly Guid ViewItemKind = new Guid("F2ADB66A-1210-43C0-AC6C-551161B0A931");
        internal static readonly Guid ViewItemPluginId = new Guid("9D96E454-0E17-4FAF-BBE9-B1E3A20EEDF6");
        internal static readonly Guid WorkspacePluginId = new Guid("8FC619A8-63DE-4F98-ACBD-E9AC74D115A6");

        private List<ViewItemPlugin> _viewItemPlugins = new List<ViewItemPlugin>();
        private List<WorkSpacePlugin> _workSpacePlugins = new List<WorkSpacePlugin>();

        private Image _pluginIcon;
        private static VideoOSIconSourceBase _pluginIconSource;

        static RemoteManagerDefinition()
        {
            try
            {
                _pluginIconSource = PluginIcon.RenderIconSource(EFontAwesomeIcon.Solid_NetworkWired);
            }
            catch { }
        }

        internal static VideoOSIconSourceBase PluginIconSource => _pluginIconSource;

        public override Guid Id => PluginId;
        public override string Name => "Remote Manager";
        public override string Manufacturer => "MSC Community Plugins";
        public override Image Icon => _pluginIcon;

        public override void Init()
        {
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

            try
            {
                _pluginIcon = PluginIcon.Render(EFontAwesomeIcon.Solid_NetworkWired);
            }
            catch
            {
                _pluginIcon = VideoOS.Platform.UI.Util.ImageList.Images[VideoOS.Platform.UI.Util.PluginIx];
            }

            if (EnvironmentManager.Instance.EnvironmentType == EnvironmentType.SmartClient)
            {
                _viewItemPlugins.Add(new RemoteManagerViewItemPlugin());
                _workSpacePlugins.Add(new RemoteManagerWorkspacePlugin());
            }
        }

        private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            try { Debug.WriteLine($"[RemoteManager] Unhandled exception: {e.ExceptionObject}"); } catch { }
        }

        private static void OnUnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
        {
            e.SetObserved();
            try { Debug.WriteLine($"[RemoteManager] Unobserved task exception: {e.Exception}"); } catch { }
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
