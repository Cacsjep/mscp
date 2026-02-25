using System;

namespace RTMPStreamer
{
    /// <summary>
    /// Logging for the standalone helper process.
    /// Same API as the plugin's PluginLog so shared source files work in both contexts.
    /// Writes to stderr so the parent BackgroundPlugin can capture output.
    /// </summary>
    internal static class PluginLog
    {
        public static void Info(string message)
        {
            Console.Error.WriteLine($"{DateTime.Now:HH:mm:ss.fff} INFO  {message}");
        }

        public static void Error(string message)
        {
            Console.Error.WriteLine($"{DateTime.Now:HH:mm:ss.fff} ERROR {message}");
        }

        public static void Error(string message, Exception ex)
        {
            Console.Error.WriteLine($"{DateTime.Now:HH:mm:ss.fff} ERROR {message}: {ex.Message}");
        }
    }
}
