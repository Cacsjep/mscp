using System;
using System.Collections.Generic;
using VideoOS.Platform;
using VideoOS.Platform.Admin;
using VideoOS.Platform.Log;

namespace CommunitySDK
{
    /// <summary>
    /// Base class for writing structured entries to the Milestone System Log
    /// visible in Management Client. Handles dictionary registration, site
    /// item lookup and NewEntry boilerplate so plugins only define their
    /// message templates and typed convenience methods.
    /// </summary>
    public abstract class SystemLogBase
    {
        private readonly string _appId;
        private readonly string _componentId;
        private readonly PluginLog _log;
        private bool _registered;

        protected SystemLogBase(string appId, PluginLog log)
            : this(appId, appId, log) { }

        protected SystemLogBase(string appId, string componentId, PluginLog log)
        {
            _appId = appId;
            _componentId = componentId;
            _log = log;
        }

        protected abstract Dictionary<string, LogMessage> BuildMessages();

        public void Register()
        {
            if (_registered) return;

            try
            {
                var dict = new LogMessageDictionary(
                    culture: "en-US",
                    version: "1.0",
                    application: _appId,
                    component: _componentId,
                    logMessages: BuildMessages(),
                    resourceType: "text");

                LogClient.Instance.RegisterDictionary(dict);
                LogClient.Instance.SetCulture("en-US");
                _registered = true;
            }
            catch (Exception ex)
            {
                _log.Error($"Failed to register system log dictionary: {ex.Message}");
            }
        }

        protected void WriteEntry(string messageId, Dictionary<string, string> parameters = null)
        {
            if (!_registered) return;
            try
            {
                LogClient.Instance.NewEntry(_appId, _componentId, messageId, GetSiteItem(), parameters);
            }
            catch { }
        }

        private static Item GetSiteItem()
        {
            try
            {
                return EnvironmentManager.Instance.GetSiteItem(
                    EnvironmentManager.Instance.MasterSite);
            }
            catch { return null; }
        }
    }
}
