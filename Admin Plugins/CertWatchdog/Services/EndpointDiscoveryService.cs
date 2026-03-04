using CertWatchdog.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using VideoOS.Platform;
using VideoOS.Platform.ConfigurationItems;

namespace CertWatchdog.Services
{
    internal static class EndpointDiscoveryService
    {
        /// <summary>
        /// Returns a list of EndpointInfo for all discovered HTTPS endpoints,
        /// including server endpoints and hardware device endpoints.
        /// </summary>
        public static List<EndpointInfo> DiscoverHttpsEndpoints()
        {
            // Key: authority URL, Value: EndpointInfo
            var endpoints = new Dictionary<string, EndpointInfo>(StringComparer.OrdinalIgnoreCase);

            try
            {
                DiscoverRegisteredServices(endpoints);
            }
            catch (Exception ex)
            {
                PluginLog.Error($"Error discovering registered services: {ex.Message}", ex);
            }

            try
            {
                DiscoverRecordingServers(endpoints);
            }
            catch (Exception ex)
            {
                PluginLog.Error($"Error discovering recording servers: {ex.Message}", ex);
            }

            try
            {
                DiscoverManagementServer(endpoints);
            }
            catch (Exception ex)
            {
                PluginLog.Error($"Error discovering management server: {ex.Message}", ex);
            }

            try
            {
                DiscoverHardwareDevices(endpoints);
            }
            catch (Exception ex)
            {
                PluginLog.Error($"Error discovering hardware devices: {ex.Message}", ex);
            }

            return endpoints.Values.ToList();
        }

        private static void DiscoverRegisteredServices(Dictionary<string, EndpointInfo> endpoints)
        {
            var serverId = EnvironmentManager.Instance.MasterSite.ServerId;
            var serviceInfoList = Configuration.Instance.GetRegisteredServiceUriInfo(serverId);

            if (serviceInfoList == null) return;

            foreach (var serviceInfo in serviceInfoList)
            {
                if (serviceInfo.UriArray == null) continue;

                var serviceType = serviceInfo.Name ?? "Unknown Service";

                foreach (var uri in serviceInfo.UriArray)
                {
                    if (string.IsNullOrEmpty(uri)) continue;

                    try
                    {
                        var parsed = new Uri(uri);
                        if (parsed.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase))
                        {
                            var authority = $"https://{parsed.Authority}";
                            if (!endpoints.ContainsKey(authority))
                                endpoints[authority] = new EndpointInfo { Url = authority, ServiceType = serviceType };
                        }
                    }
                    catch
                    {
                        // Skip malformed URIs
                    }
                }
            }
        }

        private static void DiscoverRecordingServers(Dictionary<string, EndpointInfo> endpoints)
        {
            var items = Configuration.Instance.GetItemsByKind(Kind.Server);
            if (items == null) return;

            foreach (var item in items)
            {
                if (item.FQID?.ServerId != null)
                {
                    try
                    {
                        var sid = item.FQID.ServerId;
                        if (sid.ServerScheme != null &&
                            sid.ServerScheme.Equals("https", StringComparison.OrdinalIgnoreCase))
                        {
                            var authority = $"https://{sid.ServerHostname}:{sid.Serverport}";
                            if (!endpoints.ContainsKey(authority))
                                endpoints[authority] = new EndpointInfo { Url = authority, ServiceType = "Recording Server" };
                        }
                    }
                    catch
                    {
                        // Skip
                    }
                }

                if (item.Properties == null) continue;

                foreach (var key in new[] { "Address", "WebServerUri", "Uri" })
                {
                    if (!item.Properties.ContainsKey(key)) continue;

                    var value = item.Properties[key];
                    if (string.IsNullOrEmpty(value)) continue;

                    try
                    {
                        var parsed = new Uri(value);
                        if (parsed.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase))
                        {
                            var authority = $"https://{parsed.Authority}";
                            if (!endpoints.ContainsKey(authority))
                                endpoints[authority] = new EndpointInfo { Url = authority, ServiceType = "Recording Server" };
                        }
                    }
                    catch
                    {
                        // Skip malformed URIs
                    }
                }
            }
        }

        private static void DiscoverManagementServer(Dictionary<string, EndpointInfo> endpoints)
        {
            var serverId = EnvironmentManager.Instance.MasterSite.ServerId;
            if (serverId.ServerScheme != null &&
                serverId.ServerScheme.Equals("https", StringComparison.OrdinalIgnoreCase))
            {
                var authority = $"https://{serverId.ServerHostname}:{serverId.Serverport}";
                if (!endpoints.ContainsKey(authority))
                    endpoints[authority] = new EndpointInfo { Url = authority, ServiceType = "Management Server" };
            }
        }

        private static void DiscoverHardwareDevices(Dictionary<string, EndpointInfo> endpoints)
        {
            var management = new ManagementServer(EnvironmentManager.Instance.MasterSite);
            int count = 0;

            foreach (var rs in management.RecordingServerFolder.RecordingServers)
            {
                PluginLog.Info($"Checking recording server: {rs.Name}");

                foreach (var hw in rs.HardwareFolder.Hardwares)
                {
                    if (!hw.Enabled) continue;

                    try
                    {
                        var address = hw.Address;
                        if (string.IsNullOrEmpty(address)) continue;

                        // Read HTTPS settings from HardwareDriverSettings
                        bool httpsEnabled = false;
                        int httpsPort = 443;

                        try
                        {
                            foreach (var settings in hw.HardwareDriverSettingsFolder.HardwareDriverSettings)
                            {
                                foreach (var child in settings.HardwareDriverSettingsChildItems)
                                {
                                    var props = child.Properties;
                                    if (props.Keys.Contains("HttpSEnabled"))
                                        httpsEnabled = "Yes".Equals(props.GetValue("HttpSEnabled"), StringComparison.OrdinalIgnoreCase);
                                    if (props.Keys.Contains("HttpSPort"))
                                        int.TryParse(props.GetValue("HttpSPort"), out httpsPort);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            PluginLog.Error($"Error reading driver settings for '{hw.Name}': {ex.Message}");
                        }

                        if (!httpsEnabled) continue;

                        // Parse Address (always HTTP) and construct HTTPS URL
                        Uri parsed;
                        try { parsed = new Uri(address); }
                        catch { continue; }

                        var url = httpsPort == 443
                            ? $"https://{parsed.Host}"
                            : $"https://{parsed.Host}:{httpsPort}";

                        // Use first camera under this hardware as event source
                        Guid? cameraId = null;
                        try
                        {
                            var firstCamera = hw.CameraFolder.Cameras.FirstOrDefault();
                            if (firstCamera != null)
                                cameraId = new Guid(firstCamera.Id);
                        }
                        catch { }

                        var key = $"{url}|{hw.Id}";
                        if (!endpoints.ContainsKey(key))
                        {
                            endpoints[key] = new EndpointInfo
                            {
                                Url = url,
                                ServiceType = hw.Name,
                                SourceItemId = cameraId
                            };
                            count++;
                            PluginLog.Info($"  Hardware '{hw.Name}': {url} (HTTPS port {httpsPort})");
                        }
                    }
                    catch (Exception ex)
                    {
                        PluginLog.Error($"Error processing hardware '{hw.Name}': {ex.Message}");
                    }
                }
            }

            PluginLog.Info($"Discovered {count} hardware HTTPS endpoint(s)");
        }
    }
}
