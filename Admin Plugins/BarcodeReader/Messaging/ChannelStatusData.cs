using System;
using System.Collections.Generic;

namespace BarcodeReader.Messaging
{
    internal static class BarcodeMessageIds
    {
        public const string StatusUpdate   = "BarcodeReader.StatusUpdate";
        public const string StatusRequest  = "BarcodeReader.StatusRequest";
        public const string StatusResponse = "BarcodeReader.StatusResponse";
    }

    [Serializable]
    public class ChannelStatusUpdate
    {
        public Guid ItemId;
        public string Status;

        public long Frames;
        public long Decoded;
        public long Failed;

        public double CameraFps;     // observed frame-arrival rate from JPEGLiveSource
        public double DecodeFps;     // observed successful decode rate
        public double InfMsAvg;      // rolling average inference time
        public double InfMsP95;      // 95th percentile inference time
        public double MaxFps;        // 1000 / InfMsAvg — theoretical ceiling

        public int RestartCount;
        public List<string> RecentLogLines;
        public DateTime Timestamp;
    }

    [Serializable]
    public class ChannelStatusRequest
    {
        public Guid ItemId;
    }
}
