using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using VideoOS.Platform.DriverFramework.Definitions;
using VideoOS.Platform.DriverFramework.Exceptions;
using VideoOS.Platform.DriverFramework.Managers;
using VideoOS.Platform.DriverFramework.Utilities;

namespace RTMPDriver
{
    /// <summary>
    /// This class returns information about the hardware including capabilities and settings supported.
    /// </summary>
    public class RTMPDriverConfigurationManager : ConfigurationManager
    {
        private const string _firmware = "RTMPDriver Firmware";
        private const string _firmwareVersion = "1.0";
        private const string _hardwareName = "RTMP Server";

        private new RTMPDriverContainer Container => base.Container as RTMPDriverContainer;

        public RTMPDriverConfigurationManager(RTMPDriverContainer container) : base(container)
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
            var uri = Container.HardwareUri;
            int port = uri?.Port > 0 ? uri.Port : 8783;

            return new Dictionary<string, string>()
            {
                {Constants.ServerPort, port.ToString()},
                {Constants.ShowOfflineInfo, "true" },
                {Constants.RateLimitEnabled, "true"},
                {Constants.RateLimitMaxRequests, "10"},
                {Constants.MaxConnections, Constants.DefaultMaxConnections.ToString()},
                {Constants.EnableTls, "false"},
                {Constants.TlsCertificatePassword, ""}
            };
        }

        protected override ICollection<ISetupField> BuildFields()
        {
            var fields = new List<ISetupField>();

            fields.Add(new NumberSetupField()
            {
                Key = Constants.ServerPort,
                DisplayName = "Server Port",
                ReferenceId = Constants.ServerPortRefId,
                MinValue = 1,
                MaxValue = 65535,
                Resolution = 1,
                DefaultValue = 8783,
            });

            fields.Add(new BoolSetupField()
            {
                Key = Constants.ShowOfflineInfo,
                DisplayName = "Show Stream Offline Info",
                ReferenceId = Constants.ShowOfflineInfoRefId,
                DefaultValue = true,
            });

            fields.Add(new BoolSetupField()
            {
                Key = Constants.RateLimitEnabled,
                DisplayName = "Enable Rate Limiter",
                ReferenceId = Constants.RateLimitEnabledRefId,
                DefaultValue = true,
            });

            fields.Add(new NumberSetupField()
            {
                Key = Constants.RateLimitMaxRequests,
                DisplayName = "Rate Limit Max Requests Per Second",
                ReferenceId = Constants.RateLimitMaxRequestsRefId,
                MinValue = 1,
                MaxValue = 1000,
                Resolution = 1,
                DefaultValue = 10,
            });

            fields.Add(new NumberSetupField()
            {
                Key = Constants.MaxConnections,
                DisplayName = "Max Concurrent Connections",
                ReferenceId = Constants.MaxConnectionsRefId,
                MinValue = 1,
                MaxValue = 1000,
                Resolution = 1,
                DefaultValue = Constants.DefaultMaxConnections,
            });

            fields.Add(new BoolSetupField()
            {
                Key = Constants.EnableTls,
                DisplayName = "TLS Enable (RTMPS)",
                ReferenceId = Constants.EnableTlsRefId,
                DefaultValue = false,
            });

            fields.Add(new StringSetupField()
            {
                Key = Constants.TlsCertificatePassword,
                DisplayName = "TLS Certificate Password",
                ReferenceId = Constants.TlsCertificatePasswordRefId,
                DefaultValue = "",
            });

            fields.Add(new StringSetupField()
            {
                Key = Constants.RtmpPushStreamPath,
                DisplayName = "RTMP Push Stream Path",
                ReferenceId = Constants.RtmpPushStreamPathRefId,
                DefaultValue = "/camera1",
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
                    DisplayName = $"Stream {i + 1}",
                    DeviceId = Constants.DeviceIds[i].ToString(),
                    Settings = new Dictionary<string, string>()
                    {
                        {Constants.RtmpPushStreamPath, $"/stream{i + 1}"}
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
                DisplayName = "RTMP Stream Started",
                NameReferenceId = Constants.StreamStartedRefId,
                CounterReferenceId = Constants.StreamStarted,
            });
            deviceEvents.Add(new EventDefinition()
            {
                ReferenceId = Constants.StreamStopped,
                DisplayName = "RTMP Stream Stopped",
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
                Settings = new Dictionary<string, string>()
                {
                },
                RemotePlaybackSupport = false,
            });

            return streams;
        }
    }
}
