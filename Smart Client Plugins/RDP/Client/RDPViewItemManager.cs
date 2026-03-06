using VideoOS.Platform.Client;

namespace RDP.Client
{
    public class RDPViewItemManager : ViewItemManager
    {
        private const string ConnectionNamePropertyKey = "ConnectionName";
        private const string IPAddressPropertyKey = "IPAddress";
        private const string UsernamePropertyKey = "Username";
        private const string EnableNlaPropertyKey = "EnableNLA";
        private const string EnableClipboardPropertyKey = "EnableClipboard";
        private const string PortPropertyKey = "Port";

        public RDPViewItemManager()
            : base("RDPViewItemManager")
        {
        }

        public string ConnectionName
        {
            get => GetProperty(ConnectionNamePropertyKey) ?? string.Empty;
            set => SetProperty(ConnectionNamePropertyKey, value);
        }

        public string IPAddress
        {
            get => GetProperty(IPAddressPropertyKey) ?? string.Empty;
            set => SetProperty(IPAddressPropertyKey, value);
        }

        public string Username
        {
            get => GetProperty(UsernamePropertyKey) ?? string.Empty;
            set => SetProperty(UsernamePropertyKey, value);
        }

        public bool EnableNLA
        {
            get => (GetProperty(EnableNlaPropertyKey) ?? "False") == "True";
            set => SetProperty(EnableNlaPropertyKey, value ? "True" : "False");
        }

        public bool EnableClipboard
        {
            get => (GetProperty(EnableClipboardPropertyKey) ?? "True") == "True";
            set => SetProperty(EnableClipboardPropertyKey, value ? "True" : "False");
        }

        public int Port
        {
            get
            {
                var s = GetProperty(PortPropertyKey);

                if (int.TryParse(s, out var port))
                    return port;

                return 3389;
            }
            set => SetProperty(PortPropertyKey, value.ToString());
        }

        public void Save()
        {
            SaveProperties();
        }

        public override void PropertiesLoaded()
        {
        }

        public override ViewItemWpfUserControl GenerateViewItemWpfUserControl()
        {
            return new RDPViewItemWpfUserControl(this);
        }

        public override PropertiesWpfUserControl GeneratePropertiesWpfUserControl()
        {
            return new RDPPropertiesWpfUserControl(this);
        }
    }
}
