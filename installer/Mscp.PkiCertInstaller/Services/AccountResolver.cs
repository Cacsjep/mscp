using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Runtime.Versioning;
using System.Security.Principal;

namespace Mscp.PkiCertInstaller.Services;

// Enumerates the Windows accounts the install dialog can grant key
// access to: well-known SIDs (NETWORK SERVICE, etc.), local users and
// groups, plus the per-service virtual accounts (NT SERVICE\xxx). Used
// to back the account-picker UI and to validate manually-typed names.
[SupportedOSPlatform("windows")]
public static class AccountResolver
{
    public sealed record AccountEntry(string Name, string Kind);

    public static bool Exists(string accountName)
    {
        if (string.IsNullOrWhiteSpace(accountName)) return false;
        try
        {
            new NTAccount(accountName).Translate(typeof(SecurityIdentifier));
            return true;
        }
        catch { return false; }
    }

    public static IReadOnlyList<AccountEntry> Enumerate()
    {
        var list = new List<AccountEntry>();

        // The current interactive Windows identity sits at the very top
        // - it's almost always the admin running the installer, and
        // they shouldn't have to scroll past 200 service entries to
        // find themselves.
        try
        {
            var me = WindowsIdentity.GetCurrent().Name;
            if (!string.IsNullOrEmpty(me))
                list.Add(new AccountEntry(me, "Current user"));
        }
        catch { /* fallback: no current identity row */ }

        // Well-known service identities every Windows box has.
        foreach (var name in new[]
        {
            "NETWORK SERVICE",
            "LOCAL SERVICE",
            "SYSTEM",
            "Authenticated Users",
            "Users",
            "Administrators",
        })
        {
            list.Add(new AccountEntry(name, "Built-in"));
        }

        var machine = Environment.MachineName ?? "";

        TryQuery("SELECT Name FROM Win32_UserAccount WHERE LocalAccount=True", mo =>
        {
            var n = mo["Name"]?.ToString();
            if (!string.IsNullOrEmpty(n))
                list.Add(new AccountEntry($"{machine}\\{n}", "Local user"));
        });

        TryQuery("SELECT Name FROM Win32_Group WHERE LocalAccount=True", mo =>
        {
            var n = mo["Name"]?.ToString();
            if (!string.IsNullOrEmpty(n))
                list.Add(new AccountEntry($"{machine}\\{n}", "Local group"));
        });

        // NT SERVICE\<svc> virtual accounts. Useful when admins want to
        // grant a key to a specific service identity without messing with
        // group memberships. Only those services that actually run as
        // their own virtual account are practically relevant, but the
        // SCM accepts the form for any installed service - so we list
        // everything and let the user pick.
        TryQuery("SELECT Name, DisplayName FROM Win32_Service", mo =>
        {
            var n = mo["Name"]?.ToString();
            if (!string.IsNullOrEmpty(n))
            {
                var disp = mo["DisplayName"]?.ToString() ?? "";
                var kind = string.IsNullOrEmpty(disp) ? "Service account" : $"Service - {disp}";
                list.Add(new AccountEntry($"NT SERVICE\\{n}", kind));
            }
        });

        return list;
    }

    // Best-effort lookup of the Windows accounts the installed Milestone
    // XProtect™ services run under. Lets the install dialog pre-fill the
    // grant list with the *actual* service identity instead of the
    // hard-coded "NETWORK SERVICE" default - matters on installs where
    // the admin chose a domain service account, or on OEM rebrands where
    // the SCM StartName isn't the built-in. Returns an empty list if
    // nothing matches; caller should fall back to its hard-coded default
    // (and the user can still edit manually in the dialog).
    public static IReadOnlyList<string> DetectMilestoneServiceAccounts()
    {
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // DisplayName scan covers all the official Milestone service
        // names ("Milestone XProtect Recording Server", "... Management
        // Server", "... Event Server", "... Mobile Server", "... Log
        // Server", "... Data Collector Server"). PathName scan catches
        // OEM rebrands that kept the binary path but renamed the service.
        TryQuery(
            "SELECT Name, DisplayName, StartName, PathName FROM Win32_Service " +
            "WHERE DisplayName LIKE 'Milestone%' OR PathName LIKE '%Milestone%' " +
            "OR PathName LIKE '%XProtect%'",
            mo =>
            {
                var startName = mo["StartName"]?.ToString();
                var disp      = mo["DisplayName"]?.ToString() ?? "";
                var name      = mo["Name"]?.ToString() ?? "";
                var normalized = NormalizeAccount(startName);
                if (!string.IsNullOrEmpty(normalized))
                {
                    Log.Info($"Detected Milestone service '{name}' ({disp}) runs as '{startName}' -> '{normalized}'");
                    found.Add(normalized);
                }
            });
        return found.ToList();
    }

    // Maps SCM StartName forms to the friendly built-in names the
    // existing AccountRow / ACL code already understands. Domain
    // accounts (DOMAIN\user) and managed-service accounts (DOMAIN\svc$)
    // are returned as-is.
    private static string? NormalizeAccount(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var t = s.Trim();
        if (t.Equals("LocalSystem", StringComparison.OrdinalIgnoreCase))            return "SYSTEM";
        if (t.Equals(".\\LocalSystem", StringComparison.OrdinalIgnoreCase))         return "SYSTEM";
        if (t.Equals("NT AUTHORITY\\NetworkService", StringComparison.OrdinalIgnoreCase)) return "NETWORK SERVICE";
        if (t.Equals("NT AUTHORITY\\LocalService", StringComparison.OrdinalIgnoreCase))  return "LOCAL SERVICE";
        if (t.Equals("NT AUTHORITY\\System", StringComparison.OrdinalIgnoreCase))        return "SYSTEM";
        if (t.Equals("NetworkService", StringComparison.OrdinalIgnoreCase))         return "NETWORK SERVICE";
        if (t.Equals("LocalService", StringComparison.OrdinalIgnoreCase))           return "LOCAL SERVICE";
        return t;
    }

    private static void TryQuery(string wql, Action<ManagementObject> visit)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(wql);
            foreach (var item in searcher.Get())
            {
                if (item is ManagementObject mo)
                {
                    try { visit(mo); } catch { /* skip bad row */ }
                    finally { mo.Dispose(); }
                }
            }
        }
        catch
        {
            // WMI service down / blocked - return whatever we have.
        }
    }
}
