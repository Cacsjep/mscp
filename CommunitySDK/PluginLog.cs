using System;
using VideoOS.Platform;

namespace CommunitySDK
{
    /// <summary>
    /// Shared logging helper using the Milestone SDK logging API.
    /// Supports both static (single-category) and instance-based (per-component) usage.
    /// </summary>
    public class PluginLog
    {
        private readonly string _category;

        public PluginLog(string category)
        {
            _category = category;
        }

        public void Info(string message)
        {
            EnvironmentManager.Instance.Log(false, _category, message);
        }

        public void Error(string message)
        {
            EnvironmentManager.Instance.Log(true, _category, message);
        }

        public void Error(string message, Exception ex)
        {
            EnvironmentManager.Instance.Log(true, _category, message, new[] { ex });
        }
    }
}
