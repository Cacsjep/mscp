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
            try { LogInternal(false, message, null); } catch { }
        }

        public void Error(string message)
        {
            try { LogInternal(true, message, null); } catch { }
        }

        public void Error(string message, Exception ex)
        {
            try { LogInternal(true, message, ex); } catch { }
        }

        // Kept separate so callers above can catch assembly-load failures (e.g. in
        // unit-test processes that don't have VideoOS.Platform available). The JIT
        // compiles this method lazily on first call, so any TypeLoad/FileNotFound
        // exception surfaces inside the try/catch above rather than during JIT of
        // the public entry points.
        private void LogInternal(bool isError, string message, Exception ex)
        {
            if (ex != null)
                EnvironmentManager.Instance.Log(isError, _category, message, new[] { ex });
            else
                EnvironmentManager.Instance.Log(isError, _category, message);
        }
    }
}
