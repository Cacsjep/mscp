using System;
using VideoOS.Platform;

namespace RtmpStreamer
{
    /// <summary>
    /// Centralized logging helper using the Milestone SDK logging API.
    /// Logs go to the XProtect log system and are visible in the Management Client logs.
    /// </summary>
    internal static class PluginLog
    {
        private const string Category = "RtmpStreamer";

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
