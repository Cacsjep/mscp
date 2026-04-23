using CertWatchdog.Models;
using CommunitySDK;
using System;
using System.Collections.Generic;
using System.Linq;
using VideoOS.Platform;
using VideoOS.Platform.ConfigurationItems;

namespace CertWatchdog.Services
{
    internal static class EndpointDiscoveryService
    {
        internal static readonly PluginLog _log = new PluginLog("CertWatchdog");
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
                _log.Error($"Error discovering registered services: {ex.Message}", ex);
            }

            try
            {
                DiscoverRecordingServers(endpoints);
            }
            catch (Exception ex)
            {
                _log.Error($"Error discovering recording servers: {ex.Message}", ex);
            }

            try
            {
                DiscoverManagementServer(endpoints);
            }
            catch (Exception ex)
            {
                _log.Error($"Error discovering management server: {ex.Message}", ex);
            }

            try
            {
                DiscoverFailoverServers(endpoints);
            }
            catch (Exception ex)
            {
                _log.Error($"Error discovering failover servers: {ex.Message}", ex);
            }

            try
            {
                DiscoverHardwareDevices(endpoints);
            }
            catch (Exception ex)
            {
                _log.Error($"Error discovering hardware devices: {ex.Message}", ex);
            }

            return endpoints.Values.ToList();
        }

        private static void DiscoverFailoverServers(Dictionary<string, EndpointInfo> endpoints)
        {
            var site = EnvironmentManager.Instance.MasterSite;
            var management = new ManagementServer(site);
            var serverId = site.ServerId;
            int count = 0;
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // 1) Cold-standby: FailoverRecorders inside FailoverGroups
            int groupCount = 0;
            foreach (var group in management.FailoverGroupFolder.FailoverGroups)
            {
                groupCount++;
                foreach (var fr in group.FailoverRecorderFolder.FailoverRecorders)
                {
                    if (!visited.Add(fr.Path ?? fr.Id)) continue;
                    if (!fr.Enabled) continue;

                    AddFailoverEndpoint(endpoints, fr, group.Name, ref count);
                }
            }
            _log.Info($"Enumerated {groupCount} failover group(s)");

            // 2) Hot-standby + group references: RecordingServerFailover entries
            foreach (var rs in management.RecordingServerFolder.RecordingServers)
            {
                foreach (var rsf in rs.RecordingServerFailoverFolder.RecordingServerFailovers)
                {
                    // Hot standby: direct FailoverRecorder reference
                    TryResolveAndAddFailover(endpoints, serverId, rsf.HotStandby, rs.Name, "Hot Standby", visited, ref count);

                    // Primary/Secondary: these are FailoverGroup paths; resolve their FailoverRecorders
                    TryResolveAndAddFailoverGroup(endpoints, serverId, rsf.PrimaryFailoverGroup, rs.Name, "Primary Group", visited, ref count);
                    TryResolveAndAddFailoverGroup(endpoints, serverId, rsf.SecondaryFailoverGroup, rs.Name, "Secondary Group", visited, ref count);
                }
            }

            _log.Info($"Discovered {count} failover server HTTPS endpoint(s)");
        }

        private static bool IsEmptyConfigPath(string path)
        {
            // Milestone reports unassigned references as e.g. "FailoverGroup[00000000-0000-0000-0000-000000000000]"
            return string.IsNullOrEmpty(path) || path.IndexOf("00000000-0000-0000-0000-000000000000", StringComparison.Ordinal) >= 0;
        }

        private static void TryResolveAndAddFailover(
            Dictionary<string, EndpointInfo> endpoints,
            ServerId serverId,
            string path,
            string rsName,
            string kind,
            HashSet<string> visited,
            ref int count)
        {
            if (IsEmptyConfigPath(path)) return;
            if (!visited.Add(path)) return;

            try
            {
                var fr = new FailoverRecorder(serverId, path);
                _log.Info($"Resolved {kind} for recording server '{rsName}': {fr.Name} (enabled={fr.Enabled})");
                if (!fr.Enabled) return;
                AddFailoverEndpoint(endpoints, fr, kind, ref count);
            }
            catch (Exception ex)
            {
                _log.Error($"Failed to resolve {kind} path '{path}' for '{rsName}': {ex.Message}");
            }
        }

        private static void TryResolveAndAddFailoverGroup(
            Dictionary<string, EndpointInfo> endpoints,
            ServerId serverId,
            string path,
            string rsName,
            string kind,
            HashSet<string> visited,
            ref int count)
        {
            if (IsEmptyConfigPath(path)) return;

            try
            {
                var group = new FailoverGroup(serverId, path);
                foreach (var fr in group.FailoverRecorderFolder.FailoverRecorders)
                {
                    if (!visited.Add(fr.Path ?? fr.Id)) continue;
                    if (!fr.Enabled) continue;
                    AddFailoverEndpoint(endpoints, fr, $"{kind} {group.Name}", ref count);
                }
            }
            catch (Exception ex)
            {
                _log.Error($"Failed to resolve {kind} path '{path}' for '{rsName}': {ex.Message}");
            }
        }

        // Port exposed by the Milestone Failover Server's own management API.
        // Useful as a fallback when the recording-server port (WebServerUri) is only
        // online during an active failover takeover.
        private const int FailoverServicePort = 8990;

        private static void AddFailoverEndpoint(
            Dictionary<string, EndpointInfo> endpoints,
            FailoverRecorder fr,
            string groupName,
            ref int count)
        {
            var serviceType = string.IsNullOrEmpty(groupName)
                ? $"Failover Server ({fr.Name})"
                : $"Failover Server ({groupName} / {fr.Name})";

            foreach (var candidate in new[] { fr.ActiveWebServerUri, fr.WebServerUri })
            {
                if (string.IsNullOrEmpty(candidate)) continue;

                try
                {
                    var parsed = new Uri(candidate);
                    if (!parsed.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase)) continue;

                    var authority = $"https://{parsed.Authority}";
                    if (!endpoints.ContainsKey(authority))
                    {
                        var fallback = $"https://{parsed.Host}:{FailoverServicePort}";
                        endpoints[authority] = new EndpointInfo
                        {
                            Url = authority,
                            ServiceType = serviceType,
                            FallbackUrl = fallback
                        };
                        count++;
                        _log.Info($"  Failover '{fr.Name}': {authority} (fallback {fallback})");
                    }
                }
                catch
                {
                    // Skip malformed URIs
                }
            }
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
                _log.Info($"Checking recording server: {rs.Name}");

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
                            _log.Error($"Error reading driver settings for '{hw.Name}': {ex.Message}");
                        }

                        Uri parsed;
                        try { parsed = new Uri(address); }
                        catch { continue; }

                        string url;
                        bool addressIsHttps = parsed.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase);

                        if (httpsEnabled)
                        {
                            url = httpsPort == 443
                                ? $"https://{parsed.Host}"
                                : $"https://{parsed.Host}:{httpsPort}";
                        }
                        else if (addressIsHttps)
                        {
                            // Fallback: driver has no HttpSEnabled flag but the Address itself is https://
                            url = parsed.IsDefaultPort
                                ? $"https://{parsed.Host}"
                                : $"https://{parsed.Host}:{parsed.Port}";
                            _log.Info($"  Hardware '{hw.Name}': using Address scheme (https://) fallback");
                        }
                        else
                        {
                            continue;
                        }

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
                            _log.Info($"  Hardware '{hw.Name}': {url}");
                        }
                    }
                    catch (Exception ex)
                    {
                        _log.Error($"Error processing hardware '{hw.Name}': {ex.Message}");
                    }
                }
            }

            _log.Info($"Discovered {count} hardware HTTPS endpoint(s)");
        }
    }
}
