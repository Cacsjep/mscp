using System.Collections.Generic;
using CommunitySDK;
using VideoOS.Platform;
using VideoOS.Platform.Admin;
using VideoOS.Platform.Log;

namespace AutoExporter
{
    internal class SystemLog : SystemLogBase
    {
        public SystemLog(PluginLog log) : base("AutoExporter", log) { }

        protected override Dictionary<string, LogMessage> BuildMessages() => new Dictionary<string, LogMessage>
        {
            ["JobSucceeded"] = new LogMessage
            {
                Id = "JobSucceeded",
                Group = Group.System,
                Severity = Severity.Info,
                Status = Status.Success,
                RelatedObjectKind = Kind.Server,
                Category = Category.Text.ToString(),
                CategoryName = "Auto Exporter",
                Message = "Job '{p1}' exported {p2} cameras ({p3} MB) in {p4}s"
            },
            ["JobFailed"] = new LogMessage
            {
                Id = "JobFailed",
                Group = Group.System,
                Severity = Severity.Error,
                Status = Status.Failure,
                RelatedObjectKind = Kind.Server,
                Category = Category.Text.ToString(),
                CategoryName = "Auto Exporter",
                Message = "Job '{p1}' failed: {p2}"
            },
            ["JobSkippedBusy"] = new LogMessage
            {
                Id = "JobSkippedBusy",
                Group = Group.System,
                Severity = Severity.Warning,
                Status = Status.Failure,
                RelatedObjectKind = Kind.Server,
                Category = Category.Text.ToString(),
                CategoryName = "Auto Exporter",
                Message = "Job '{p1}' skipped: previous run still in progress"
            },
            ["RingCleanup"] = new LogMessage
            {
                Id = "RingCleanup",
                Group = Group.System,
                Severity = Severity.Info,
                Status = Status.Success,
                RelatedObjectKind = Kind.Server,
                Category = Category.Text.ToString(),
                CategoryName = "Auto Exporter",
                Message = "Ring cleanup: pruned {p1} run folders ({p2} MB) under '{p3}'"
            },
        };

        public void JobSucceeded(string jobName, int cameraCount, long mbWritten, double seconds) =>
            WriteEntry("JobSucceeded", new Dictionary<string, string>
            {
                ["p1"] = jobName, ["p2"] = cameraCount.ToString(),
                ["p3"] = mbWritten.ToString(), ["p4"] = seconds.ToString("0.0")
            });

        public void JobFailed(string jobName, string error) =>
            WriteEntry("JobFailed", new Dictionary<string, string>
            {
                ["p1"] = jobName, ["p2"] = error
            });

        public void JobSkippedBusy(string jobName) =>
            WriteEntry("JobSkippedBusy", new Dictionary<string, string>
            {
                ["p1"] = jobName
            });

        public void RingCleanup(int prunedFolders, long mbReclaimed, string storagePath) =>
            WriteEntry("RingCleanup", new Dictionary<string, string>
            {
                ["p1"] = prunedFolders.ToString(),
                ["p2"] = mbReclaimed.ToString(),
                ["p3"] = storagePath
            });
    }
}
