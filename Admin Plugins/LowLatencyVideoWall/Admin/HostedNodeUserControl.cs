using System;
using System.Windows.Forms;
using System.Windows.Forms.Integration;
using VideoOS.Platform;
using VideoOS.Platform.Admin;

namespace LowLatencyVideoWall.Admin
{
    /// <summary>
    /// Hosts a WPF page inside an ItemNodeUserControl, which is the only type the Management
    /// Client accepts from ItemManager.GenerateOverviewUserControl, and which also satisfies
    /// GenerateDetailUserControl because it is itself a WinForms UserControl. Every page in
    /// this plugin is WPF, so every page arrives through here.
    ///
    /// Dock = Fill on both levels because the client just drops the returned control into
    /// its detail pane without imposing a layout, and a stock ItemNodeUserControl defaults
    /// to roughly 150x150.
    /// </summary>
    internal sealed class HostedNodeUserControl : ItemNodeUserControl
    {
        private readonly System.Windows.Controls.UserControl _inner;

        internal HostedNodeUserControl(System.Windows.Controls.UserControl inner)
        {
            Dock = DockStyle.Fill;
            AutoScroll = false;
            _inner = inner;

            Controls.Add(new ElementHost
            {
                Dock = DockStyle.Fill,
                Child = _inner
            });
        }

        internal System.Windows.Controls.UserControl Inner => _inner;

        public override void Init(Item item)
        {
            try
            {
                (_inner as IAdminPage)?.Activate();
            }
            catch (Exception ex)
            {
                LowLatencyVideoWallDefinition.Log.Error($"Activating admin page failed: {ex.Message}");
            }
        }

        public override void Close()
        {
            try
            {
                (_inner as IAdminPage)?.Deactivate();
            }
            catch (Exception ex)
            {
                LowLatencyVideoWallDefinition.Log.Error($"Closing admin page failed: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Lets the node wrapper tell a page when it becomes visible and when it goes away, so a
    /// page can load lazily and cancel its background work instead of reading the whole site
    /// again every time the admin clicks through the tree.
    /// </summary>
    internal interface IAdminPage
    {
        void Activate();
        void Deactivate();
    }
}
