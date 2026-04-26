using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;

namespace Mscp.PkiCertInstaller.Services;

// Snapshot of how this Windows host identifies itself on the network.
// Used by CertItemViewModel.MatchesMachine to decide whether a cert
// "belongs" here. We compute it once at startup; the answers don't
// change for the lifetime of the installer.
public static class MachineIdentity
{
    public static string Hostname { get; }
    public static string Fqdn     { get; }
    public static IReadOnlyList<IPAddress> IpAddresses { get; }

    static MachineIdentity()
    {
        Hostname = Environment.MachineName ?? "";
        string fqdn = "";
        try { fqdn = Dns.GetHostEntry("").HostName ?? ""; } catch { /* offline */ }
        Fqdn = fqdn;

        var ips = new List<IPAddress>();
        try
        {
            foreach (var a in Dns.GetHostAddresses(""))
            {
                if (a.AddressFamily != AddressFamily.InterNetwork
                 && a.AddressFamily != AddressFamily.InterNetworkV6) continue;
                if (IPAddress.IsLoopback(a)) continue;
                if (a.IsIPv6LinkLocal) continue;
                ips.Add(a);
            }
        }
        catch { /* offline */ }
        IpAddresses = ips;
    }

    public static bool DnsMatches(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        var n = name.Trim();
        if (string.Equals(n, Hostname, StringComparison.OrdinalIgnoreCase)) return true;
        if (!string.IsNullOrEmpty(Fqdn) &&
            string.Equals(n, Fqdn, StringComparison.OrdinalIgnoreCase)) return true;

        // Wildcard cert (e.g. "*.example.local") matches our FQDN if our
        // FQDN is a single-label child of the wildcard's parent domain.
        if (n.StartsWith("*.", StringComparison.Ordinal) &&
            !string.IsNullOrEmpty(Fqdn))
        {
            var parent = n[2..];
            // FQDN must be exactly one label deeper than the parent.
            if (Fqdn.EndsWith("." + parent, StringComparison.OrdinalIgnoreCase) &&
                !Fqdn[..^(parent.Length + 1)].Contains('.'))
                return true;
        }
        return false;
    }

    public static bool IpMatches(string ip)
        => IPAddress.TryParse((ip ?? "").Trim(), out var addr) &&
           IpAddresses.Any(a => a.Equals(addr));
}
