using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using WebView.Models;
using VideoOS.Platform;
using VideoOS.Platform.ConfigurationItems;

namespace WebView.Services
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
                        Debug.WriteLine($"[WebView] Error on recording server '{rs.Name}': {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WebView] Error discovering devices: {ex.Message}");
            }

            return devices;
        }

        /// <summary>
        /// Reads the hardware password via ReadPasswordHardware().
        /// The password is returned on the ServerTask via GetProperty("Password").
        /// See: https://forum.milestonesys.com/t/how-get-password-with-readpasswordhardware/13146
        /// </summary>
        public static string ReadPassword(string hardwarePath)
        {
            try
            {
                var hw = new Hardware(
                    EnvironmentManager.Instance.MasterSite.ServerId,
                    hardwarePath);

                var serverTask = hw.ReadPasswordHardware();
                var password = serverTask.GetProperty("Password");

                Debug.WriteLine($"[WebView] Password read for '{hw.Name}': {(string.IsNullOrEmpty(password) ? "(empty)" : "(ok)")}");
                return password;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WebView] Error reading password for path '{hardwarePath}': {ex.Message}");
            }
            return null;
        }

        private static void DiscoverHardwareOnServer(RecordingServer rs, List<HardwareDeviceInfo> devices)
        {
            foreach (var hw in rs.HardwareFolder.Hardwares)
            {
                if (!hw.Enabled) continue;

                // Filter out localhost/loopback devices
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

                    // Read HTTPS settings from HardwareDriverSettings
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
                        Debug.WriteLine($"[WebView] Error reading driver settings for '{hw.Name}': {ex.Message}");
                    }

                    devices.Add(device);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[WebView] Error processing hardware '{hw.Name}': {ex.Message}");
                }
            }
        }
    }
}
