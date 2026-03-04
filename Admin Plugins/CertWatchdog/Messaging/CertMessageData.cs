using System;
using System.Collections.Generic;
using CertWatchdog.Models;

namespace CertWatchdog.Messaging
{
    internal static class CertMessageIds
    {
        public const string CertDataRequest = "CertWatchdog.CertDataRequest";
        public const string CertDataResponse = "CertWatchdog.CertDataResponse";
        public const string CertRecollectRequest = "CertWatchdog.CertRecollectRequest";
    }

    [Serializable]
    public class CertDataRequest
    {
        public Guid RequestId;
    }

    [Serializable]
    public class CertDataResponse
    {
        public Guid RequestId;
        public List<CertificateInfo> Certificates;
        public DateTime Timestamp;
    }
}
