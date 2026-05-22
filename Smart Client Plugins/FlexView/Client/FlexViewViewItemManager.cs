using VideoOS.Platform.Client;

namespace FlexView.Client
{
    public class FlexViewViewItemManager : ViewItemManager
    {
        private const string BackgroundColorPropertyKey = "BackgroundColor";

        public FlexViewViewItemManager() : base("FlexViewViewItemManager") { }

        public string BackgroundColor
        {
            get => GetProperty(BackgroundColorPropertyKey) ?? "#FF070809";
            set => SetProperty(BackgroundColorPropertyKey, value);
        }

        public void Save()
        {
            SaveProperties();
        }

        public override void PropertiesLoaded() { }

        public override ViewItemWpfUserControl GenerateViewItemWpfUserControl()
        {
            FlexViewDefinition.Log.Info("GenerateViewItemWpfUserControl called");
            return new FlexViewViewItemWpfUserControl(this);
        }

        public override PropertiesWpfUserControl GeneratePropertiesWpfUserControl()
        {
            return new FlexViewPropertiesWpfUserControl(this);
        }
    }
}
