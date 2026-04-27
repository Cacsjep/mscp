using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace Mscp.PkiCertInstaller.Services;

// Milestone ships ServerConfigurator.exe as the canonical tool for
// pointing each service (Mgmt Server, Recording, Mobile, Event,
// Failover) at a thumbprint. We don't try to replace it - we just
// launch it and let the admin pick the cert we just dropped.
//
// OEM rebrands (Siemens Siveillance, Mobotix HUB, etc.) ship the
// exact same Server Configurator under their own vendor folder.
// We probe Milestone first, then the known OEMs - whichever exists
// is what's installed on this box.
public static class ServerConfiguratorLauncher
{
    // Vendor folders that ship a "Server Configurator\ServerConfigurator.exe"
    // identical to the Milestone original (same MIP build, same UI). Order
    // is irrelevant - we stop at the first File.Exists hit.
    private static readonly string[] VendorRoots =
    {
        "Milestone",
        "Siemens",
        "Mobotix",
    };

    private static IEnumerable<string> EnumerateWellKnownPaths()
    {
        var pf  = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

        foreach (var vendor in VendorRoots)
        {
            // Standalone Server Configurator install path (preferred,
            // present on every flavour from 2020 R2 onwards).
            yield return Path.Combine(pf,  vendor, "Server Configurator", "ServerConfigurator.exe");
            yield return Path.Combine(pf86, vendor, "Server Configurator", "ServerConfigurator.exe");
        }

        // Legacy embedded paths under each XProtect service (pre-2020).
        // Milestone-only - OEM rebrands always ship the standalone path.
        yield return @"C:\Program Files\Milestone\XProtect Management Server\Tools\ServerConfigurator.exe";
        yield return @"C:\Program Files\Milestone\XProtect Recording Server\Tools\ServerConfigurator.exe";
        yield return @"C:\Program Files\Milestone\XProtect Mobile Server\Tools\ServerConfigurator.exe";
        yield return @"C:\Program Files\Milestone\XProtect Event Server\Tools\ServerConfigurator.exe";
    }

    public static string? ResolvePath()
    {
        foreach (var p in EnumerateWellKnownPaths())
            if (File.Exists(p)) return p;
        return null;
    }

    public static bool TryLaunch(out string error)
    {
        error = "";
        var path = ResolvePath();
        if (path == null)
        {
            error = "Server Configurator not found. Install / repair an XProtect product first.";
            return false;
        }
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
                Verb = "runas",
            });
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }
}
