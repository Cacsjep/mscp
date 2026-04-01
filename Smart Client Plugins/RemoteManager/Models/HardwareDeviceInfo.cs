using System;

namespace RemoteManager.Models
{
    public class HardwareDeviceInfo
    {
        public string Name { get; set; }
        public string Address { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
        public string Model { get; set; }
        public bool HttpsEnabled { get; set; }
        public int HttpsPort { get; set; } = 443;
        public string RecordingServerName { get; set; }
        public Guid HardwareId { get; set; }
        public string HardwarePath { get; set; }
        public bool IsUserDefined { get; set; }

        public string WebUrl
        {
            get
            {
                if (string.IsNullOrEmpty(Address)) return null;
                try
                {
                    var parsed = new Uri(Address);
                    if (HttpsEnabled)
                    {
                        return HttpsPort == 443
                            ? $"https://{parsed.Host}"
                            : $"https://{parsed.Host}:{HttpsPort}";
                    }
                    return parsed.Port == 80
                        ? $"http://{parsed.Host}"
                        : $"http://{parsed.Host}:{parsed.Port}";
                }
                catch
                {
                    return Address;
                }
            }
        }

        public string IpAddress
        {
            get
            {
                try
                {
                    return new Uri(Address).Host;
                }
                catch
                {
                    return Address;
                }
            }
        }
    }
}
