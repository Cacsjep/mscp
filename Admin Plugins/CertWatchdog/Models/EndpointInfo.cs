using System;

namespace CertWatchdog.Models
{
    internal class EndpointInfo
    {
        public string Url { get; set; }
        public string ServiceType { get; set; }
        public Guid? SourceItemId { get; set; }

        /// <summary>Secondary URL tried when the primary yields no certificate (e.g. failover service port 8990).</summary>
        public string FallbackUrl { get; set; }
    }
}
