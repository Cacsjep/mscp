using System;
using System.Collections.Generic;

namespace RtmpStreamer.Messaging
{
    internal static class StreamMessageIds
    {
        public const string StatusUpdate = "RtmpStreamer.StatusUpdate";
        public const string StatusRequest = "RtmpStreamer.StatusRequest";
        public const string StatusResponse = "RtmpStreamer.StatusResponse";
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
