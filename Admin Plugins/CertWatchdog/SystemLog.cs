using System.Collections.Generic;
using VideoOS.Platform;
using VideoOS.Platform.Admin;
using VideoOS.Platform.Log;

namespace CertWatchdog
{
    internal static class SystemLog
    {
        private const string AppId = "CertWatchdog";
        private const string ComponentId = "CertWatchdog";
        private const string Version = "1.0";
        private const string Culture = "en-US";
        private const string ResourceType = "text";

        private const string MsgPluginStarted = "PluginStarted";
        private const string MsgPluginStopped = "PluginStopped";
        private const string MsgCertExpiring = "CertExpiring";
        private const string MsgCertCheckComplete = "CertCheckComplete";
        private const string MsgCertCheckError = "CertCheckError";

        private static bool _registered;

        public static void Register()
        {
            if (_registered) return;

            try
            {
                var messages = new Dictionary<string, LogMessage>
                {
                    [MsgPluginStarted] = new LogMessage
                    {
                        Id = MsgPluginStarted,
                        Group = Group.System,
                        Severity = Severity.Info,
                        Status = Status.Success,
                        RelatedObjectKind = Kind.Server,
                        Category = Category.VideoOut.ToString(),
                        CategoryName = "Certificate Monitoring",
                        Message = "Certificate Watchdog started, monitoring {p1} endpoint(s)"
                    },
                    [MsgPluginStopped] = new LogMessage
                    {
                        Id = MsgPluginStopped,
                        Group = Group.System,
                        Severity = Severity.Info,
                        Status = Status.StatusQuo,
                        RelatedObjectKind = Kind.Server,
                        Category = Category.VideoOut.ToString(),
                        CategoryName = "Certificate Monitoring",
                        Message = "Certificate Watchdog stopped"
                    },
                    [MsgCertExpiring] = new LogMessage
                    {
                        Id = MsgCertExpiring,
                        Group = Group.System,
                        Severity = Severity.Warning,
                        Status = Status.Failure,
                        RelatedObjectKind = Kind.Server,
                        Category = Category.VideoOut.ToString(),
                        CategoryName = "Certificate Monitoring",
                        Message = "Certificate for '{p1}' expires in {p2} days"
                    },
                    [MsgCertCheckComplete] = new LogMessage
                    {
                        Id = MsgCertCheckComplete,
                        Group = Group.System,
                        Severity = Severity.Info,
                        Status = Status.Success,
                        RelatedObjectKind = Kind.Server,
                        Category = Category.VideoOut.ToString(),
                        CategoryName = "Certificate Monitoring",
                        Message = "Certificate check complete: {p1} endpoints checked, {p2} expiring"
                    },
                    [MsgCertCheckError] = new LogMessage
                    {
                        Id = MsgCertCheckError,
                        Group = Group.System,
                        Severity = Severity.Error,
                        Status = Status.Failure,
                        RelatedObjectKind = Kind.Server,
                        Category = Category.VideoOut.ToString(),
                        CategoryName = "Certificate Monitoring",
                        Message = "Certificate check failed for '{p1}': {p2}"
                    }
                };

                var dict = new LogMessageDictionary(
                    culture: Culture,
                    version: Version,
                    application: AppId,
                    component: ComponentId,
                    logMessages: messages,
                    resourceType: ResourceType);

                LogClient.Instance.RegisterDictionary(dict);
                LogClient.Instance.SetCulture(Culture);

                _registered = true;
            }
            catch (System.Exception ex)
            {
                PluginLog.Error($"Failed to register system log dictionary: {ex.Message}");
            }
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

        public static void PluginStarted(int endpointCount)
        {
            if (!_registered) return;
            try
            {
                LogClient.Instance.NewEntry(AppId, ComponentId, MsgPluginStarted,
                    GetSiteItem(),
                    new Dictionary<string, string> { ["p1"] = endpointCount.ToString() });
            }
            catch { }
        }

        public static void PluginStopped()
        {
            if (!_registered) return;
            try
            {
                LogClient.Instance.NewEntry(AppId, ComponentId, MsgPluginStopped,
                    GetSiteItem(), null);
            }
            catch { }
        }

        public static void CertExpiring(string endpoint, int daysLeft)
        {
            if (!_registered) return;
            try
            {
                LogClient.Instance.NewEntry(AppId, ComponentId, MsgCertExpiring,
                    GetSiteItem(),
                    new Dictionary<string, string>
                    {
                        ["p1"] = endpoint,
                        ["p2"] = daysLeft.ToString()
                    });
            }
            catch { }
        }

        public static void CertCheckComplete(int totalChecked, int expiringCount)
        {
            if (!_registered) return;
            try
            {
                LogClient.Instance.NewEntry(AppId, ComponentId, MsgCertCheckComplete,
                    GetSiteItem(),
                    new Dictionary<string, string>
                    {
                        ["p1"] = totalChecked.ToString(),
                        ["p2"] = expiringCount.ToString()
                    });
            }
            catch { }
        }

        public static void CertCheckError(string endpoint, string error)
        {
            if (!_registered) return;
            try
            {
                LogClient.Instance.NewEntry(AppId, ComponentId, MsgCertCheckError,
                    GetSiteItem(),
                    new Dictionary<string, string>
                    {
                        ["p1"] = endpoint,
                        ["p2"] = error
                    });
            }
            catch { }
        }
    }
}
