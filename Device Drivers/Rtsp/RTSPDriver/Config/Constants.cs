using System;
using VideoOS.Platform.DriverFramework.Definitions;

namespace RTSPDriver
{
    public static class Constants
    {
        public const string DriverVersion = "1.0";
        public const string DriverDisplayName = "RTSP-Driver";
        public const int MaxDevices = 4;

        public static readonly ProductDefinition Product1 = new ProductDefinition
        {
            Id = new Guid("d4a1b2c3-e5f6-4a7b-8c9d-0e1f2a3b4c5d"),
            Name = "RTSPDriver"
        };

        public static readonly Guid DriverId = new Guid("f7e6d5c4-b3a2-4190-8f7e-6d5c4b3a2190");
        public static readonly Guid VideoStream1RefId = new Guid("a1b2c3d4-e5f6-4a7b-8c9d-0e1f2a3b4c5e");
        public static readonly Guid VideoStream2RefId = new Guid("a1b2c3d4-e5f6-4a7b-8c9d-0e1f2a3b4c5f");
        public static readonly Guid AudioStream1RefId = new Guid("a1b2c3d4-e5f6-4a7b-8c9d-0e1f2a3b4c60");

        // Per-device settings (primary stream)
        public static readonly string RtspPath = nameof(RtspPath);
        public static readonly Guid RtspPathRefId = new Guid("b2c3d4e5-f6a7-4b8c-9d0e-1f2a3b4c5d6e");

        // Per-device settings (secondary stream)
        public static readonly string RtspPath2 = nameof(RtspPath2);
        public static readonly Guid RtspPath2RefId = new Guid("b2c3d4e5-f6a7-4b8c-9d0e-1f2a3b4c5d6f");

        public static readonly string TransportProtocol = nameof(TransportProtocol);
        public static readonly Guid TransportProtocolRefId = new Guid("c3d4e5f6-a7b8-4c9d-0e1f-2a3b4c5d6e7f");

        public static readonly string ChannelEnabled = nameof(ChannelEnabled);
        public static readonly Guid ChannelEnabledRefId = new Guid("d4e5f6a7-b8c9-4d0e-1f2a-3b4c5d6e7f80");

        // Hardware settings
        public static readonly string RtspPort = nameof(RtspPort);
        public static readonly Guid RtspPortRefId = new Guid("a1b2c3d4-e5f6-4a7b-8c9d-1a2b3c4d5e6f");

        public static readonly string ConnectionTimeoutSec = nameof(ConnectionTimeoutSec);
        public static readonly Guid ConnectionTimeoutSecRefId = new Guid("e5f6a7b8-c9d0-4e1f-2a3b-4c5d6e7f8091");

        public static readonly string ReconnectIntervalSec = nameof(ReconnectIntervalSec);
        public static readonly Guid ReconnectIntervalSecRefId = new Guid("f6a7b8c9-d0e1-4f2a-3b4c-5d6e7f8091a2");

        public static readonly string RtpBufferSizeKB = nameof(RtpBufferSizeKB);
        public static readonly Guid RtpBufferSizeKBRefId = new Guid("b8c9d0e1-f2a3-4b4c-5d6e-7f8091a2b3c4");

        // Events
        public static readonly Guid StreamStarted = new Guid("c9d0e1f2-a3b4-4c5d-6e7f-8091a2b3c4d5");
        public static readonly Guid StreamStartedRefId = new Guid("d0e1f2a3-b4c5-4d6e-7f80-91a2b3c4d5e6");
        public static readonly Guid StreamStopped = new Guid("e1f2a3b4-c5d6-4e7f-8091-a2b3c4d5e6f7");
        public static readonly Guid StreamStoppedRefId = new Guid("f2a3b4c5-d6e7-4f80-91a2-b3c4d5e6f7a8");

        /// <summary>
        /// Unique device GUIDs for each of the 4 camera/channel devices.
        /// </summary>
        public static readonly Guid[] DeviceIds = new Guid[]
        {
            new Guid("a4c7e291-3f5b-4d8a-9c1e-7b2d4f6a8001"),
            new Guid("a4c7e291-3f5b-4d8a-9c1e-7b2d4f6a8002"),
            new Guid("a4c7e291-3f5b-4d8a-9c1e-7b2d4f6a8003"),
            new Guid("a4c7e291-3f5b-4d8a-9c1e-7b2d4f6a8004"),
        };

        /// <summary>
        /// Unique device GUIDs for each of the 4 microphone devices.
        /// Audio is always sourced from the primary RTSP stream.
        /// </summary>
        public static readonly Guid[] MicrophoneDeviceIds = new Guid[]
        {
            new Guid("a4c7e291-3f5b-4d8a-9c1e-7b2d4f6a8011"),
            new Guid("a4c7e291-3f5b-4d8a-9c1e-7b2d4f6a8012"),
            new Guid("a4c7e291-3f5b-4d8a-9c1e-7b2d4f6a8013"),
            new Guid("a4c7e291-3f5b-4d8a-9c1e-7b2d4f6a8014"),
        };

        /// <summary>
        /// Check if a GUID is one of our camera device IDs.
        /// </summary>
        public static bool IsVideoDevice(Guid id)
        {
            for (int i = 0; i < DeviceIds.Length; i++)
            {
                if (DeviceIds[i] == id) return true;
            }
            return false;
        }

        /// <summary>
        /// Check if a GUID is one of our microphone device IDs.
        /// </summary>
        public static bool IsMicrophoneDevice(Guid id)
        {
            for (int i = 0; i < MicrophoneDeviceIds.Length; i++)
            {
                if (MicrophoneDeviceIds[i] == id) return true;
            }
            return false;
        }

        /// <summary>
        /// Get the channel index (0-based) for a microphone device GUID.
        /// Returns -1 if not found.
        /// </summary>
        public static int MicrophoneChannelIndex(Guid id)
        {
            for (int i = 0; i < MicrophoneDeviceIds.Length; i++)
            {
                if (MicrophoneDeviceIds[i] == id) return i;
            }
            return -1;
        }
    }
}
