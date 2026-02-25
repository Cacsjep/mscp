using System.Collections.Generic;
using VideoOS.Platform;
using VideoOS.Platform.Admin;
using VideoOS.Platform.Log;

namespace RtmpStreamer
{
    /// <summary>
    /// Writes entries to the Milestone System Log visible in Management Client.
    /// Uses LogClient API with pre-registered message templates.
    /// </summary>
    internal static class SystemLog
    {
        private const string AppId = "RtmpStreamer";
        private const string ComponentId = "RtmpStreamer";
        private const string Version = "1.0";
        private const string Culture = "en-US";
        private const string ResourceType = "text";

        // Message IDs - keep stable, they identify log entries on the server
        private const string MsgStreamConnected = "StreamConnected";
        private const string MsgStreamError = "StreamError";
        private const string MsgStreamStopped = "StreamStopped";
        private const string MsgHelperCrashed = "HelperCrashed";
        private const string MsgPluginStarted = "PluginStarted";
        private const string MsgPluginStopped = "PluginStopped";

        private static bool _registered;

        public static void Register()
        {
            if (_registered) return;

            try
            {
                var messages = new Dictionary<string, LogMessage>
                {
                    [MsgStreamConnected] = new LogMessage
                    {
                        Id = MsgStreamConnected,
                        Group = Group.System,
                        Severity = Severity.Info,
                        Status = Status.Success,
                        RelatedObjectKind = Kind.Server,
                        Category = Category.VideoOut.ToString(),
                        CategoryName = "RTMP Streaming",
                        Message = "RTMP stream '{p1}' is now streaming to {p2}"
                    },
                    [MsgStreamError] = new LogMessage
                    {
                        Id = MsgStreamError,
                        Group = Group.System,
                        Severity = Severity.Error,
                        Status = Status.Failure,
                        RelatedObjectKind = Kind.Server,
                        Category = Category.VideoOut.ToString(),
                        CategoryName = "RTMP Streaming",
                        Message = "RTMP stream '{p1}': {p2}"
                    },
                    [MsgStreamStopped] = new LogMessage
                    {
                        Id = MsgStreamStopped,
                        Group = Group.System,
                        Severity = Severity.Info,
                        Status = Status.StatusQuo,
                        RelatedObjectKind = Kind.Server,
                        Category = Category.VideoOut.ToString(),
                        CategoryName = "RTMP Streaming",
                        Message = "RTMP stream '{p1}' stopped"
                    },
                    [MsgHelperCrashed] = new LogMessage
                    {
                        Id = MsgHelperCrashed,
                        Group = Group.System,
                        Severity = Severity.Warning,
                        Status = Status.Failure,
                        RelatedObjectKind = Kind.Server,
                        Category = Category.VideoOut.ToString(),
                        CategoryName = "RTMP Streaming",
                        Message = "RTMP stream '{p1}' process crashed, restart #{p2}"
                    },
                    [MsgPluginStarted] = new LogMessage
                    {
                        Id = MsgPluginStarted,
                        Group = Group.System,
                        Severity = Severity.Info,
                        Status = Status.Success,
                        RelatedObjectKind = Kind.Server,
                        Category = Category.VideoOut.ToString(),
                        CategoryName = "RTMP Streaming",
                        Message = "RTMP Streamer started with {p1} active stream(s)"
                    },
                    [MsgPluginStopped] = new LogMessage
                    {
                        Id = MsgPluginStopped,
                        Group = Group.System,
                        Severity = Severity.Info,
                        Status = Status.StatusQuo,
                        RelatedObjectKind = Kind.Server,
                        Category = Category.VideoOut.ToString(),
                        CategoryName = "RTMP Streaming",
                        Message = "RTMP Streamer stopped"
                    }
                };

                var dict = new LogMessageDictionary(
                    culture: Culture,
                    version: Version,
                    application: AppId,
                    component: ComponentId,
                    logMessages: messages,
                    resourceType: ResourceType);

                LogClient.Instance.RegisterDictionary(dict);
                LogClient.Instance.SetCulture(Culture);

                _registered = true;
            }
            catch (System.Exception ex)
            {
                PluginLog.Error($"Failed to register system log dictionary: {ex.Message}");
            }
        }

        private static Item GetSiteItem()
        {
            try
            {
                return EnvironmentManager.Instance.GetSiteItem(
                    EnvironmentManager.Instance.MasterSite);
            }
            catch { return null; }
        }

        public static void StreamConnected(string streamName, string rtmpUrl)
        {
            if (!_registered) return;
            try
            {
                LogClient.Instance.NewEntry(AppId, ComponentId, MsgStreamConnected,
                    GetSiteItem(),
                    new Dictionary<string, string> { ["p1"] = streamName, ["p2"] = rtmpUrl });
            }
            catch { }
        }

        public static void StreamError(string streamName, string error)
        {
            if (!_registered) return;
            try
            {
                LogClient.Instance.NewEntry(AppId, ComponentId, MsgStreamError,
                    GetSiteItem(),
                    new Dictionary<string, string> { ["p1"] = streamName, ["p2"] = error });
            }
            catch { }
        }

        public static void StreamStopped(string streamName)
        {
            if (!_registered) return;
            try
            {
                LogClient.Instance.NewEntry(AppId, ComponentId, MsgStreamStopped,
                    GetSiteItem(),
                    new Dictionary<string, string> { ["p1"] = streamName });
            }
            catch { }
        }

        public static void HelperCrashed(string streamName, int restartCount)
        {
            if (!_registered) return;
            try
            {
                LogClient.Instance.NewEntry(AppId, ComponentId, MsgHelperCrashed,
                    GetSiteItem(),
                    new Dictionary<string, string>
                    {
                        ["p1"] = streamName,
                        ["p2"] = restartCount.ToString()
                    });
            }
            catch { }
        }

        public static void PluginStarted(int streamCount)
        {
            if (!_registered) return;
            try
            {
                LogClient.Instance.NewEntry(AppId, ComponentId, MsgPluginStarted,
                    GetSiteItem(),
                    new Dictionary<string, string> { ["p1"] = streamCount.ToString() });
            }
            catch { }
        }

        public static void PluginStopped()
        {
            if (!_registered) return;
            try
            {
                LogClient.Instance.NewEntry(AppId, ComponentId, MsgPluginStopped,
                    GetSiteItem(), null);
            }
            catch { }
        }
    }
}
