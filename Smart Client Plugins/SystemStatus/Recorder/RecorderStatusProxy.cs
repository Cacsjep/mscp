using System;
using System.CodeDom.Compiler;
using System.Web.Services;
using System.Web.Services.Description;
using System.Web.Services.Protocols;
using System.Xml.Serialization;

namespace SystemStatus.Recorder
{
    /// <summary>
    /// Minimal, self-contained SOAP client for the recording server's RecorderStatusService2,
    /// covering only GetVideoDeviceStatistics. It is a faithful copy of the relevant slice of the
    /// generated MIP SDK proxy (VideoOS.Platform.SDK.Proxy.Status2) - same SOAP namespace, action,
    /// Literal/Wrapped style and data contracts - reproduced here so the plugin needs no runtime
    /// dependency on VideoOS.Platform.SDK.dll. The Smart Client does not ship that assembly, and it
    /// pulls in a heavy dependency graph (Autofac, identity, media-storage) that the client lacks;
    /// the wire format, however, is generated deterministically from the method signature and
    /// attributes below, so this produces byte-identical requests to the SDK proxy.
    ///
    /// The contract (namespaces, member names/order, the Size class, inheritance via xsi:type) was
    /// extracted from MilestoneSystems.VideoOS.Platform.SDK 21.2 and verified by round-tripping the
    /// real types through XmlSerializer.
    /// </summary>
    [GeneratedCode("CommunityProxy", "1.0")]
    [System.Diagnostics.DebuggerStepThrough]
    [System.ComponentModel.DesignerCategory("code")]
    [WebServiceBinding(Name = "RecorderStatusService2Soap", Namespace = ServiceNamespace)]
    [XmlInclude(typeof(DeviceStatisticsBase))]
    public class RecorderStatusService2 : SoapHttpClientProtocol
    {
        public const string ServiceNamespace = "http://videoos.net/2/XProtectCSRecorderStatus2";

        // The status service is hosted at this path on each recording server's web server
        // (e.g. http://recorder-host:7563/). Matches the path the SDK proxy targets.
        private const string ServicePath = "recorderstatusservice/recorderstatusservice2.asmx";

        /// <param name="recorderBaseUri">The recording server's base URI (FQID.ServerId.Uri).</param>
        public RecorderStatusService2(Uri recorderBaseUri)
        {
            if (recorderBaseUri == null) throw new ArgumentNullException(nameof(recorderBaseUri));
            Url = new Uri(recorderBaseUri, ServicePath).ToString();
        }

        [SoapDocumentMethod(ServiceNamespace + "/GetVideoDeviceStatistics",
            RequestNamespace = ServiceNamespace,
            ResponseNamespace = ServiceNamespace,
            Use = SoapBindingUse.Literal,
            ParameterStyle = SoapParameterStyle.Wrapped)]
        // Return is the CONCRETE VideoDeviceStatistics[] (matching the SDK 21.2 signature). No XML
        // attribute on the return: the SDK has none, so the array uses the default *wrapped* form -
        // a single <GetVideoDeviceStatisticsResult> container holding <VideoDeviceStatistics> items.
        // (An explicit [return: XmlElement] would instead force the unwrapped/repeated form, which
        // the recorder does not send, yielding one empty object with a null stream array.)
        public VideoDeviceStatistics[] GetVideoDeviceStatistics(string token, Guid[] deviceIds)
        {
            object[] results = Invoke("GetVideoDeviceStatistics", new object[] { token, deviceIds });
            return (VideoDeviceStatistics[])results[0];
        }

        [SoapDocumentMethod(ServiceNamespace + "/GetRecorderStatus",
            RequestNamespace = ServiceNamespace, ResponseNamespace = ServiceNamespace,
            Use = SoapBindingUse.Literal, ParameterStyle = SoapParameterStyle.Wrapped)]
        public AttachAndConnectionState GetRecorderStatus(string token)
        {
            object[] results = Invoke("GetRecorderStatus", new object[] { token });
            return (AttachAndConnectionState)results[0];
        }

        [SoapDocumentMethod(ServiceNamespace + "/GetRecordingStorageStatus",
            RequestNamespace = ServiceNamespace, ResponseNamespace = ServiceNamespace,
            Use = SoapBindingUse.Literal, ParameterStyle = SoapParameterStyle.Wrapped)]
        public StorageStatus[] GetRecordingStorageStatus(string token)
        {
            object[] results = Invoke("GetRecordingStorageStatus", new object[] { token });
            return (StorageStatus[])results[0];
        }

        [SoapDocumentMethod(ServiceNamespace + "/GetArchiveStorageStatus",
            RequestNamespace = ServiceNamespace, ResponseNamespace = ServiceNamespace,
            Use = SoapBindingUse.Literal, ParameterStyle = SoapParameterStyle.Wrapped)]
        public StorageStatus[] GetArchiveStorageStatus(string token)
        {
            object[] results = Invoke("GetArchiveStorageStatus", new object[] { token });
            return (StorageStatus[])results[0];
        }
    }

    // ── Data contracts (xsi:type polymorphism via XmlInclude on the base types) ──────────────

    [XmlInclude(typeof(VideoDeviceStatistics))]
    [XmlType(Namespace = RecorderStatusService2.ServiceNamespace)]
    public class DeviceStatisticsBase
    {
        public Guid DeviceId;
        public ulong UsedSpaceInBytes;
    }

    [XmlType(Namespace = RecorderStatusService2.ServiceNamespace)]
    public class VideoDeviceStatistics : DeviceStatisticsBase
    {
        public VideoStreamStatistics[] VideoStreamStatisticsArray;
    }

    [XmlInclude(typeof(VideoStreamStatistics))]
    [XmlType(Namespace = RecorderStatusService2.ServiceNamespace)]
    public class MediaStreamStatisticsBase
    {
        public Guid StreamId;
        public string Name;
        public bool RecordingStream;
        public bool LiveStream;
        public bool LiveStreamDefault;
        public bool LiveStreamRunAlways;
        public ulong BPS;
        public double FPS;
    }

    [XmlType(Namespace = RecorderStatusService2.ServiceNamespace)]
    public class VideoStreamStatistics : MediaStreamStatisticsBase
    {
        // Declared first in the derived type; reference type, omitted on the wire when null.
        public Size ImageResolution;
        public string VideoFormat;
        public ulong ImageSizeInBytes;
        public double FPSRequested;
    }

    [XmlType(Namespace = RecorderStatusService2.ServiceNamespace)]
    public class Size
    {
        public int Width;
        public int Height;
    }

    // ── Recorder + storage status (non-polymorphic) ──────────────────────────────────────────

    [XmlType(Namespace = RecorderStatusService2.ServiceNamespace)]
    public class AttachAndConnectionState
    {
        public string AttachState;
        public string ConnectionState;
    }

    /// <summary>One storage (recording or archive) on a recording server.</summary>
    [XmlType(Namespace = RecorderStatusService2.ServiceNamespace)]
    public class StorageStatus
    {
        public Guid StorageId;
        public string Name;
        public string Path;
        public bool Available;
        public ulong UsedSpaceInBytes;
        public ulong FreeSpaceInBytes;
    }
}
