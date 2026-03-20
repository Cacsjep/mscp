using System;
using System.Collections.Generic;
using System.Threading;
using Auditor.Messaging;
using CommunitySDK;
using VideoOS.Platform;
using VideoOS.Platform.Background;
using VideoOS.Platform.Data;
using VideoOS.Platform.Messaging;

namespace Auditor.Background
{
    public class AuditEventServerPlugin : BackgroundPlugin
    {
        private static readonly PluginLog _log = new PluginLog("ES Auditor");
        private readonly CrossMessageHandler _cmh = new CrossMessageHandler(_log);
        private readonly AuditLog _auditLog = new AuditLog(_log);
        private object _configChangedReceiver;
        private volatile bool _mcRegistered;
        private volatile bool _closing;
        private Timer _initTimer;
        private FQID _pluginItemFqid;

        public override Guid Id => AuditorDefinition.EventServerBgPluginId;
        public override string Name => "Auditor Event Server";

        public override List<EnvironmentType> TargetEnvironments =>
            new List<EnvironmentType> { EnvironmentType.Service };

        public override void Init()
        {
            _log.Info("Audit Event Server plugin initializing");

            _auditLog.Register();
            RefreshPluginItemFqid();

            // Listen for config changes to pick up new audit rules
            _configChangedReceiver = EnvironmentManager.Instance.RegisterReceiver(
                OnConfigChanged,
                new MessageIdFilter(MessageId.Server.ConfigurationChangedIndication));
            _log.Info("Registered ConfigurationChangedIndication receiver");

            _cmh.Start();

            // Defer filter registration - MC not ready during Init
            _initTimer = new Timer(OnInitTimer, null, TimeSpan.FromSeconds(15), Timeout.InfiniteTimeSpan);

            _log.Info("Audit Event Server plugin initialized");
        }

        public override void Close()
        {
            _log.Info("Audit Event Server plugin closing");
            _closing = true;

            _initTimer?.Dispose();
            _initTimer = null;

            if (_configChangedReceiver != null)
            {
                EnvironmentManager.Instance.UnRegisterReceiver(_configChangedReceiver);
                _configChangedReceiver = null;
            }

            _cmh.Close();
            _log.Info("Audit Event Server plugin closed");
        }

        private object OnConfigChanged(Message message, FQID dest, FQID source)
        {
            // Only react to config changes for our own plugin's audit rules
            var fqid = message.RelatedFQID;
            if (fqid != null && fqid.Kind != AuditorDefinition.AuditRuleKindId)
                return null;

            _log.Info("ConfigurationChangedIndication received for audit rules, refreshing plugin item FQID");
            RefreshPluginItemFqid();
            return null;
        }

        private void RefreshPluginItemFqid()
        {
            try
            {
                var items = Configuration.Instance.GetItemConfigurations(
                    AuditorDefinition.PluginId, null, AuditorDefinition.AuditRuleKindId);
                if (items != null && items.Count > 0)
                {
                    _pluginItemFqid = items[0].FQID;
                    _log.Info($"Plugin item FQID set from audit rule '{items[0].Name}' (count={items.Count})");
                }
                else
                {
                    _log.Info("No audit rules found - events will not have a source FQID until rules are created");
                    _pluginItemFqid = null;
                }
            }
            catch (Exception ex)
            {
                _log.Error($"Failed to refresh plugin item FQID: {ex.Message}");
            }
        }

        private void OnInitTimer(object state)
        {
            if (_closing) return;
            _log.Info("Init timer fired, registering MC filter");
            EnsureMessageCommunicationFilter();
        }

        private void EnsureMessageCommunicationFilter()
        {
            if (_mcRegistered || _cmh.MessageCommunication == null) return;

            _cmh.Register(OnAuditEventReceived, new CommunicationIdFilter(AuditMessageIds.AuditEventReport));
            _mcRegistered = true;
        }

        private object OnAuditEventReceived(Message message, FQID dest, FQID source)
        {
            if (_closing) return null;

            var report = message.Data as AuditEventReport;
            if (report == null)
            {
                _log.Error("Received MC message but data is not AuditEventReport");
                return null;
            }

            _log.Info($"Received audit event from Smart Client: type={report.EventType} user={report.UserName} camera={report.CameraName ?? "(none)"} reason={report.Reason ?? "(none)"}");

            switch (report.EventType)
            {
                case "GeneralPlayback":
                    if (!string.IsNullOrEmpty(report.Reason))
                        _auditLog.PlaybackMessage(report.UserName, report.Reason);
                    break;
                case "ExportStarted":
                case "ExportWorkspaceEntered":
                    if (!string.IsNullOrEmpty(report.Reason))
                        _auditLog.ExportMessage(report.UserName, report.Reason);
                    break;
                case "IndependentPlaybackEnabled":
                    if (!string.IsNullOrEmpty(report.Reason))
                        _auditLog.IndependentPlaybackMessage(report.UserName, report.Reason);
                    break;
                case "PlaybackAction":
                    var action = report.Details ?? "Unknown";
                    var recordingTime = report.PlaybackDate?.ToString("yyyy-MM-dd HH:mm:ss") ?? "(unknown)";
                    var cameras = report.CamerasInView != null && report.CamerasInView.Length > 0
                        ? "Cameras: " + string.Join(", ", report.CamerasInView)
                        : report.CameraName != null ? "Camera: " + report.CameraName : "Cameras: (none)";
                    _auditLog.PlaybackActionMessage(report.UserName, action, recordingTime, cameras);
                    break;
            }

            if (report.FireEvent)
            {
                if (_pluginItemFqid == null)
                {
                    _log.Info("No plugin item FQID available - refreshing");
                    RefreshPluginItemFqid();
                    if (_pluginItemFqid == null)
                    {
                        _log.Error("Still no plugin item FQID - cannot fire analytics event (no audit rules configured?)");
                        return null;
                    }
                }

                try
                {
                    var eventTypeId = MapEventType(report.EventType);
                    if (eventTypeId == Guid.Empty)
                    {
                        _log.Info($"Unknown event type '{report.EventType}' - skipping event trigger");
                    }
                    else
                    {
                        var eventMessage = MapEventMessage(report.EventType);
                        var customTag = BuildCustomTag(report);

                        _log.Info($"Building AnalyticsEvent: message='{eventMessage}' customTag='{customTag}'");

                        var header = new EventHeader
                        {
                            ID = Guid.NewGuid(),
                            Class = "Operational",
                            Type = $"Audit_{report.EventType}",
                            Timestamp = report.Timestamp,
                            Name = report.UserName ?? "(unknown)",
                            Message = eventMessage,
                            CustomTag = customTag,
                            Priority = 3,
                            Source = new EventSource
                            {
                                Name = report.UserName ?? "Auditor",
                                FQID = _pluginItemFqid
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
                                RelatedFQID = _pluginItemFqid
                            });

                        _log.Info($"Fired AnalyticsEvent '{eventMessage}' for user '{report.UserName}' (eventId={header.ID})");
                    }
                }
                catch (Exception ex)
                {
                    _log.Error($"Failed to fire analytics event: {ex.Message}");
                }
            }
            else
            {
                _log.Info($"Event trigger disabled for '{report.EventType}' - skipping analytics event");
            }

            return null;
        }

        private static Guid MapEventType(string eventType)
        {
            switch (eventType)
            {
                case "GeneralPlayback": return AuditorDefinition.EvtPlaybackId;
                case "ExportStarted":
                case "ExportWorkspaceEntered": return AuditorDefinition.EvtExportId;
                case "IndependentPlaybackEnabled": return AuditorDefinition.EvtIndepPlaybackId;
                case "PlaybackAction": return AuditorDefinition.EvtPlaybackActionId;
                default: return Guid.Empty;
            }
        }

        private static string MapEventMessage(string eventType)
        {
            switch (eventType)
            {
                case "GeneralPlayback": return "Audit: Playback Entry";
                case "ExportStarted":
                case "ExportWorkspaceEntered": return "Audit: Export Entry";
                case "IndependentPlaybackEnabled": return "Audit: Independent Playback";
                case "PlaybackAction": return "Audit: Playback Action";
                default: return "Audit: Unknown";
            }
        }

        private static string BuildCustomTag(AuditEventReport report)
        {
            var parts = new List<string>();
            parts.Add($"User: {report.UserName}");

            if (!string.IsNullOrEmpty(report.CameraName))
                parts.Add($"Camera: {report.CameraName}");

            if (report.PlaybackDate.HasValue)
                parts.Add($"PlaybackDate: {report.PlaybackDate.Value:yyyy-MM-dd HH:mm:ss}");

            if (report.CamerasInView != null && report.CamerasInView.Length > 0)
                parts.Add($"CamerasInView: {string.Join(", ", report.CamerasInView)}");

            if (!string.IsNullOrEmpty(report.Reason))
                parts.Add($"Reason: {report.Reason}");

            if (!string.IsNullOrEmpty(report.Details))
                parts.Add($"Details: {report.Details}");

            return string.Join(" | ", parts);
        }
    }
}
