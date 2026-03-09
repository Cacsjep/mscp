using System.Collections.Generic;
using CommunitySDK;
using VideoOS.Platform;
using VideoOS.Platform.Admin;
using VideoOS.Platform.Log;

namespace HttpRequests
{
    internal class SystemLog : SystemLogBase
    {
        public SystemLog(PluginLog log) : base("HttpRequests", log) { }

        protected override Dictionary<string, LogMessage> BuildMessages() => new Dictionary<string, LogMessage>
        {
            ["RequestExecuted"] = new LogMessage
            {
                Id = "RequestExecuted",
                Group = Group.System,
                Severity = Severity.Info,
                Status = Status.Success,
                RelatedObjectKind = Kind.Server,
                Category = Category.Text.ToString(),
                CategoryName = "HTTP Requests",
                Message = "HTTP {p1} to '{p2}' returned {p3} in {p4}ms"
            },
            ["RequestFailed"] = new LogMessage
            {
                Id = "RequestFailed",
                Group = Group.System,
                Severity = Severity.Error,
                Status = Status.Failure,
                RelatedObjectKind = Kind.Server,
                Category = Category.Text.ToString(),
                CategoryName = "HTTP Requests",
                Message = "HTTP {p1} to '{p2}' failed: {p3}"
            },
        };

        public void RequestExecuted(string method, string url, int statusCode, long elapsedMs) =>
            WriteEntry("RequestExecuted", new Dictionary<string, string>
            {
                ["p1"] = method, ["p2"] = url,
                ["p3"] = statusCode.ToString(), ["p4"] = elapsedMs.ToString()
            });

        public void RequestFailed(string method, string url, string error) =>
            WriteEntry("RequestFailed", new Dictionary<string, string>
            {
                ["p1"] = method, ["p2"] = url, ["p3"] = error
            });
    }
}
