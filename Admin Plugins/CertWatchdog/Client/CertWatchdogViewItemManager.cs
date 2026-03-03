using VideoOS.Platform.Client;

namespace CertWatchdog.Client
{
    public class CertWatchdogViewItemManager : ViewItemManager
    {
        public CertWatchdogViewItemManager()
            : base("CertWatchdogViewItemManager")
        {
        }

        public override void PropertiesLoaded()
        {
        }

        public override ViewItemWpfUserControl GenerateViewItemWpfUserControl()
        {
            return new CertWatchdogViewItemWpfUserControl();
        }
    }
}
