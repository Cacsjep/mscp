using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using VideoOS.Platform.DriverFramework.Definitions;
using VideoOS.Platform.DriverFramework.Exceptions;
using VideoOS.Platform.DriverFramework.Managers;
using VideoOS.Platform.DriverFramework.Utilities;

namespace RTSPDriver
{
    /// <summary>
    /// Returns information about the hardware including capabilities and settings.
    /// </summary>
    public class RTSPDriverConfigurationManager : ConfigurationManager
    {
        private const string _firmware = "RTSPDriver Firmware";
        private const string _firmwareVersion = "1.0";
        private const string _hardwareName = "RTSP Source";

        private new RTSPDriverContainer Container => base.Container as RTSPDriverContainer;

        public RTSPDriverConfigurationManager(RTSPDriverContainer container) : base(container)
        {
        }

        protected override ProductInformation FetchProductInformation()
        {
            if (!Container.ConnectionManager.IsConnected)
            {
                Toolbox.Log.Trace("ConfigurationManager: FetchProductInformation called but not connected");
                throw new ConnectionLostException("Connection not established");
            }

            var driverInfo = Container.Definition.DriverInfo;
            var product = driverInfo.SupportedProducts.FirstOrDefault();
            var uri = Container.ConnectionManager.HardwareUri;
            var macAddress = GenerateMacAddress(uri);
            var serialNumber = macAddress.Replace(":", "").Substring(6);

            Toolbox.Log.Trace("ConfigurationManager: FetchProductInformation product={0} version={1} mac={2} serial={3}", product?.Name, driverInfo.Version, macAddress, serialNumber);

            return new ProductInformation
            {
                ProductId = product.Id,
                ProductName = product.Name,
                ProductVersion = driverInfo.Version,
                MacAddress = macAddress,
                FirmwareVersion = _firmwareVersion,
                Firmware = _firmware,
                HardwareName = _hardwareName,
                SerialNumber = serialNumber
            };
        }

        private static string GenerateMacAddress(Uri uri)
        {
            if (uri == null)
                return "02:00:00:00:00:00";

            var input = uri.Host + ":" + uri.Port;
            using (var sha256 = SHA256.Create())
            {
                var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(input));
                return string.Format("02:{0:X2}:{1:X2}:{2:X2}:{3:X2}:{4:X2}",
                    hash[0], hash[1], hash[2], hash[3], hash[4]);
            }
        }

        protected override IDictionary<string, string> BuildHardwareSettings()
        {
            return new Dictionary<string, string>()
            {
                {Constants.ConnectionTimeoutSec, "10"},
                {Constants.ReconnectIntervalSec, "5"},
                {Constants.RtpBufferSizeKB, "256"},
            };
        }

        protected override ICollection<ISetupField> BuildFields()
        {
            var fields = new List<ISetupField>();

            fields.Add(new NumberSetupField()
            {
                Key = Constants.ConnectionTimeoutSec,
                DisplayName = "Connection Timeout (seconds)",
                ReferenceId = Constants.ConnectionTimeoutSecRefId,
                MinValue = 1,
                MaxValue = 60,
                Resolution = 1,
                DefaultValue = 10,
            });

            fields.Add(new NumberSetupField()
            {
                Key = Constants.ReconnectIntervalSec,
                DisplayName = "Reconnect Interval (seconds)",
                ReferenceId = Constants.ReconnectIntervalSecRefId,
                MinValue = 1,
                MaxValue = 300,
                Resolution = 1,
                DefaultValue = 5,
            });

            fields.Add(new NumberSetupField()
            {
                Key = Constants.RtpBufferSizeKB,
                DisplayName = "RTP Buffer Size (KB)",
                ReferenceId = Constants.RtpBufferSizeKBRefId,
                MinValue = 32,
                MaxValue = 4096,
                Resolution = 32,
                DefaultValue = 256,
            });

            // Per-device fields
            fields.Add(new NumberSetupField()
            {
                Key = Constants.RtspPort,
                DisplayName = "RTSP Port",
                ReferenceId = Constants.RtspPortRefId,
                MinValue = 1,
                MaxValue = 65535,
                Resolution = 1,
                DefaultValue = 554,
            });

            fields.Add(new StringSetupField()
            {
                Key = Constants.RtspPath,
                DisplayName = "RTSP Path",
                ReferenceId = Constants.RtspPathRefId,
                DefaultValue = "",
            });

            fields.Add(new EnumSetupField()
            {
                Key = Constants.TransportProtocol,
                DisplayName = "Transport Protocol",
                ReferenceId = Constants.TransportProtocolRefId,
                DefaultValue = "auto",
                EnumList = new List<StringSetupField>
                {
                    new StringSetupField { Key = "auto", DisplayName = "Auto (prefer TCP)" },
                    new StringSetupField { Key = "tcp", DisplayName = "TCP (interleaved)" },
                    new StringSetupField { Key = "udp", DisplayName = "UDP" },
                }
            });

            fields.Add(new BoolSetupField()
            {
                Key = Constants.ChannelEnabled,
                DisplayName = "Channel Enabled",
                ReferenceId = Constants.ChannelEnabledRefId,
                DefaultValue = true,
            });

            return fields;
        }

        protected override ICollection<DeviceDefinitionBase> BuildDevices()
        {
            Toolbox.Log.Trace("ConfigurationManager: BuildDevices count={0}", Constants.MaxDevices);
            var devices = new List<DeviceDefinitionBase>();

            for (int i = 0; i < Constants.MaxDevices; i++)
            {
                devices.Add(new CameraDeviceDefinition()
                {
                    DisplayName = $"Channel {i + 1}",
                    DeviceId = Constants.DeviceIds[i].ToString(),
                    Settings = new Dictionary<string, string>()
                    {
                        {Constants.RtspPort, "554"},
                        {Constants.RtspPath, ""},
                        {Constants.TransportProtocol, "auto"},
                        {Constants.ChannelEnabled, "true"}
                    },
                    Streams = BuildCameraStreams(),
                    DeviceEvents = BuildCameraEvents()
                });
            }
            return devices;
        }

        private static ICollection<EventDefinition> BuildCameraEvents()
        {
            var deviceEvents = new List<EventDefinition>();

            deviceEvents.Add(new EventDefinition()
            {
                ReferenceId = Constants.StreamStarted,
                DisplayName = "RTSP Stream Started",
                NameReferenceId = Constants.StreamStartedRefId,
                CounterReferenceId = Constants.StreamStarted,
            });
            deviceEvents.Add(new EventDefinition()
            {
                ReferenceId = Constants.StreamStopped,
                DisplayName = "RTSP Stream Stopped",
                NameReferenceId = Constants.StreamStoppedRefId,
                CounterReferenceId = Constants.StreamStarted,
            });
            return deviceEvents;
        }

        private static ICollection<StreamDefinition> BuildCameraStreams()
        {
            ICollection<StreamDefinition> streams = new List<StreamDefinition>();
            streams.Add(new StreamDefinition()
            {
                DisplayName = "Video Stream",
                ReferenceId = Constants.VideoStream1RefId.ToString(),
                Settings = new Dictionary<string, string>(),
                RemotePlaybackSupport = false,
            });
            return streams;
        }
    }
}
