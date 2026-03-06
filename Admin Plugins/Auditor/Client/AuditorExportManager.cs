using CommunitySDK;
using System;
using System.Collections.Generic;
using System.Linq;
using VideoOS.Platform;
using VideoOS.Platform.Client;
using VideoOS.Platform.Login;
using VideoOS.Platform.Messaging;

namespace Auditor.Client
{
    public class AuditorExportManager : ExportManager
    {
        private readonly ExportParameters _exportParameters;
        private readonly PluginLog _log = new PluginLog("SC Auditor - ExportManager");

        public AuditorExportManager(ExportParameters exportParameters)
            : base(exportParameters)
        {
            _exportParameters = exportParameters;
        }

        public override int Progress => 100;
        public override string LastErrorMessage => null;
        public override bool IncludePluginFilesInExport => false;

        public override void ExportStarting()
        {
            var parts = new List<string>();

            parts.Add($"Period: {_exportParameters.BeginTime.ToLocalTime():yyyy-MM-dd HH:mm:ss} - {_exportParameters.EndTime.ToLocalTime():yyyy-MM-dd HH:mm:ss}");

            if (!string.IsNullOrEmpty(_exportParameters.DestinationDirectory))
                parts.Add($"Destination: {_exportParameters.DestinationDirectory}");

            var exportTypes = new List<string>();
            if (_exportParameters.ExportingDatabase) exportTypes.Add("Database");
            if (_exportParameters.ExportingVideoClip) exportTypes.Add("Video Clip");
            if (_exportParameters.ExportingStillImages) exportTypes.Add("Still Images");
            if (exportTypes.Count > 0)
                parts.Add($"Type: {string.Join(", ", exportTypes)}");

            if (_exportParameters.CameraItems != null && _exportParameters.CameraItems.Any())
                parts.Add($"Cameras: {string.Join(", ", _exportParameters.CameraItems.Select(c => c.Name))}");

            var details = string.Join(" | ", parts);

            string userName = null;
            try
            {
                var ls = LoginSettingsCache.GetLoginSettings(EnvironmentManager.Instance.MasterSite);
                userName = ls.UserName;
            }
            catch { }

            var auditData = new AuditEventData
            {
                EventType = AuditEventType.ExportStarted,
                Timestamp = DateTime.Now,
                UserName = userName ?? "(unknown)",
                Details = details,
            };

            _log.Info($"ExportStarting - {details}");
            EnvironmentManager.Instance.SendMessage(
                new Message(AuditorBackgroundPlugin.AuditMessageId) { Data = auditData });
        }

        public override void ExportCancelled()
        {
            SendExportAudit(AuditEventType.ExportCancelled);
        }

        public override void ExportFailed()
        {
            SendExportAudit(AuditEventType.ExportFailed);
        }

        public override void ExportComplete()
        {
            SendExportAudit(AuditEventType.ExportCompleted);
        }

        private void SendExportAudit(AuditEventType eventType)
        {
            string userName = null;
            try
            {
                var ls = LoginSettingsCache.GetLoginSettings(EnvironmentManager.Instance.MasterSite);
                userName = ls.UserName;
            }
            catch { }

            var auditData = new AuditEventData
            {
                EventType = eventType,
                Timestamp = DateTime.Now,
                UserName = userName ?? "(unknown)",
            };

            _log.Info($"{eventType}");
            EnvironmentManager.Instance.SendMessage(
                new Message(AuditorBackgroundPlugin.AuditMessageId) { Data = auditData });
        }

        public override ulong? EstimateSizeOfExport()
        {
            return null;
        }
    }
}
