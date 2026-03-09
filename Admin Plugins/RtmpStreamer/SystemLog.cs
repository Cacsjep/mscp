using System.Collections.Generic;
using CommunitySDK;
using VideoOS.Platform;
using VideoOS.Platform.Admin;
using VideoOS.Platform.Log;

namespace RTMPStreamer
{
    internal class SystemLog : SystemLogBase
    {
        public SystemLog(PluginLog log) : base("RTMPStreamer", log) { }

        protected override Dictionary<string, LogMessage> BuildMessages() => new Dictionary<string, LogMessage>
        {
            ["StreamConnected"] = new LogMessage
            {
                Id = "StreamConnected",
                Group = Group.System,
                Severity = Severity.Info,
                Status = Status.Success,
                RelatedObjectKind = Kind.Server,
                Category = Category.VideoOut.ToString(),
                CategoryName = "RTMP Streaming",
                Message = "RTMP stream '{p1}' is now streaming to {p2}"
            },
            ["StreamError"] = new LogMessage
            {
                Id = "StreamError",
                Group = Group.System,
                Severity = Severity.Error,
                Status = Status.Failure,
                RelatedObjectKind = Kind.Server,
                Category = Category.VideoOut.ToString(),
                CategoryName = "RTMP Streaming",
                Message = "RTMP stream '{p1}': {p2}"
            },
            ["StreamStopped"] = new LogMessage
            {
                Id = "StreamStopped",
                Group = Group.System,
                Severity = Severity.Info,
                Status = Status.StatusQuo,
                RelatedObjectKind = Kind.Server,
                Category = Category.VideoOut.ToString(),
                CategoryName = "RTMP Streaming",
                Message = "RTMP stream '{p1}' stopped"
            },
            ["HelperCrashed"] = new LogMessage
            {
                Id = "HelperCrashed",
                Group = Group.System,
                Severity = Severity.Warning,
                Status = Status.Failure,
                RelatedObjectKind = Kind.Server,
                Category = Category.VideoOut.ToString(),
                CategoryName = "RTMP Streaming",
                Message = "RTMP stream '{p1}' process crashed, restart #{p2}"
            },
        };

        public void StreamConnected(string streamName, string rtmpUrl) =>
            WriteEntry("StreamConnected", new Dictionary<string, string> { ["p1"] = streamName, ["p2"] = rtmpUrl });

        public void StreamError(string streamName, string error) =>
            WriteEntry("StreamError", new Dictionary<string, string> { ["p1"] = streamName, ["p2"] = error });

        public void StreamStopped(string streamName) =>
            WriteEntry("StreamStopped", new Dictionary<string, string> { ["p1"] = streamName });

        public void HelperCrashed(string streamName, int restartCount) =>
            WriteEntry("HelperCrashed", new Dictionary<string, string> { ["p1"] = streamName, ["p2"] = restartCount.ToString() });
    }
}
