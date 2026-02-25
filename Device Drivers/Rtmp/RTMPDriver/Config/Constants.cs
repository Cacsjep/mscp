using System;
using VideoOS.Platform.DriverFramework.Definitions;

namespace RTMPDriver
{
    public static class Constants
    {
        public const string DriverVersion = "2.3";
        public const string DriverDisplayName = "RTMP-Driver";
        public const int MaxDevices = 16;

        public static readonly ProductDefinition Product1 = new ProductDefinition
        {
            Id = new Guid("e155114f-dd32-4f24-bdaa-954d92482d50"),
            Name = "RTMPDriver"
        };

        public static readonly Guid DriverId = new Guid("282cd862-d1a8-4f13-a275-9ad10a184ed1");
        public static readonly Guid VideoStream1RefId = new Guid("86579eea-dfef-47bb-ab3a-d8bcec31d4a8");

        public static readonly string ServerPort = nameof(ServerPort);
        public static readonly Guid ServerPortRefId = new Guid("a3f7c8b1-2d4e-4a6f-b8c9-1e3d5f7a9b0c");

        public static readonly string RtmpPushStreamPath = nameof(RtmpPushStreamPath);
        public static readonly Guid RtmpPushStreamPathRefId = new Guid("b4e8d9c2-3f5a-4b7e-c9d0-2f4e6a8b0d1e");

        public static readonly string ShowOfflineInfo = nameof(ShowOfflineInfo);
        public static readonly Guid ShowOfflineInfoRefId = new Guid("b4e8d9c2-3f5a-4b7e-c9d0-1f3e6a8b0d1e");

        public static readonly string RateLimitEnabled = nameof(RateLimitEnabled);
        public static readonly Guid RateLimitEnabledRefId = new Guid("c5f9e0d3-4a6b-4c8f-a0e1-3a5b7c9d1e2f");

        public static readonly string RateLimitMaxRequests = nameof(RateLimitMaxRequests);
        public static readonly Guid RateLimitMaxRequestsRefId = new Guid("d6a0f1e4-5b7c-4d9a-b1f2-4c6d8e0f2a3b");

        public static readonly string MaxConnections = nameof(MaxConnections);
        public static readonly Guid MaxConnectionsRefId = new Guid("e7b1a2c3-6d8e-4f0a-c2d3-5e7f9a1b3c4d");

        public static readonly string EnableTls = nameof(EnableTls);
        public static readonly Guid EnableTlsRefId = new Guid("f8c2b3d4-7e9a-4a1b-d4e5-6f0a1b2c3d4e");

        public static readonly string TlsCertificatePassword = nameof(TlsCertificatePassword);
        public static readonly Guid TlsCertificatePasswordRefId = new Guid("b0e4d5f6-9a1c-4c3d-f6a7-8b2c3d4e5f6a");

        public const string TlsCertificateFileName = "rtmp.pfx";

        // Protocol security limits
        public const int DefaultMaxConnections = MaxDevices * 2;        // 32
        public const int MaxMessageSize = 5 * 1024 * 1024;             // 5 MB
        public const int MaxChunkStreamsPerClient = 32;
        public const int MinChunkSize = 1;                              // RTMP spec MUST minimum
        public const int MaxChunkSize = 16_777_215;                     // RTMP spec 0xFFFFFF
        public const int MaxConnectionsPerIp = MaxDevices;              // One source IP can use all cameras
        public const int VideoDataTimeoutMs = 15000;                    // 15s no video after publish → disconnect


        public static readonly Guid StreamStarted = new Guid("a1a1f2e4-5b7c-4d9a-b1f2-4c6d8e0f2a3b");
        public static readonly Guid StreamStartedRefId = new Guid("a1a1f2e4-5b7c-4d9a-b1f2-4c6d8e0f2a11");
        public static readonly Guid StreamStopped = new Guid("a1a2f2e4-5b7c-4d9a-b1f2-4c6d8e0f2a3b");
        public static readonly Guid StreamStoppedRefId = new Guid("a1a1f2e4-5b7c-4d9a-b1f2-4c6d8e0f2a22");

        /// <summary>
        /// Unique device GUIDs for each of the 16 camera/stream devices.
        /// </summary>
        public static readonly Guid[] DeviceIds = new Guid[]
        {
            new Guid("ebb3f997-75e5-474d-9da5-952c47d20001"),
            new Guid("ebb3f997-75e5-474d-9da5-952c47d20002"),
            new Guid("ebb3f997-75e5-474d-9da5-952c47d20003"),
            new Guid("ebb3f997-75e5-474d-9da5-952c47d20004"),
            new Guid("ebb3f997-75e5-474d-9da5-952c47d20005"),
            new Guid("ebb3f997-75e5-474d-9da5-952c47d20006"),
            new Guid("ebb3f997-75e5-474d-9da5-952c47d20007"),
            new Guid("ebb3f997-75e5-474d-9da5-952c47d20008"),
            new Guid("ebb3f997-75e5-474d-9da5-952c47d20009"),
            new Guid("ebb3f997-75e5-474d-9da5-952c47d2000a"),
            new Guid("ebb3f997-75e5-474d-9da5-952c47d2000b"),
            new Guid("ebb3f997-75e5-474d-9da5-952c47d2000c"),
            new Guid("ebb3f997-75e5-474d-9da5-952c47d2000d"),
            new Guid("ebb3f997-75e5-474d-9da5-952c47d2000e"),
            new Guid("ebb3f997-75e5-474d-9da5-952c47d2000f"),
            new Guid("ebb3f997-75e5-474d-9da5-952c47d20010"),
        };

        /// <summary>
        /// Check if a GUID is one of our device IDs.
        /// </summary>
        public static bool IsVideoDevice(Guid id)
        {
            for (int i = 0; i < DeviceIds.Length; i++)
            {
                if (DeviceIds[i] == id) return true;
            }
            return false;
        }
    }
}
