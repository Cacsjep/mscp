using CertWatchdog.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using VideoOS.Platform;

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
            var items = Configuration.Instance.GetItemsByKind(Kind.Hardware);
            if (items == null || items.Count == 0)
            {
                PluginLog.Info("No hardware items found");
                return;
            }

            PluginLog.Info($"Found {items.Count} hardware items");

            var firstItem = items[0];
            if (firstItem.Properties != null)
            {
                PluginLog.Info($"Hardware item '{firstItem.Name}' properties: {string.Join(", ", firstItem.Properties.Keys)}");
            }

            int count = 0;
            foreach (var item in items)
            {
                if (item.Properties == null) continue;

                try
                {
                    var address = GetProperty(item, "Address");
                    if (string.IsNullOrEmpty(address)) continue;

                    string url = null;
                    var objectId = item.FQID?.ObjectId ?? Guid.Empty;

                    // Strategy 1: Address is already a full URL — check its scheme
                    try
                    {
                        var parsed = new Uri(address);
                        if (parsed.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase))
                        {
                            url = $"https://{parsed.Authority}";
                        }
                    }
                    catch
                    {
                        // Not a valid URI, skip
                    }

                    // Strategy 2: Explicit HTTPSEnabled property (some drivers expose this)
                    if (url == null)
                    {
                        var httpsEnabled = GetProperty(item, "HTTPSEnabled");
                        if (!string.IsNullOrEmpty(httpsEnabled) &&
                            httpsEnabled.Equals("Yes", StringComparison.OrdinalIgnoreCase))
                        {
                            var httpsPort = GetProperty(item, "HTTPSPort");
                            if (string.IsNullOrEmpty(httpsPort))
                                httpsPort = "443";

                            string host;
                            try
                            {
                                var parsed = new Uri(address);
                                host = parsed.Host;
                            }
                            catch
                            {
                                host = address;
                            }

                            url = $"https://{host}:{httpsPort}";
                        }
                    }

                    if (url == null) continue;

                    // Hardware devices can share the same address; use URL+ObjectId as key
                    var key = $"{url}|{objectId}";
                    if (!endpoints.ContainsKey(key))
                    {
                        endpoints[key] = new EndpointInfo
                        {
                            Url = url,
                            ServiceType = "Camera/Hardware",
                            SourceItemId = objectId == Guid.Empty ? (Guid?)null : objectId
                        };
                        count++;
                    }
                }
                catch (Exception ex)
                {
                    PluginLog.Error($"Error processing hardware item '{item.Name}': {ex.Message}");
                }
            }

            PluginLog.Info($"Discovered {count} hardware HTTPS endpoint(s)");
        }

        private static string GetProperty(Item item, string key)
        {
            if (item.Properties.ContainsKey(key))
                return item.Properties[key];
            return null;
        }
    }
}
