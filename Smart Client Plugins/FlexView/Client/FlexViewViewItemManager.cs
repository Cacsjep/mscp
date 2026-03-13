using VideoOS.Platform.Client;

namespace FlexView.Client
{
    public class FlexViewViewItemManager : ViewItemManager
    {
        public FlexViewViewItemManager() : base("FlexViewViewItemManager") { }

        public override void PropertiesLoaded() { }

        public override ViewItemWpfUserControl GenerateViewItemWpfUserControl()
        {
            FlexViewDefinition.Log.Info("GenerateViewItemWpfUserControl called");
            return new FlexViewViewItemWpfUserControl();
        }
    }
}
