using CommunitySDK;
using System.Collections.Generic;
using System.Reflection;
using VideoOS.Platform;
using VideoOS.Platform.Admin;
using VideoOS.Platform.Log;

namespace Auditor.Background
{
    internal class AuditLog : SystemLogBase
    {
        private static readonly PropertyInfo _categoryNameProp =
            typeof(LogMessage).GetProperty("CategoryName");

        public AuditLog(PluginLog log) : base("Auditor", log) { }

        private static LogMessage CreateMessage(string id, string message)
        {
            var msg = new LogMessage
            {
                Id = id,
                Group = Group.Audit,
                Severity = Severity.Info,
                Status = Status.Success,
                RelatedObjectKind = Kind.User,
                Category = Category.Server.ToString(),
                Message = message
            };
            _categoryNameProp?.SetValue(msg, "Auditor");
            return msg;
        }

        protected override Dictionary<string, LogMessage> BuildMessages() => new Dictionary<string, LogMessage>
        {
            ["ExportMessage"] = CreateMessage("ExportMessage", "User '{p1}' enter export because: {p2}"),
            ["PlaybackMessage"] = CreateMessage("PlaybackMessage", "User '{p1}' enter playback because: {p2}"),
            ["IndependentPlaybackMessage"] = CreateMessage("IndependentPlaybackMessage", "User '{p1}' enabled independent playback because {p2}"),
            ["PlaybackActionMessage"] = CreateMessage("PlaybackActionMessage", "User '{p1}' playback action '{p2}' at recording time {p3} | {p4}"),
        };

        public void ExportMessage(string user, string reason) => WriteAuditEntry("ExportMessage", new Dictionary<string, string> { ["p1"] = user, ["p2"] = reason });
        public void PlaybackMessage(string user, string reason) => WriteAuditEntry("PlaybackMessage", new Dictionary<string, string> { ["p1"] = user, ["p2"] = reason });
        public void IndependentPlaybackMessage(string user, string reason) => WriteAuditEntry("IndependentPlaybackMessage", new Dictionary<string, string> { ["p1"] = user, ["p2"] = reason });
        public void PlaybackActionMessage(string user, string action, string recordingTime, string cameras) => WriteAuditEntry("PlaybackActionMessage", new Dictionary<string, string> { ["p1"] = user, ["p2"] = action, ["p3"] = recordingTime, ["p4"] = cameras });
    }
}


