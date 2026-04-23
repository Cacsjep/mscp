using System.Collections.Generic;
using CommunitySDK;
using VideoOS.Platform;
using VideoOS.Platform.Admin;
using VideoOS.Platform.Log;

namespace BarcodeReader
{
    internal class SystemLog : SystemLogBase
    {
        public SystemLog(PluginLog log) : base("BarcodeReader", log) { }

        protected override Dictionary<string, LogMessage> BuildMessages() => new Dictionary<string, LogMessage>
        {
            ["ChannelStarted"] = new LogMessage
            {
                Id = "ChannelStarted",
                Group = Group.System,
                Severity = Severity.Info,
                Status = Status.Success,
                RelatedObjectKind = Kind.Server,
                Category = Category.Server.ToString(),
                CategoryName = "Barcode Reader",
                Message = "Barcode channel '{p1}' started on camera {p2}"
            },
            ["ChannelError"] = new LogMessage
            {
                Id = "ChannelError",
                Group = Group.System,
                Severity = Severity.Error,
                Status = Status.Failure,
                RelatedObjectKind = Kind.Server,
                Category = Category.Server.ToString(),
                CategoryName = "Barcode Reader",
                Message = "Barcode channel '{p1}': {p2}"
            },
            ["ChannelStopped"] = new LogMessage
            {
                Id = "ChannelStopped",
                Group = Group.System,
                Severity = Severity.Info,
                Status = Status.StatusQuo,
                RelatedObjectKind = Kind.Server,
                Category = Category.Server.ToString(),
                CategoryName = "Barcode Reader",
                Message = "Barcode channel '{p1}' stopped"
            },
            ["HelperCrashed"] = new LogMessage
            {
                Id = "HelperCrashed",
                Group = Group.System,
                Severity = Severity.Warning,
                Status = Status.Failure,
                RelatedObjectKind = Kind.Server,
                Category = Category.Server.ToString(),
                CategoryName = "Barcode Reader",
                Message = "Barcode channel '{p1}' process crashed, restart #{p2}"
            },
        };

        public void ChannelStarted(string name, string camera) =>
            WriteEntry("ChannelStarted", new Dictionary<string, string> { ["p1"] = name, ["p2"] = camera });

        public void ChannelError(string name, string error) =>
            WriteEntry("ChannelError", new Dictionary<string, string> { ["p1"] = name, ["p2"] = error });

        public void ChannelStopped(string name) =>
            WriteEntry("ChannelStopped", new Dictionary<string, string> { ["p1"] = name });

        public void HelperCrashed(string name, int restartCount) =>
            WriteEntry("HelperCrashed", new Dictionary<string, string> { ["p1"] = name, ["p2"] = restartCount.ToString() });
    }
}
