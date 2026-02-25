using System;
using System.Collections.Generic;

namespace RTMPStreamer.Messaging
{
    internal static class StreamMessageIds
    {
        public const string StatusUpdate = "RTMPStreamer.StatusUpdate";
        public const string StatusRequest = "RTMPStreamer.StatusRequest";
        public const string StatusResponse = "RTMPStreamer.StatusResponse";
    }

    [Serializable]
    public class StreamStatusUpdate
    {
        public Guid ItemId;
        public string Status;
        public long Frames;
        public double Fps;
        public long Bytes;
        public long KeyFrames;
        public int RestartCount;
        public List<string> RecentLogLines;
        public DateTime Timestamp;
    }

    [Serializable]
    public class StreamStatusRequest
    {
        public Guid ItemId;
    }
}
