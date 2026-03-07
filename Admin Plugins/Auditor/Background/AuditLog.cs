using CommunitySDK;
using System.Collections.Generic;
using VideoOS.Platform;
using VideoOS.Platform.Admin;
using VideoOS.Platform.Log;

namespace Auditor.Background
{
    internal class AuditLog : SystemLogBase
    {

        public AuditLog(PluginLog log) : base("Auditor", "Auditor", log) { }

        protected override Dictionary<string, LogMessage> BuildMessages() => new Dictionary<string, LogMessage>
        {
            ["ExportMessage"] = new LogMessage
            {
                Id = "ExportMessage",
                Group = Group.Audit,
                Severity = Severity.Info,
                Status = Status.Success,
                RelatedObjectKind = Kind.User,
                Category = Category.Server.ToString(),
                CategoryName = "Auditor",
                Message = "User '{p1}' enter export because: {p2}"
            },
            ["PlaybackMessage"] = new LogMessage
            {
                Id = "PlaybackMessage",
                Group = Group.Audit,
                Severity = Severity.Info,
                Status = Status.Success,
                RelatedObjectKind = Kind.User,
                Category = Category.Server.ToString(),
                CategoryName = "Auditor",
                Message = "User '{p1}' enter playback because: {p2}"
            },
            ["IndependentPlaybackMessage"] = new LogMessage
            {
                Id = "IndependentPlaybackMessage",
                Group = Group.Audit,
                Severity = Severity.Info,
                Status = Status.Success,
                RelatedObjectKind = Kind.User,
                Category = Category.Server.ToString(),
                CategoryName = "Auditor",
                Message = "User '{p1}' enabled independent playback because {p2}"
            },
        };

        public void ExportMessage(string user, string reason) => WriteAuditEntry("ExportMessage", new Dictionary<string, string> { ["p1"] = user, ["p2"] = reason });
        public void PlaybackMessage(string user, string reason) => WriteAuditEntry("PlaybackMessage", new Dictionary<string, string> { ["p1"] = user, ["p2"] = reason });
        public void IndependentPlaybackMessage(string user, string reason) => WriteAuditEntry("IndependentPlaybackMessage", new Dictionary<string, string> { ["p1"] = user, ["p2"] = reason });
    }
}


