using System;

namespace Auditor.Messaging
{
    internal static class AuditMessageIds
    {
        public const string AuditEventReport = "Auditor.AuditEventReport";
    }

    [Serializable]
    public class AuditEventReport
    {
        public string EventType;
        public DateTime Timestamp;
        public string UserName;
        public string CameraName;
        public DateTime? PlaybackDate;
        public string[] CamerasInView;
        public string Reason;
        public string Details;
        public bool FireEvent;
    }
}
