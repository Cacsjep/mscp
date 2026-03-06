using System;

namespace CommunitySDK
{
    /// <summary>
    /// Standalone helper replacement for CommunitySDK.PluginLog.
    /// Same instance-based API so shared source files compile in both contexts.
    /// Writes to stderr so the parent BackgroundPlugin can capture output.
    /// </summary>
    internal class PluginLog
    {
        private readonly string _category;

        public PluginLog(string category)
        {
            _category = category;
        }

        public void Info(string message)
        {
            Console.Error.WriteLine($"{DateTime.Now:HH:mm:ss.fff} INFO  [{_category}] {message}");
        }

        public void Error(string message)
        {
            Console.Error.WriteLine($"{DateTime.Now:HH:mm:ss.fff} ERROR [{_category}] {message}");
        }

        public void Error(string message, Exception ex)
        {
            Console.Error.WriteLine($"{DateTime.Now:HH:mm:ss.fff} ERROR [{_category}] {message}: {ex.Message}");
        }
    }
}
