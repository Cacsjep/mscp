using VideoOS.Platform.Client;

namespace SystemStatus.Client
{
    /// <summary>
    /// Per-instance manager for the Folder &amp; Role view item. Holds one setting - whether to keep
    /// the leading recording-server segment in folder labels - and generates the display + properties
    /// controls.
    /// </summary>
    public class CameraUserStatusViewItemManager : ViewItemManager
    {
        private const string ShowServerPrefixKey = "ShowServerPrefix";

        public CameraUserStatusViewItemManager() : base("CameraUserStatusViewItemManager") { }

        /// <summary>"true"/"false" - when false the leading "server / " segment is dropped from folders.</summary>
        public string ShowServerPrefix
        {
            get => GetProperty(ShowServerPrefixKey) ?? "true";
            set => SetProperty(ShowServerPrefixKey, value);
        }

        public void Save() => SaveProperties();

        public override ViewItemWpfUserControl GenerateViewItemWpfUserControl()
        {
            return new CameraUserStatusViewItemWpfUserControl(this);
        }

        public override PropertiesWpfUserControl GeneratePropertiesWpfUserControl()
        {
            return new CameraUserStatusPropertiesWpfUserControl(this);
        }
    }
}
