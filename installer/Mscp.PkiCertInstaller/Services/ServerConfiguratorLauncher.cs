using System;
using System.Diagnostics;
using System.IO;

namespace Mscp.PkiCertInstaller.Services;

// Milestone ships ServerConfigurator.exe as the canonical tool for
// pointing each service (Mgmt Server, Recording, Mobile, Event,
// Failover) at a thumbprint. We don't try to replace it - we just
// launch it and let the admin pick the cert we just dropped.
public static class ServerConfiguratorLauncher
{
    private static readonly string[] WellKnownPaths =
    {
        @"C:\Program Files\Milestone\Server Configurator\ServerConfigurator.exe",
        @"C:\Program Files (x86)\Milestone\Server Configurator\ServerConfigurator.exe",
        @"C:\Program Files\Milestone\XProtect Management Server\Tools\ServerConfigurator.exe",
        @"C:\Program Files\Milestone\XProtect Recording Server\Tools\ServerConfigurator.exe",
        @"C:\Program Files\Milestone\XProtect Mobile Server\Tools\ServerConfigurator.exe",
        @"C:\Program Files\Milestone\XProtect Event Server\Tools\ServerConfigurator.exe",
    };

    public static string? ResolvePath()
    {
        foreach (var p in WellKnownPaths)
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
