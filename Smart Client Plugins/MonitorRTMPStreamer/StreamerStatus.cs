using System;

namespace MonitorRTMPStreamer
{
    public class StreamerStatus
    {
        public static readonly StreamerStatus Instance = new StreamerStatus();

        private readonly object _lock = new object();

        public bool IsCapturing { get; private set; }
        public bool IsStreaming { get; private set; }
        public int MonitorCount { get; private set; }
        public int StitchedWidth { get; private set; }
        public int StitchedHeight { get; private set; }
        public long CaptureMs { get; private set; }
        public long EncodeMs { get; private set; }
        public long TotalMs { get; private set; }
        public string RtmpUrl { get; private set; } = "";
        public string CaptureMethodName { get; private set; } = "";
        public string LastError { get; private set; } = "";
        public DateTime? StreamStarted { get; private set; }

        public volatile bool RestartRequested;
        public volatile bool RestartCompleted;

        public void UpdateCycle(int monitors, int width, int height, long captureMs, long encodeMs, long totalMs)
        {
            lock (_lock)
            {
                IsCapturing = true;
                MonitorCount = monitors;
                StitchedWidth = width;
                StitchedHeight = height;
                CaptureMs = captureMs;
                EncodeMs = encodeMs;
                TotalMs = totalMs;
            }
        }

        public void UpdateCaptureMethod(string name)
        {
            lock (_lock) { CaptureMethodName = name ?? ""; }
        }

        public void UpdateStreaming(bool streaming, string url)
        {
            lock (_lock)
            {
                IsStreaming = streaming;
                RtmpUrl = url ?? "";
                if (streaming && StreamStarted == null)
                    StreamStarted = DateTime.Now;
                if (!streaming)
                    StreamStarted = null;
            }
        }

        public void SetError(string error)
        {
            lock (_lock) { LastError = error; }
        }

        public void ClearError()
        {
            lock (_lock) { LastError = ""; }
        }
    }
}
