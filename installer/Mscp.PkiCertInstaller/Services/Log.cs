using System;
using System.IO;
using System.Text;

namespace Mscp.PkiCertInstaller.Services;

// Minimal rotating file logger. We deliberately avoid Serilog/NLog so
// the single-file installer stays small. Output sits next to other
// per-user diagnostics under %LOCALAPPDATA%\MSCP\PkiCertInstaller\logs
// so admins can grab it for GH issues without admin rights.
//
// Rotation: when the active log exceeds MaxBytes, it's renamed to
// installer.1.log (rolling over .1→.2, .2→.3, ...) and a fresh
// installer.log is opened. Up to MaxFiles rotations are kept.
public static class Log
{
    private const long MaxBytes = 1_000_000;   // 1 MB
    private const int  MaxFiles = 5;

    private static readonly object _lock = new();
    private static readonly string _dir;
    private static readonly string _path;

    static Log()
    {
        _dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MSCP", "PkiCertInstaller", "logs");
        _path = Path.Combine(_dir, "installer.log");
        try { Directory.CreateDirectory(_dir); } catch { /* best effort */ }
    }

    public static string LogDirectory => _dir;
    public static string LogFile => _path;

    public static void Info (string msg)            => Write("INFO ", msg, null);
    public static void Warn (string msg)            => Write("WARN ", msg, null);
    public static void Error(string msg, Exception? ex = null) => Write("ERROR", msg, ex);

    private static void Write(string level, string msg, Exception? ex)
    {
        try
        {
            lock (_lock)
            {
                RotateIfNeeded();
                var sb = new StringBuilder();
                sb.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"))
                  .Append(' ').Append(level)
                  .Append(' ').Append(msg);
                if (ex != null)
                {
                    sb.Append(" | ").Append(ex.GetType().Name)
                      .Append(": ").Append(ex.Message);
                    if (ex.StackTrace != null)
                        sb.Append(Environment.NewLine).Append(ex.StackTrace);
                }
                sb.Append(Environment.NewLine);
                File.AppendAllText(_path, sb.ToString(), Encoding.UTF8);
            }
        }
        catch
        {
            // Logging must never throw - if the disk is full or the
            // log file is locked we silently drop the message rather
            // than crash the UI.
        }
    }

    private static void RotateIfNeeded()
    {
        try
        {
            if (!File.Exists(_path)) return;
            var len = new FileInfo(_path).Length;
            if (len < MaxBytes) return;

            // Roll over .N-1 → .N from oldest to newest, then drop the
            // active log to .1.
            var oldest = $"{_path}.{MaxFiles}";
            if (File.Exists(oldest)) File.Delete(oldest);
            for (int i = MaxFiles - 1; i >= 1; i--)
            {
                var src = $"{_path}.{i}";
                var dst = $"{_path}.{i + 1}";
                if (File.Exists(src)) File.Move(src, dst);
            }
            File.Move(_path, $"{_path}.1");
        }
        catch
        {
            // If rotation fails we just keep appending - better a big
            // log than a lost log.
        }
    }
}
