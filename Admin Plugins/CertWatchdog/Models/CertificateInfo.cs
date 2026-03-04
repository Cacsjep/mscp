using System;

namespace CertWatchdog.Models
{
    public enum CertStatus
    {
        OK,
        Expiring,
        Critical,
        Expired,
        Error
    }

    [Serializable]
    public class CertificateInfo
    {
        public string ServiceType { get; set; }
        public string Endpoint { get; set; }
        public string Url { get; set; }
        public string Issuer { get; set; }
        public string Subject { get; set; }
        public DateTime NotAfter { get; set; }
        public int DaysLeft { get; set; }
        public CertStatus Status { get; set; }
        public string ErrorMessage { get; set; }
        public DateTime LastChecked { get; set; }
        public Guid? SourceItemId { get; set; }

        public static CertStatus ClassifyDaysLeft(int daysLeft)
        {
            if (daysLeft < 0) return CertStatus.Expired;
            if (daysLeft <= 15) return CertStatus.Critical;
            if (daysLeft <= 60) return CertStatus.Expiring;
            return CertStatus.OK;
        }
    }
}
