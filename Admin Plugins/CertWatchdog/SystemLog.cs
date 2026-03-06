using System.Collections.Generic;
using CommunitySDK;
using VideoOS.Platform;
using VideoOS.Platform.Admin;
using VideoOS.Platform.Log;

namespace CertWatchdog
{
    internal class SystemLog : SystemLogBase
    {
        public SystemLog(PluginLog log) : base("CertWatchdog", log) { }

        protected override Dictionary<string, LogMessage> BuildMessages() => new Dictionary<string, LogMessage>
        {
            ["PluginStarted"] = new LogMessage
            {
                Id = "PluginStarted",
                Group = Group.System,
                Severity = Severity.Info,
                Status = Status.Success,
                RelatedObjectKind = Kind.Server,
                Category = Category.VideoOut.ToString(),
                CategoryName = "Certificate Monitoring",
                Message = "Certificate Watchdog started, monitoring {p1} endpoint(s)"
            },
            ["PluginStopped"] = new LogMessage
            {
                Id = "PluginStopped",
                Group = Group.System,
                Severity = Severity.Info,
                Status = Status.StatusQuo,
                RelatedObjectKind = Kind.Server,
                Category = Category.VideoOut.ToString(),
                CategoryName = "Certificate Monitoring",
                Message = "Certificate Watchdog stopped"
            },
            ["CertExpiring"] = new LogMessage
            {
                Id = "CertExpiring",
                Group = Group.System,
                Severity = Severity.Warning,
                Status = Status.Failure,
                RelatedObjectKind = Kind.Server,
                Category = Category.VideoOut.ToString(),
                CategoryName = "Certificate Monitoring",
                Message = "Certificate for '{p1}' expires in {p2} days"
            },
            ["CertCheckComplete"] = new LogMessage
            {
                Id = "CertCheckComplete",
                Group = Group.System,
                Severity = Severity.Info,
                Status = Status.Success,
                RelatedObjectKind = Kind.Server,
                Category = Category.VideoOut.ToString(),
                CategoryName = "Certificate Monitoring",
                Message = "Certificate check complete: {p1} endpoints checked, {p2} expiring"
            },
            ["CertCheckError"] = new LogMessage
            {
                Id = "CertCheckError",
                Group = Group.System,
                Severity = Severity.Error,
                Status = Status.Failure,
                RelatedObjectKind = Kind.Server,
                Category = Category.VideoOut.ToString(),
                CategoryName = "Certificate Monitoring",
                Message = "Certificate check failed for '{p1}': {p2}"
            }
        };

        public void PluginStarted(int endpointCount) =>
            WriteEntry("PluginStarted", new Dictionary<string, string> { ["p1"] = endpointCount.ToString() });

        public void PluginStopped() =>
            WriteEntry("PluginStopped");

        public void CertExpiring(string endpoint, int daysLeft) =>
            WriteEntry("CertExpiring", new Dictionary<string, string> { ["p1"] = endpoint, ["p2"] = daysLeft.ToString() });

        public void CertCheckComplete(int totalChecked, int expiringCount) =>
            WriteEntry("CertCheckComplete", new Dictionary<string, string> { ["p1"] = totalChecked.ToString(), ["p2"] = expiringCount.ToString() });

        public void CertCheckError(string endpoint, string error) =>
            WriteEntry("CertCheckError", new Dictionary<string, string> { ["p1"] = endpoint, ["p2"] = error });
    }
}
