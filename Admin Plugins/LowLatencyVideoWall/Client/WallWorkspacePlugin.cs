using System;
using System.Drawing;
using VideoOS.Platform.Client;

namespace LowLatencyVideoWall.Client
{
    /// <summary>
    /// The wall workspace. One full size slot holding the wall view item, because the wall
    /// draws all of its panes itself into a single swapchain rather than letting the Smart
    /// Client lay out one view item per camera.
    /// </summary>
    public class WallWorkspacePlugin : WorkSpacePlugin
    {
        public override Guid Id => LowLatencyVideoWallDefinition.WorkspacePluginId;
        public override string Name => "Low Latency Video Wall";

        public override void Init()
        {
            try
            {
                ViewAndLayoutItem.Layout = new[] { new Rectangle(0, 0, 1000, 1000) };
                ViewAndLayoutItem.InsertViewItemPlugin(0, new WallViewItemPlugin(), null);
                LowLatencyVideoWallDefinition.Log.Info("Wall workspace initialised");
            }
            catch (Exception ex)
            {
                LowLatencyVideoWallDefinition.Log.Error($"Wall workspace init failed: {ex}");
            }
        }

        public override void Close()
        {
        }
    }
}
