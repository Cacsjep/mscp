using System;
using System.IO;

namespace CommunitySDK
{
    /// <summary>
    /// Standalone replacement for CommunitySDK.PluginLog for helper processes.
    /// Writes to stderr so the parent BackgroundPlugin captures every line, and
    /// optionally to a file on disk so the trail survives the parent losing the
    /// pipe (e.g. helper APPCRASH where stderr gets truncated before flush).
    /// Same instance API as the real CommunitySDK.PluginLog so shared files
    /// compile in either host unchanged.
    /// </summary>
    internal class PluginLog
    {
        private readonly string _category;
        private readonly object _fileLock = new object();
        private string _logFilePath;

        // 5 MB before we rotate to .1. Helpers per channel get their own file so
        // the cap is per-channel, not aggregate.
        private const long MaxFileBytes = 5 * 1024 * 1024;

        public PluginLog(string category) { _category = category; }

        /// <summary>
        /// Start mirroring all writes to the given file path (in addition to stderr).
        /// Idempotent and best-effort: if the directory can't be created or the file
        /// can't be opened, file logging is silently disabled but stderr still works.
        /// </summary>
        public void AttachFile(string logFilePath)
        {
            if (string.IsNullOrEmpty(logFilePath)) return;
            try
            {
                var dir = Path.GetDirectoryName(logFilePath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                _logFilePath = logFilePath;
            }
            catch
            {
                _logFilePath = null;
            }
        }

        public void Info(string message) => Write("INFO ", message, null);
        public void Error(string message) => Write("ERROR", message, null);

        public void Error(string message, Exception ex)
            => Write("ERROR", message + ": " + (ex?.Message ?? ""), ex);

        private void Write(string level, string message, Exception ex)
        {
            var line = $"{DateTime.Now:HH:mm:ss.fff} {level} [{_category}] {message}";
            try { Console.Error.WriteLine(line); } catch { }
            if (ex != null)
            {
                try { Console.Error.WriteLine(ex.ToString()); } catch { }
            }
            WriteFile(line, ex);
        }

        private void WriteFile(string line, Exception ex)
        {
            var path = _logFilePath;
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                lock (_fileLock)
                {
                    RotateIfNeeded(path);
                    using (var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read))
                    using (var sw = new StreamWriter(fs))
                    {
                        sw.WriteLine(line);
                        if (ex != null) sw.WriteLine(ex.ToString());
                    }
                }
            }
            catch { /* file logging is best-effort */ }
        }

        private static void RotateIfNeeded(string path)
        {
            try
            {
                var fi = new FileInfo(path);
                if (!fi.Exists || fi.Length < MaxFileBytes) return;
                var rotated = path + ".1";
                if (File.Exists(rotated)) File.Delete(rotated);
                File.Move(path, rotated);
            }
            catch { }
        }
    }
}
