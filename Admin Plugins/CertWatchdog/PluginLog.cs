using System;
using VideoOS.Platform;

namespace CertWatchdog
{
    internal static class PluginLog
    {
        private const string Category = "CertWatchdog";

        public static void Info(string message)
        {
            EnvironmentManager.Instance.Log(false, Category, message);
        }

        public static void Error(string message)
        {
            EnvironmentManager.Instance.Log(true, Category, message);
        }

        public static void Error(string message, Exception ex)
        {
            EnvironmentManager.Instance.Log(true, Category, message, new[] { ex });
        }
    }
}
