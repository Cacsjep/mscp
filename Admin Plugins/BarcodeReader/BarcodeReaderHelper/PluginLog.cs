using System;

namespace CommunitySDK
{
    /// <summary>
    /// Standalone replacement for CommunitySDK.PluginLog for helper processes.
    /// Writes to stderr so the parent BackgroundPlugin captures every line.
    /// Same instance API as the real CommunitySDK.PluginLog so shared files compile
    /// in either host unchanged.
    /// </summary>
    internal class PluginLog
    {
        private readonly string _category;

        public PluginLog(string category) { _category = category; }

        public void Info(string message)
            => Console.Error.WriteLine($"{DateTime.Now:HH:mm:ss.fff} INFO  [{_category}] {message}");

        public void Error(string message)
            => Console.Error.WriteLine($"{DateTime.Now:HH:mm:ss.fff} ERROR [{_category}] {message}");

        public void Error(string message, Exception ex)
            => Console.Error.WriteLine($"{DateTime.Now:HH:mm:ss.fff} ERROR [{_category}] {message}: {ex.Message}");
    }
}
