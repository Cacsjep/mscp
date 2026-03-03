using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using CertWatchdog.Messaging;
using CertWatchdog.Models;
using CertWatchdog.Services;
using VideoOS.Platform;
using VideoOS.Platform.Background;
using VideoOS.Platform.Data;
using VideoOS.Platform.Messaging;

namespace CertWatchdog.Background
{
    public class CertWatchdogBackgroundPlugin : BackgroundPlugin
    {
        private Timer _checkTimer;
        private volatile bool _closing;
        private readonly object _certLock = new object();
        private List<CertificateInfo> _lastResults = new List<CertificateInfo>();

        private MessageCommunication _mc;
        private object _requestFilter;
        private volatile bool _mcRegistered;

        // FQID of the auto-created plugin item (used as event source)
        private FQID _pluginItemFqid;

        // Thresholds for event firing (days)
        private static readonly int[] Thresholds = { 60, 30, 15 };

        // Track which thresholds have been fired per endpoint URL
        // Key: URL, Value: set of threshold days already fired
        private readonly Dictionary<string, HashSet<int>> _firedThresholds =
            new Dictionary<string, HashSet<int>>(StringComparer.OrdinalIgnoreCase);

        // Check interval: 6 hours
        private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(6);
        // Initial delay before first check: 30 seconds
        private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(30);

        public override Guid Id => CertWatchdogDefinition.BackgroundPluginId;
        public override string Name => "Certificate Watchdog Background";

        public override List<EnvironmentType> TargetEnvironments =>
            new List<EnvironmentType> { EnvironmentType.Service };

        public override void Init()
        {
            PluginLog.Info("Certificate Watchdog background plugin initializing");

            SystemLog.Register();

            // Auto-create the plugin item if it doesn't exist
            EnsurePluginItem();

            // Load previously fired thresholds from saved item properties
            LoadFiredThresholds();

            // Start MessageCommunication (session established asynchronously)
            try
            {
                var serverId = EnvironmentManager.Instance.MasterSite.ServerId;
                MessageCommunicationManager.Start(serverId);
                _mc = MessageCommunicationManager.Get(serverId);
            }
            catch (Exception ex)
            {
                PluginLog.Error($"Failed to init MessageCommunication: {ex.Message}", ex);
            }

            // Start the periodic check timer
            // Filter registration is deferred to the first timer tick when
            // the CommunicationService is operational
            _checkTimer = new Timer(OnCheckTimer, null, InitialDelay, CheckInterval);

            PluginLog.Info("Certificate Watchdog background plugin initialized");
        }

        public override void Close()
        {
            PluginLog.Info("Certificate Watchdog background plugin closing");
            _closing = true;

            _checkTimer?.Dispose();
            _checkTimer = null;

            if (_mc != null && _requestFilter != null)
            {
                _mc.UnRegisterCommunicationFilter(_requestFilter);
                _requestFilter = null;
            }
            _mc = null;

            SystemLog.PluginStopped();
        }

        private void EnsurePluginItem()
        {
            try
            {
                var items = Configuration.Instance.GetItemConfigurations(
                    CertWatchdogDefinition.PluginId, null, CertWatchdogDefinition.CertWatchdogKindId);

                if (items != null && items.Count > 0)
                {
                    _pluginItemFqid = items[0].FQID;
                    return;
                }

                PluginLog.Info("Creating Certificate Watchdog item");
                var fqid = new FQID(
                    EnvironmentManager.Instance.MasterSite.ServerId,
                    Guid.Empty,
                    Guid.NewGuid(),
                    FolderType.No,
                    CertWatchdogDefinition.CertWatchdogKindId);

                var item = new Item(fqid, "Certificate Watchdog");
                item.Properties["CheckIntervalHours"] = "6";
                Configuration.Instance.SaveItemConfiguration(CertWatchdogDefinition.PluginId, item);
                _pluginItemFqid = fqid;
                PluginLog.Info("Certificate Watchdog item created");
            }
            catch (Exception ex)
            {
                PluginLog.Error($"Failed to ensure plugin item: {ex.Message}", ex);
            }
        }

        private void OnCheckTimer(object state)
        {
            if (_closing) return;

            // Deferred MessageCommunication filter registration
            // CommunicationService is not operational during Init(),
            // so we register on the first timer tick (~30s later)
            EnsureMessageCommunicationFilter();

            try
            {
                PerformCertCheck();
            }
            catch (Exception ex)
            {
                PluginLog.Error($"Certificate check failed: {ex.Message}", ex);
            }
        }

        private void EnsureMessageCommunicationFilter()
        {
            if (_mcRegistered || _mc == null) return;

            try
            {
                _requestFilter = _mc.RegisterCommunicationFilter(
                    OnCertDataRequest,
                    new CommunicationIdFilter(CertMessageIds.CertDataRequest));
                _mcRegistered = true;
                PluginLog.Info("MessageCommunication filter registered for cert data requests");
            }
            catch (Exception ex)
            {
                PluginLog.Error($"Failed to register MessageCommunication filter: {ex.Message}", ex);
            }
        }

        private void PerformCertCheck()
        {
            PluginLog.Info("Starting certificate check");

            // Discover endpoints
            var endpoints = EndpointDiscoveryService.DiscoverHttpsEndpoints();
            PluginLog.Info($"Discovered {endpoints.Count} HTTPS endpoint(s)");

            if (endpoints.Count == 0)
            {
                SystemLog.PluginStarted(0);
                return;
            }

            // Check all certificates
            var results = CertificateCheckService.CheckAllCertificates(endpoints);

            lock (_certLock)
            {
                _lastResults = results;
            }

            // Count expiring certs
            var expiringCount = results.Count(c =>
                c.Status == CertStatus.Expiring ||
                c.Status == CertStatus.Critical ||
                c.Status == CertStatus.Expired);

            // Fire events for expiring certs
            foreach (var cert in results)
            {
                if (cert.Status == CertStatus.Error) continue;

                foreach (var threshold in Thresholds)
                {
                    if (cert.DaysLeft <= threshold)
                    {
                        FireThresholdEvent(cert, threshold);
                    }
                }
            }

            // Persist fired thresholds
            SaveFiredThresholds();

            SystemLog.CertCheckComplete(results.Count, expiringCount);
            PluginLog.Info($"Certificate check complete: {results.Count} checked, {expiringCount} expiring");
        }

        private void FireThresholdEvent(CertificateInfo cert, int thresholdDays)
        {
            // Check if already fired for this endpoint+threshold
            if (!_firedThresholds.TryGetValue(cert.Url, out var thresholds))
            {
                thresholds = new HashSet<int>();
                _firedThresholds[cert.Url] = thresholds;
            }

            if (thresholds.Contains(thresholdDays))
                return; // Already fired

            thresholds.Add(thresholdDays);

            // Determine event type GUID based on threshold
            Guid eventTypeId;
            switch (thresholdDays)
            {
                case 60:
                    eventTypeId = CertWatchdogDefinition.EventType60DaysId;
                    break;
                case 30:
                    eventTypeId = CertWatchdogDefinition.EventType30DaysId;
                    break;
                case 15:
                    eventTypeId = CertWatchdogDefinition.EventType15DaysId;
                    break;
                default:
                    return;
            }

            try
            {
                var header = new EventHeader
                {
                    ID = eventTypeId,
                    Class = "Analytics",
                    Type = $"CertExpire{thresholdDays}Days",
                    Name = $"Cert Expire ({thresholdDays} Days)",
                    Message = $"Certificate for '{cert.Endpoint}' expires in {cert.DaysLeft} days (threshold: {thresholdDays}d)",
                    CustomTag = cert.Url,
                    Priority = 3,
                    Source = new EventSource
                    {
                        Name = cert.Endpoint,
                        FQID = _pluginItemFqid
                    }
                };

                var analyticsEvent = new AnalyticsEvent
                {
                    EventHeader = header
                };

                var eventMessage = new Message(MessageId.Server.NewEventIndication)
                {
                    Data = analyticsEvent
                };

                EnvironmentManager.Instance.PostMessage(eventMessage);

                SystemLog.CertExpiring(cert.Endpoint, cert.DaysLeft);
                PluginLog.Info($"Fired {thresholdDays}-day event for {cert.Endpoint} ({cert.DaysLeft} days left)");
            }
            catch (Exception ex)
            {
                PluginLog.Error($"Failed to fire event for {cert.Endpoint}: {ex.Message}", ex);
            }
        }

        private void LoadFiredThresholds()
        {
            try
            {
                var items = Configuration.Instance.GetItemConfigurations(
                    CertWatchdogDefinition.PluginId, null, CertWatchdogDefinition.CertWatchdogKindId);

                if (items == null || items.Count == 0) return;

                var item = items[0];
                foreach (var kvp in item.Properties)
                {
                    if (!kvp.Key.StartsWith("Fired_")) continue;

                    var url = kvp.Key.Substring(6);
                    var thresholdValues = kvp.Value.Split(',');
                    var thresholdSet = new HashSet<int>();

                    foreach (var v in thresholdValues)
                    {
                        if (int.TryParse(v, out var t))
                            thresholdSet.Add(t);
                    }

                    _firedThresholds[url] = thresholdSet;
                }
            }
            catch (Exception ex)
            {
                PluginLog.Error($"Failed to load fired thresholds: {ex.Message}", ex);
            }
        }

        private void SaveFiredThresholds()
        {
            try
            {
                var items = Configuration.Instance.GetItemConfigurations(
                    CertWatchdogDefinition.PluginId, null, CertWatchdogDefinition.CertWatchdogKindId);

                if (items == null || items.Count == 0) return;

                var item = items[0];

                // Clear old fired entries
                var keysToRemove = new List<string>();
                foreach (var key in item.Properties.Keys)
                {
                    if (key.StartsWith("Fired_"))
                        keysToRemove.Add(key);
                }
                foreach (var key in keysToRemove)
                    item.Properties.Remove(key);

                // Write current fired thresholds
                foreach (var kvp in _firedThresholds)
                {
                    var key = "Fired_" + kvp.Key;
                    var value = string.Join(",", kvp.Value);
                    item.Properties[key] = value;
                }

                Configuration.Instance.SaveItemConfiguration(CertWatchdogDefinition.PluginId, item);
            }
            catch (Exception ex)
            {
                PluginLog.Error($"Failed to save fired thresholds: {ex.Message}", ex);
            }
        }

        private object OnCertDataRequest(Message message, FQID dest, FQID source)
        {
            var request = message.Data as CertDataRequest;
            if (request == null) return null;

            List<CertificateInfo> certs;
            lock (_certLock)
            {
                certs = new List<CertificateInfo>(_lastResults);
            }

            var response = new CertDataResponse
            {
                RequestId = request.RequestId,
                Certificates = certs,
                Timestamp = DateTime.UtcNow
            };

            var mc = _mc;
            if (mc != null)
            {
                try
                {
                    mc.TransmitMessage(
                        new Message(CertMessageIds.CertDataResponse, response), null, null, null);
                }
                catch (Exception ex)
                {
                    PluginLog.Error($"Failed to send cert data response: {ex.Message}");
                }
            }

            return null;
        }
    }
}
