using System;
using System.Collections.Generic;
using System.Net;
using VideoOS.Platform;
using VideoOS.Platform.ConfigurationItems;

namespace PKI.Crypto
{
    public enum ServiceCategory
    {
        ManagementServer,
        RecordingServer,
        FailoverServer,
        EventServer,
        MobileServer,
        Other,
    }

    public class DiscoveredService
    {
        public ServiceCategory Category;
        public string DisplayName;             // Milestone-side label, e.g. "Recording01"
        public string Hostname;                // Hostname pulled from configuration
        public string Fqdn;                    // FQDN if reverse-resolution found one
        public List<string> IpAddresses = new List<string>();

        // Two-letter prefix matches what admins typically write into cert names:
        // RS = recording server, MS = management, FS = failover, EVS = event,
        // MOS = mobile. Keeps cert names short and sortable in the tree.
        public string Prefix
        {
            get
            {
                switch (Category)
                {
                    case ServiceCategory.ManagementServer: return "MS";
                    case ServiceCategory.RecordingServer:  return "RS";
                    case ServiceCategory.FailoverServer:   return "FS";
                    case ServiceCategory.EventServer:      return "EVS";
                    case ServiceCategory.MobileServer:     return "MOS";
                    default: return "SVC";
                }
            }
        }

        public string CategoryLabel
        {
            get
            {
                switch (Category)
                {
                    case ServiceCategory.ManagementServer: return "Management Server";
                    case ServiceCategory.RecordingServer:  return "Recording Server";
                    case ServiceCategory.FailoverServer:   return "Failover Server";
                    case ServiceCategory.EventServer:      return "Event Server";
                    case ServiceCategory.MobileServer:     return "Mobile Server";
                    default: return "Service";
                }
            }
        }

        public string SuggestedCertName
            => $"{Prefix} {(string.IsNullOrEmpty(DisplayName) ? Hostname : DisplayName)}";
    }

    // Walks the Milestone configuration tree to enumerate every server-side
    // service that admins typically front with TLS. Best-effort: each
    // discovery step is wrapped so a single broken kind doesn't kill the
    // whole list. DNS resolution is attempted for every result so the cert
    // can include hostname + IPs in its SAN extension.
    public static class ServiceDiscovery
    {
        public static List<DiscoveredService> Discover()
        {
            var result = new List<DiscoveredService>();
            // Dedup by (category, hostname) so an all-in-one install
            // (Mgmt + Recording + Event Server on the same box) still
            // gets one cert per *service*, not one cert for the box.
            // Per the Milestone Certificates Guide, each XProtect
            // service has its own slot in Server Configurator and
            // wants its own thumbprint.
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string Key(ServiceCategory cat, string host) => cat + "|" + (host ?? "");

            try { DiscoverManagementServer(result, seen, Key); }
            catch (Exception ex) { PKIDefinition.Log.Error($"Discover MS failed: {ex.Message}"); }

            try { DiscoverRecordingServers(result, seen, Key); }
            catch (Exception ex) { PKIDefinition.Log.Error($"Discover RS failed: {ex.Message}"); }

            try { DiscoverFailoverServers(result, seen, Key); }
            catch (Exception ex) { PKIDefinition.Log.Error($"Discover FS failed: {ex.Message}"); }

            try { DiscoverRegisteredServices(result, seen, Key); }
            catch (Exception ex) { PKIDefinition.Log.Error($"Discover registered services failed: {ex.Message}"); }

            foreach (var svc in result) ResolveAddresses(svc);
            return result;
        }

        private static void DiscoverManagementServer(List<DiscoveredService> result,
            HashSet<string> seen, Func<ServiceCategory, string, string> key)
        {
            var sid = EnvironmentManager.Instance.MasterSite.ServerId;
            var host = sid?.ServerHostname;
            if (string.IsNullOrEmpty(host)) return;
            if (!seen.Add(key(ServiceCategory.ManagementServer, host))) return;
            result.Add(new DiscoveredService
            {
                Category = ServiceCategory.ManagementServer,
                DisplayName = host,
                Hostname = host,
            });
        }

        // Walks ManagementServer.RecordingServerFolder.RecordingServers
        // to find every Recording Server registered on this site (the
        // "Recording Servers" folder in Mgmt Client). This is the same
        // approach CertWatchdog uses to enumerate them. Falls back to
        // Kind.Server item lookup if the typed folder is unavailable
        // for any reason.
        private static void DiscoverRecordingServers(List<DiscoveredService> result,
            HashSet<string> seen, Func<ServiceCategory, string, string> key)
        {
            var site = EnvironmentManager.Instance.MasterSite;
            var management = new ManagementServer(site);
            foreach (var rs in management.RecordingServerFolder.RecordingServers)
            {
                if (rs == null) continue;
                if (!rs.Enabled) continue;

                string host = null;
                try { host = rs.HostName; } catch { }
                if (string.IsNullOrEmpty(host))
                {
                    // Fall back to parsing the published WebServerUri.
                    var uri = string.IsNullOrEmpty(rs.ActiveWebServerUri) ? rs.WebServerUri : rs.ActiveWebServerUri;
                    if (!string.IsNullOrEmpty(uri))
                    {
                        try { host = new Uri(uri).Host; } catch { }
                    }
                }
                if (string.IsNullOrEmpty(host)) continue;
                if (!seen.Add(key(ServiceCategory.RecordingServer, host))) continue;
                result.Add(new DiscoveredService
                {
                    Category = ServiceCategory.RecordingServer,
                    DisplayName = string.IsNullOrEmpty(rs.Name) ? host : rs.Name,
                    Hostname = host,
                });
            }
        }

        private static void DiscoverFailoverServers(List<DiscoveredService> result,
            HashSet<string> seen, Func<ServiceCategory, string, string> key)
        {
            var site = EnvironmentManager.Instance.MasterSite;
            var management = new ManagementServer(site);
            foreach (var group in management.FailoverGroupFolder.FailoverGroups)
            {
                foreach (var fr in group.FailoverRecorderFolder.FailoverRecorders)
                {
                    if (!fr.Enabled) continue;
                    var url = string.IsNullOrEmpty(fr.ActiveWebServerUri) ? fr.WebServerUri : fr.ActiveWebServerUri;
                    if (string.IsNullOrEmpty(url)) continue;
                    string host;
                    try { host = new Uri(url).Host; }
                    catch { continue; }
                    if (string.IsNullOrEmpty(host)) continue;
                    if (!seen.Add(key(ServiceCategory.FailoverServer, host))) continue;
                    result.Add(new DiscoveredService
                    {
                        Category = ServiceCategory.FailoverServer,
                        DisplayName = fr.Name ?? host,
                        Hostname = host,
                    });
                }
            }
        }

        // Event Server and Mobile Server don't have ConfigurationItem
        // wrappers, but they register their public URIs under the master site.
        // We surface every registered service: known names get a typed
        // category (Event / Mobile), everything else lands as "Other" so
        // admins can still pick services we couldn't classify (e.g. Mobile
        // Server installs that register under a vendor-specific name).
        private static void DiscoverRegisteredServices(List<DiscoveredService> result,
            HashSet<string> seen, Func<ServiceCategory, string, string> key)
        {
            var sid = EnvironmentManager.Instance.MasterSite.ServerId;
            var infos = Configuration.Instance.GetRegisteredServiceUriInfo(sid);
            if (infos == null) return;

            foreach (var info in infos)
            {
                if (info.UriArray == null) continue;
                PKIDefinition.Log.Info($"Registered service: '{info.Name}' uris={info.UriArray.Count}");

                if (ShouldIgnoreRegistered(info.Name))
                {
                    PKIDefinition.Log.Info($"Skipping registered service '{info.Name}' (not cert-relevant)");
                    continue;
                }

                var category = ClassifyRegistered(info.Name);
                // Anything we can't classify into a known XProtect role
                // is skipped by default - the API Gateway, "MIP", and
                // other infrastructure services don't get their own
                // cert and would just clutter the auto-setup output.
                if (category == ServiceCategory.Other) continue;

                foreach (var uri in info.UriArray)
                {
                    if (string.IsNullOrEmpty(uri)) continue;
                    string host;
                    try { host = new Uri(uri).Host; }
                    catch { continue; }
                    if (string.IsNullOrEmpty(host)) continue;
                    if (!seen.Add(key(category, host))) continue;
                    result.Add(new DiscoveredService
                    {
                        Category = category,
                        DisplayName = info.Name ?? host,
                        Hostname = host,
                    });
                    break; // first usable URI per registered service is enough
                }
            }
        }

        private static ServiceCategory ClassifyRegistered(string name)
        {
            var n = (name ?? "").ToLowerInvariant();
            if (n.Contains("event server")) return ServiceCategory.EventServer;
            // Mobile Server registers under several names depending on
            // version: "Mobile Server", "MobileServer", "Mobile",
            // "SmartConnect", or the legacy "MIPS" / "MIP" service. Any of
            // those map to MobileServer.
            if (n.Contains("mobile") || n.Contains("smartconnect") || n.StartsWith("mips") || n == "mip")
                return ServiceCategory.MobileServer;
            return ServiceCategory.Other;
        }

        // Hard skip-list for registered service names that are
        // infrastructure (not user-facing TLS endpoints) and so don't
        // need their own thumbprint. The API Gateway in particular
        // reuses the Mgmt Server's cert; offering it as an auto-setup
        // target produces a duplicate cert and confuses Server
        // Configurator.
        private static bool ShouldIgnoreRegistered(string name)
        {
            if (string.IsNullOrEmpty(name)) return true;
            var n = name.Trim().ToLowerInvariant();
            return n == "api"
                || n.Contains("api gateway")
                || n == "log server"
                || n == "logserver"
                || n.Contains("data collector");
        }

        private static void ResolveAddresses(DiscoveredService svc)
        {
            if (string.IsNullOrEmpty(svc.Hostname)) return;
            try
            {
                var entry = Dns.GetHostEntry(svc.Hostname);
                if (entry?.HostName != null
                    && !string.Equals(entry.HostName, svc.Hostname, StringComparison.OrdinalIgnoreCase))
                {
                    svc.Fqdn = entry.HostName;
                }
                foreach (var addr in (entry?.AddressList ?? new IPAddress[0]))
                {
                    if (IPAddress.IsLoopback(addr)) continue;
                    if (addr.IsIPv6LinkLocal || addr.IsIPv6SiteLocal || addr.IsIPv6Multicast) continue;
                    var s = addr.ToString();
                    if (!svc.IpAddresses.Contains(s)) svc.IpAddresses.Add(s);
                }
            }
            catch
            {
                // Fall back to forward-only.
                try
                {
                    foreach (var addr in Dns.GetHostAddresses(svc.Hostname))
                    {
                        if (IPAddress.IsLoopback(addr)) continue;
                        var s = addr.ToString();
                        if (!svc.IpAddresses.Contains(s)) svc.IpAddresses.Add(s);
                    }
                }
                catch { /* host doesn't resolve - SAN will only carry the DNS name */ }
            }
        }
    }
}
