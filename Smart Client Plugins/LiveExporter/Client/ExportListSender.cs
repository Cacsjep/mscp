using System;
using VideoOS.Platform;
using VideoOS.Platform.Messaging;
using VideoOS.Platform.Util;

namespace LiveExporter.Client
{
    /// <summary>
    /// Posts a (camera, [start..end]) pair to Smart Client's built-in export list via
    /// MessageId.SmartClient.AddToExportCommand. Times are passed as UTC.
    /// </summary>
    internal static class ExportListSender
    {
        public static bool Send(FQID cameraFqid, DateTime startUtc, DateTime endUtc, out string error)
        {
            error = null;
            if (cameraFqid == null) { error = "No camera selected"; return false; }
            if (endUtc <= startUtc) { error = "End must be after start"; return false; }

            try
            {
                var data = new AddToExportCommandData
                {
                    ItemsToExport = new[] { (cameraFqid, new TimeInterval(startUtc, endUtc)) },
                    ShowConfirmationToasts = true,
                };
                EnvironmentManager.Instance.SendMessage(
                    new Message(MessageId.SmartClient.AddToExportCommand, data));
                return true;
            }
            catch (Exception ex)
            {
                LiveExporterDefinition.Log.Error("AddToExport failed", ex);
                error = ex.Message;
                return false;
            }
        }
    }
}
