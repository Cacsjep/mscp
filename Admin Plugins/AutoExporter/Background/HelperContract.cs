using System.Runtime.Serialization;

namespace AutoExporter.Background
{
    // Duplicated in AutoExporterHelper/HelperContract.cs (different namespace).
    // See the helper-side file for why.

    [DataContract(Name = "HelperRequest", Namespace = "")]
    internal class HelperRequest
    {
        [DataMember] public string RunId;
        [DataMember] public string JobName;
        [DataMember] public string Format;
        [DataMember] public bool Encrypt;
        [DataMember] public string Password;
        [DataMember] public bool IncludePlayer;
        [DataMember] public bool IncludeAudio;
        [DataMember] public string RangeStartUtc;
        [DataMember] public string RangeEndUtc;
        [DataMember] public string OutputFolder;
        [DataMember] public HelperTarget[] Targets;
    }

    [DataContract(Name = "HelperTarget", Namespace = "")]
    internal class HelperTarget
    {
        [DataMember] public string Kind;
        [DataMember] public string ObjectId;
        [DataMember] public string Name;
    }

    [DataContract(Name = "HelperResult", Namespace = "")]
    internal class HelperResult
    {
        [DataMember] public bool Success;
        [DataMember] public string Error;
        [DataMember] public int CameraCount;
        [DataMember] public long BytesWritten;
        [DataMember] public string[] CameraNames;
        [DataMember] public string[] SkippedCameras;
    }
}
