using VideoOS.Platform.Client;

namespace Timelapse.Client
{
    public class TimelapseViewItemManager : ViewItemManager
    {
        public TimelapseViewItemManager()
            : base("TimelapseViewItemManager")
        {
        }

        public override void PropertiesLoaded() { }

        public override ViewItemWpfUserControl GenerateViewItemWpfUserControl()
            => new TimelapseViewItemWpfUserControl();
    }
}
