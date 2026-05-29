using System.Runtime.Serialization;

namespace AutoExporterHelper
{
    // IPC contract between AutoExporter (Event Server BG plugin, writer) and
    // AutoExporterHelper.exe (standalone SDK process, reader). Both sides
    // duplicate these DTOs by design (they're tiny, and a separate shared
    // project isn't worth the build complexity).

    [DataContract(Name = "HelperRequest", Namespace = "")]
    public class HelperRequest
    {
        [DataMember] public string RunId;
        [DataMember] public string JobName;
        [DataMember] public string Format;          // "XProtect" | "AVI"
        [DataMember] public bool Encrypt;
        [DataMember] public string Password;
        [DataMember] public bool IncludePlayer;
        [DataMember] public bool IncludeAudio;
        [DataMember] public string RangeStartUtc;   // ISO 8601 / round-trip
        [DataMember] public string RangeEndUtc;
        [DataMember] public string OutputFolder;
        [DataMember] public HelperTarget[] Targets;
    }

    [DataContract(Name = "HelperTarget", Namespace = "")]
    public class HelperTarget
    {
        [DataMember] public string Kind;            // "Camera" | "Group"
        [DataMember] public string ObjectId;        // guid string
        [DataMember] public string Name;            // display name cached at config time
    }

    [DataContract(Name = "HelperResult", Namespace = "")]
    public class HelperResult
    {
        [DataMember] public bool Success;
        [DataMember] public string Error;
        [DataMember] public int CameraCount;
        [DataMember] public long BytesWritten;
        [DataMember] public string[] CameraNames;
        [DataMember] public string[] SkippedCameras;   // resolved cameras dropped for having no recordings
    }
}
