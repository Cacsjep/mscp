using VideoOS.Platform.Client;

namespace LowLatencyVideoWall.Client
{
    public class WallViewItemManager : ViewItemManager
    {
        public WallViewItemManager() : base("WallViewItemManager") { }

        public override void PropertiesLoaded() { }

        public override ViewItemWpfUserControl GenerateViewItemWpfUserControl()
            => new WallViewItemWpfUserControl();
    }
}
