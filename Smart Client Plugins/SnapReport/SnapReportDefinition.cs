using SnapReport.Client;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Threading.Tasks;
using VideoOS.Platform;
using VideoOS.Platform.Client;
using VideoOS.Platform.UI.Controls;

namespace SnapReport
{
    public class SnapReportDefinition : PluginDefinition
    {
        internal static readonly Guid PluginId = new Guid("A1B2C3D4-E5F6-4789-90AB-5EA900000001");
        internal static readonly Guid ViewItemKind = new Guid("A1B2C3D4-E5F6-4789-90AB-5EA900000002");
        internal static readonly Guid ViewItemPluginId = new Guid("A1B2C3D4-E5F6-4789-90AB-5EA900000003");
        internal static readonly Guid WorkspacePluginId = new Guid("A1B2C3D4-E5F6-4789-90AB-5EA900000004");

        private List<ViewItemPlugin> _viewItemPlugins = new List<ViewItemPlugin>();
        private List<WorkSpacePlugin> _workSpacePlugins = new List<WorkSpacePlugin>();

        private Image _pluginIcon;
        private static VideoOSIconSourceBase _pluginIconSource;

        static SnapReportDefinition()
        {
            var packString = $"pack://application:,,,/{Assembly.GetExecutingAssembly().GetName().Name};component/Resources/PluginIcon.png";
            try
            {
                _pluginIconSource = new VideoOSIconUriSource { Uri = new Uri(packString) };
            }
            catch
            {
                // Icon not available in non-WPF environment
            }
        }

        internal static VideoOSIconSourceBase PluginIconSource => _pluginIconSource;

        public override Guid Id => PluginId;
        public override string Name => "SnapReport";
        public override string SharedNodeName => "SnapReport";
        public override string VersionString => "1.0.0.0";
        public override string Manufacturer => "https://github.com/Cacsjep";

        public override Image Icon => _pluginIcon;

        public override void Init()
        {
            // Prevent any unhandled exception from SnapReport from crashing Smart Client
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

            var images = VideoOS.Platform.UI.Util.ImageList.Images;
            _pluginIcon = images[VideoOS.Platform.UI.Util.PluginIx];

            if (EnvironmentManager.Instance.EnvironmentType == EnvironmentType.SmartClient)
            {
                _viewItemPlugins.Add(new SnapReportViewItemPlugin());
                _workSpacePlugins.Add(new SnapReportWorkspacePlugin());
            }
        }

        private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            try
            {
                Debug.WriteLine($"[SnapReport] Unhandled exception: {e.ExceptionObject}");
            }
            catch { }
        }

        private static void OnUnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
        {
            e.SetObserved();
            try
            {
                Debug.WriteLine($"[SnapReport] Unobserved task exception: {e.Exception}");
            }
            catch { }
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
