using Avalonia;
using Mscp.PkiCertInstaller.Services;
using System;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Mscp.PkiCertInstaller;

internal static class Program
{
    // Undocumented uxtheme.dll ordinal 135 (SetPreferredAppMode) on
    // Win10 1809+. Calling this BEFORE any window is created tells the
    // OS to render system theming for this process in dark mode from
    // the very first paint, so the login window's title bar is black
    // immediately instead of flashing grey for ~3 seconds while the
    // DwmSetWindowAttribute call catches up.
    [DllImport("uxtheme.dll", EntryPoint = "#135", PreserveSig = true)]
    private static extern int SetPreferredAppMode(int mode);
    private const int APPMODE_FORCE_DARK = 2;

    [STAThread]
    public static void Main(string[] args)
    {
        try { SetPreferredAppMode(APPMODE_FORCE_DARK); } catch { /* not Windows */ }

        var ver = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "?";
        Log.Info($"=== Cert Installer start (v{ver}, {Environment.OSVersion}, machine={Environment.MachineName}) ===");

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Log.Error("Unhandled domain exception", e.ExceptionObject as Exception);

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            Log.Error("Avalonia lifetime crashed", ex);
            throw;
        }
        finally
        {
            Log.Info("=== Cert Installer exit ===");
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
