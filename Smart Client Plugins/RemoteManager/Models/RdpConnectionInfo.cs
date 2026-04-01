using System;

namespace RemoteManager.Models
{
    public class RdpConnectionInfo
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public string Host { get; set; }
        public int Port { get; set; } = 3389;
        public string Username { get; set; }
        public string Password { get; set; }
        public bool EnableNLA { get; set; }
        public bool EnableClipboard { get; set; } = true;

        public string DisplayAddress
        {
            get
            {
                if (string.IsNullOrEmpty(Host)) return "";
                return Port != 3389 ? $"{Host}:{Port}" : Host;
            }
        }
    }
}
