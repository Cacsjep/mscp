using System;
using System.Collections.Generic;
using AutoExporter.Background;

namespace AutoExporter.Messaging
{
    internal static class AutoExporterMessageIds
    {
        public const string RunNowRequest         = "AutoExporter.RunNowRequest";
        public const string Progress              = "AutoExporter.Progress";
        public const string ExecutionAdded        = "AutoExporter.ExecutionAdded";
        public const string StorageProbeRequest   = "AutoExporter.StorageProbeRequest";
        public const string StorageProbeReply     = "AutoExporter.StorageProbeReply";
        public const string ClearExecutionsRequest = "AutoExporter.ClearExecutionsRequest";
    }

    [Serializable]
    public class RunNowRequest
    {
        public Guid JobObjectId;
    }

    [Serializable]
    public class ProgressUpdate
    {
        public Guid RunId;
        public Guid JobObjectId;
        public string JobName;
        public int Percent;
        public int CameraIndex;
        public int CameraCount;
        public string CurrentCameraName;
    }

    [Serializable]
    public class ClearExecutionsRequest
    {
        public Guid CorrelationId;
    }

    [Serializable]
    public class StorageProbeRequest
    {
        public Guid CorrelationId;
        public Guid JobObjectId;     // Guid.Empty for ad-hoc Verify (no job context)
        public string JobName;
        public string Path;
        public long MaxBytes;
        public int MaxAgeDays;
    }

    [Serializable]
    public class StorageProbeReply
    {
        public Guid CorrelationId;
        public StorageStatusReport Report;
    }

    [Serializable]
    public class ExecutionRecord
    {
        public Guid RunId;
        public Guid JobObjectId;
        public string JobName;
        public DateTime StartedUtc;
        public DateTime FinishedUtc;
        public DateTime RangeStartUtc;
        public DateTime RangeEndUtc;
        public string Format;
        public string Trigger;          // "Rule", "Manual"
        public bool Success;
        public string Outcome;          // "Success", "Partial", "Skipped", "Failed" (derived from Success if empty)
        public string Error;
        public int CameraCount;
        public long BytesWritten;
        public string OutputFolder;
        public List<string> CameraNames = new List<string>();
        public List<string> SkippedCameras = new List<string>();   // cameras with no recordings in range
    }
}
