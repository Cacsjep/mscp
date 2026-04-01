using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using RemoteManager.Models;
using VideoOS.Platform;
using VideoOS.Platform.ConfigurationItems;

namespace RemoteManager.Services
{
    internal static class DeviceDiscoveryService
    {
        public static List<HardwareDeviceInfo> DiscoverDevices()
        {
            var devices = new List<HardwareDeviceInfo>();

            try
            {
                var management = new ManagementServer(EnvironmentManager.Instance.MasterSite);

                foreach (var rs in management.RecordingServerFolder.RecordingServers)
                {
                    try
                    {
                        DiscoverHardwareOnServer(rs, devices);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[RemoteManager] Error on recording server '{rs.Name}': {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[RemoteManager] Error discovering devices: {ex.Message}");
            }

            return devices;
        }

        public static string ReadPassword(string hardwarePath)
        {
            try
            {
                var hw = new Hardware(
                    EnvironmentManager.Instance.MasterSite.ServerId,
                    hardwarePath);

                var serverTask = hw.ReadPasswordHardware();
                var password = serverTask.GetProperty("Password");

                Debug.WriteLine($"[RemoteManager] Password read for '{hw.Name}': {(string.IsNullOrEmpty(password) ? "(empty)" : "(ok)")}");
                return password;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[RemoteManager] Error reading password for path '{hardwarePath}': {ex.Message}");
            }
            return null;
        }

        private static void DiscoverHardwareOnServer(RecordingServer rs, List<HardwareDeviceInfo> devices)
        {
            foreach (var hw in rs.HardwareFolder.Hardwares)
            {
                if (!hw.Enabled) continue;

                try
                {
                    var addr = hw.Address;
                    if (!string.IsNullOrEmpty(addr))
                    {
                        var host = new Uri(addr).Host;
                        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
                            host == "127.0.0.1" ||
                            host == "::1")
                            continue;
                    }
                }
                catch { }

                try
                {
                    var device = new HardwareDeviceInfo
                    {
                        Name = hw.Name,
                        Address = hw.Address,
                        Username = hw.UserName,
                        Model = hw.Model,
                        RecordingServerName = rs.Name,
                        HardwareId = new Guid(hw.Id),
                        HardwarePath = hw.Path,
                    };

                    try
                    {
                        foreach (var settings in hw.HardwareDriverSettingsFolder.HardwareDriverSettings)
                        {
                            foreach (var child in settings.HardwareDriverSettingsChildItems)
                            {
                                var props = child.Properties;
                                if (props.Keys.Contains("HttpSEnabled"))
                                    device.HttpsEnabled = "Yes".Equals(props.GetValue("HttpSEnabled"), StringComparison.OrdinalIgnoreCase);
                                if (props.Keys.Contains("HttpSPort"))
                                {
                                    if (int.TryParse(props.GetValue("HttpSPort"), out int port))
                                        device.HttpsPort = port;
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[RemoteManager] Error reading driver settings for '{hw.Name}': {ex.Message}");
                    }

                    devices.Add(device);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[RemoteManager] Error processing hardware '{hw.Name}': {ex.Message}");
                }
            }
        }
    }
}
