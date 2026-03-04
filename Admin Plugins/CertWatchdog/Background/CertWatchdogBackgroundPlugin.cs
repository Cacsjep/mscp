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
        private object _recollectFilter;
        private volatile bool _mcRegistered;

        // Config change receivers
        private object _hardwareChangeFilter;
        private object _serverChangeFilter;
        private Timer _configChangeDebounce;

        // Kind GUIDs for config change filtering
        private static readonly Guid KindHardware = new Guid("C9DDC944-FB27-4E34-88F8-76023FCC9CE9");
        private static readonly Guid KindServer = new Guid("3B25FE94-7C2F-499a-86DF-2FA68AA3E1B5");

        // FQID of the auto-created plugin item (used as event source)
        private FQID _pluginItemFqid;

        // Thresholds for event firing (days)
        private static readonly int[] Thresholds = { 60, 30, 15 };

        // Track which thresholds have been fired per endpoint URL (in-memory only, resets on restart)
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

            // Register config change receivers for Hardware and Server kinds
            RegisterConfigChangeReceivers();

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

            _configChangeDebounce?.Dispose();
            _configChangeDebounce = null;

            // Unregister config change receivers
            UnregisterConfigChangeReceivers();

            if (_mc != null)
            {
                if (_requestFilter != null)
                {
                    _mc.UnRegisterCommunicationFilter(_requestFilter);
                    _requestFilter = null;
                }
                if (_recollectFilter != null)
                {
                    _mc.UnRegisterCommunicationFilter(_recollectFilter);
                    _recollectFilter = null;
                }
            }
            _mc = null;

            SystemLog.PluginStopped();
        }

        private void RegisterConfigChangeReceivers()
        {
            try
            {
                _hardwareChangeFilter = EnvironmentManager.Instance.RegisterReceiver(
                    OnConfigChanged,
                    new MessageIdAndRelatedKindFilter(
                        MessageId.Server.ConfigurationChangedIndication,
                        KindHardware));

                _serverChangeFilter = EnvironmentManager.Instance.RegisterReceiver(
                    OnConfigChanged,
                    new MessageIdAndRelatedKindFilter(
                        MessageId.Server.ConfigurationChangedIndication,
                        KindServer));

                PluginLog.Info("Config change receivers registered for Hardware and Server kinds");
            }
            catch (Exception ex)
            {
                PluginLog.Error($"Failed to register config change receivers: {ex.Message}", ex);
            }
        }

        private void UnregisterConfigChangeReceivers()
        {
            try
            {
                if (_hardwareChangeFilter != null)
                {
                    EnvironmentManager.Instance.UnRegisterReceiver(_hardwareChangeFilter);
                    _hardwareChangeFilter = null;
                }
                if (_serverChangeFilter != null)
                {
                    EnvironmentManager.Instance.UnRegisterReceiver(_serverChangeFilter);
                    _serverChangeFilter = null;
                }
            }
            catch (Exception ex)
            {
                PluginLog.Error($"Failed to unregister config change receivers: {ex.Message}", ex);
            }
        }

        private object OnConfigChanged(Message message, FQID dest, FQID source)
        {
            if (_closing) return null;

            PluginLog.Info("Config change detected, scheduling cert re-check in 20s");

            // Reset debounce timer to 20s from now
            _configChangeDebounce?.Dispose();
            _configChangeDebounce = new Timer(_ =>
            {
                if (!_closing)
                {
                    try
                    {
                        PerformCertCheck();
                    }
                    catch (Exception ex)
                    {
                        PluginLog.Error($"Config-triggered certificate check failed: {ex.Message}", ex);
                    }
                }
            }, null, TimeSpan.FromSeconds(20), Timeout.InfiniteTimeSpan);

            return null;
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
                _recollectFilter = _mc.RegisterCommunicationFilter(
                    OnRecollectRequest,
                    new CommunicationIdFilter(CertMessageIds.CertRecollectRequest));
                _mcRegistered = true;
                PluginLog.Info("MessageCommunication filters registered");
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

            // Check all certificates (parallel)
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

            // Fire events for expiring certs (thresholds tracked in-memory only, reset on restart)
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

            // Determine event type GUID and source FQID based on whether this is a device or server cert
            bool isDeviceCert = cert.SourceItemId != null;
            Guid eventTypeId;

            if (isDeviceCert)
            {
                switch (thresholdDays)
                {
                    case 60:
                        eventTypeId = CertWatchdogDefinition.DeviceEventType60DaysId;
                        break;
                    case 30:
                        eventTypeId = CertWatchdogDefinition.DeviceEventType30DaysId;
                        break;
                    case 15:
                        eventTypeId = CertWatchdogDefinition.DeviceEventType15DaysId;
                        break;
                    default:
                        return;
                }
            }
            else
            {
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
            }

            // Build source FQID: for device certs, point to the hardware item; for server certs, use plugin item
            FQID sourceFqid;
            if (isDeviceCert)
            {
                sourceFqid = new FQID(
                    EnvironmentManager.Instance.MasterSite.ServerId,
                    Guid.Empty,
                    cert.SourceItemId.Value,
                    FolderType.No,
                    Kind.Camera);
            }
            else
            {
                sourceFqid = _pluginItemFqid;
            }

            try
            {
                // Message must match the registered EventType.Message exactly for Rules matching
                var eventTypeMessage = isDeviceCert
                    ? $"Device Cert Expire ({thresholdDays} Days)"
                    : $"Cert Expire ({thresholdDays} Days)";

                var header = new EventHeader
                {
                    ID = Guid.NewGuid(),
                    Class = "Operational",
                    Type = isDeviceCert
                        ? $"DeviceCertExpire{thresholdDays}Days"
                        : $"CertExpire{thresholdDays}Days",
                    Timestamp = DateTime.Now,
                    Name = cert.Endpoint,
                    Message = eventTypeMessage,
                    CustomTag = $"Certificate for '{cert.Endpoint}' expires in {cert.DaysLeft} days (threshold: {thresholdDays}d)",
                    Priority = 3,
                    Source = new EventSource
                    {
                        Name = cert.Endpoint,
                        FQID = sourceFqid
                    }
                };

                var analyticsEvent = new AnalyticsEvent
                {
                    EventHeader = header
                };

                EnvironmentManager.Instance.SendMessage(
                    new Message(MessageId.Server.NewEventCommand)
                    {
                        Data = analyticsEvent,
                        RelatedFQID = sourceFqid
                    });

                SystemLog.CertExpiring(cert.Endpoint, cert.DaysLeft);
                PluginLog.Info($"Fired {thresholdDays}-day {(isDeviceCert ? "device " : "")}event for {cert.Endpoint} ({cert.DaysLeft} days left)");
            }
            catch (Exception ex)
            {
                PluginLog.Error($"Failed to fire event for {cert.Endpoint}: {ex.Message}", ex);
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

        private object OnRecollectRequest(Message message, FQID dest, FQID source)
        {
            if (_closing) return null;

            PluginLog.Info("Recollect requested by client");
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    PerformCertCheck();
                }
                catch (Exception ex)
                {
                    PluginLog.Error($"Recollect cert check failed: {ex.Message}", ex);
                }
            });

            return null;
        }
    }
}
