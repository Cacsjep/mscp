using System;
using System.Collections.Generic;
using System.Linq;
using VideoOS.Platform;

namespace CertWatchdog.Services
{
    internal static class EndpointDiscoveryService
    {
        /// <summary>
        /// Returns a list of (url, serviceType) tuples for all discovered HTTPS endpoints.
        /// </summary>
        public static List<(string Url, string ServiceType)> DiscoverHttpsEndpoints()
        {
            // Key: authority URL, Value: service type name
            var endpoints = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

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

            return endpoints.Select(kvp => (kvp.Key, kvp.Value)).ToList();
        }

        private static void DiscoverRegisteredServices(Dictionary<string, string> endpoints)
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
                                endpoints[authority] = serviceType;
                        }
                    }
                    catch
                    {
                        // Skip malformed URIs
                    }
                }
            }
        }

        private static void DiscoverRecordingServers(Dictionary<string, string> endpoints)
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
                                endpoints[authority] = "Recording Server";
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
                                endpoints[authority] = "Recording Server";
                        }
                    }
                    catch
                    {
                        // Skip malformed URIs
                    }
                }
            }
        }

        private static void DiscoverManagementServer(Dictionary<string, string> endpoints)
        {
            var serverId = EnvironmentManager.Instance.MasterSite.ServerId;
            if (serverId.ServerScheme != null &&
                serverId.ServerScheme.Equals("https", StringComparison.OrdinalIgnoreCase))
            {
                var authority = $"https://{serverId.ServerHostname}:{serverId.Serverport}";
                if (!endpoints.ContainsKey(authority))
                    endpoints[authority] = "Management Server";
            }
        }
    }
}
