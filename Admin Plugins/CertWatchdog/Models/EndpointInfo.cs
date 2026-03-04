using System;

namespace CertWatchdog.Models
{
    internal class EndpointInfo
    {
        public string Url { get; set; }
        public string ServiceType { get; set; }
        public Guid? SourceItemId { get; set; }
    }
}
