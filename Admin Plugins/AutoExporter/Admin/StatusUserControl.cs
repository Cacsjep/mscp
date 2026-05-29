using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using AutoExporter.Background;
using AutoExporter.Messaging;
using CommunitySDK;
using VideoOS.Platform;
using VideoOS.Platform.Messaging;

namespace AutoExporter.Admin
{
    public partial class StatusUserControl : UserControl
    {
        private static readonly PluginLog _log = new PluginLog("AutoExporter.StatusUI");
        private readonly CrossMessageHandler _cmh = new CrossMessageHandler(_log);
        private readonly System.Windows.Forms.Timer _autoRefresh = new System.Windows.Forms.Timer { Interval = 30_000 };

        // Tracks the per-job correlationId issued in the current refresh cycle.
        // We swap this dictionary on every refresh; stale replies are dropped.
        private readonly ConcurrentDictionary<Guid, PendingProbe> _pending = new ConcurrentDictionary<Guid, PendingProbe>();
        private Guid _refreshCycleId;

        private bool _started;

        public StatusUserControl()
        {
            InitializeComponent();
            _autoRefresh.Tick += (s, e) => SafeRefresh();
            _btnRefresh.Click += (s, e) => SafeRefresh();
        }

        public void FillContent(Item _)
        {
            if (!_started)
            {
                if (_cmh.Start())
                    _cmh.Register(OnProbeReply, new CommunicationIdFilter(AutoExporterMessageIds.StorageProbeReply));
                _started = true;
            }
            _autoRefresh.Start();
            SafeRefresh();
        }

        public void ClearContent() { _grid.Rows.Clear(); }

        internal void Shutdown()
        {
            _autoRefresh.Stop();
            try { _cmh.Close(); } catch { }
            _started = false;
        }

        private void SafeRefresh()
        {
            try { Refresh(); }
            catch (Exception ex) { _log.Error($"Status refresh failed: {ex.Message}", ex); }
        }

        private new void Refresh()
        {
            _grid.Rows.Clear();
            _pending.Clear();
            _refreshCycleId = Guid.NewGuid();

            List<Item> jobs;
            try
            {
                jobs = Configuration.Instance.GetItemConfigurations(
                    AutoExporterDefinition.PluginId, null, AutoExporterDefinition.JobKindId) ?? new List<Item>();
            }
            catch (Exception ex)
            {
                _lblHint.Text = "Failed to load jobs: " + ex.Message;
                return;
            }

            if (jobs.Count == 0)
            {
                _lblHint.Text = "No jobs configured yet. Create a job under the Jobs folder.";
                _lblLastRefresh.Text = "";
                return;
            }

            if (_cmh.MessageCommunication == null)
            {
                _lblHint.Text = "Cross-environment messaging not available. Is the Event Server running?";
                return;
            }

            foreach (var job in jobs.OrderBy(j => j.Name))
            {
                var path = GetProp(job, "StoragePath", "");
                var maxGB = ParseLong(GetProp(job, "MaxGB", "0"), 0);
                var maxAge = ParseInt(GetProp(job, "MaxAgeDays", "0"), 0);
                var maxBytes = maxGB > 0 ? maxGB * 1024L * 1024L * 1024L : 0;

                var correlationId = Guid.NewGuid();
                var row = AddPendingRow(job.Name, path);
                _pending[correlationId] = new PendingProbe
                {
                    RowIndex      = row,
                    CycleId       = _refreshCycleId,
                    DispatchedUtc = DateTime.UtcNow
                };

                try
                {
                    _cmh.TransmitMessage(new VideoOS.Platform.Messaging.Message(
                        AutoExporterMessageIds.StorageProbeRequest,
                        new StorageProbeRequest
                        {
                            CorrelationId = correlationId,
                            JobObjectId   = job.FQID.ObjectId,
                            JobName       = job.Name,
                            Path          = path,
                            MaxBytes      = maxBytes,
                            MaxAgeDays    = maxAge
                        }));
                }
                catch (Exception ex)
                {
                    _log.Error($"Failed to send probe for '{job.Name}': {ex.Message}", ex);
                    UpdateRowWithError(row, "Probe send failed: " + ex.Message);
                }
            }

            _lblHint.Text = $"{jobs.Count} job(s). Probing Event Server...";
            _lblLastRefresh.Text = $"Refresh requested: {DateTime.Now:HH:mm:ss}";

            // Timeout watchdog: flag any pending row that didn't get a reply in 6s.
            var deadline = DateTime.UtcNow.AddSeconds(6);
            var watchdog = new System.Windows.Forms.Timer { Interval = 6500 };
            var cycle = _refreshCycleId;
            watchdog.Tick += (s, e) =>
            {
                watchdog.Stop();
                watchdog.Dispose();
                if (cycle != _refreshCycleId) return;
                foreach (var kv in _pending.Where(p => p.Value.CycleId == cycle).ToList())
                {
                    UpdateRowWithError(kv.Value.RowIndex, "Event Server did not reply (>6s)");
                    PendingProbe _removed;
                    _pending.TryRemove(kv.Key, out _removed);
                }
            };
            watchdog.Start();
        }

        // ─── Reply handling ─────────────────────────────────

        private object OnProbeReply(VideoOS.Platform.Messaging.Message m, FQID dest, FQID sender)
        {
            try
            {
                if (!(m?.Data is StorageProbeReply reply)) return null;
                if (!_pending.TryRemove(reply.CorrelationId, out var pending)) return null;
                if (pending.CycleId != _refreshCycleId) return null;   // stale

                BeginInvokeSafe(() => ApplyReport(pending.RowIndex, reply.Report));
            }
            catch (Exception ex)
            {
                _log.Error($"OnProbeReply error: {ex.Message}", ex);
            }
            return null;
        }

        private void ApplyReport(int rowIndex, StorageStatusReport r)
        {
            if (r == null || rowIndex < 0 || rowIndex >= _grid.Rows.Count) return;

            var usage    = r.UsageBytes > 0 ? FormatBytes(r.UsageBytes) : "-";
            var free     = r.FreeBytes  >= 0 ? FormatBytes(r.FreeBytes)  : "n/a";
            var total    = r.TotalBytes >= 0 ? FormatBytes(r.TotalBytes) : "";
            var freeCol  = string.IsNullOrEmpty(total) ? free : $"{free} / {total}";
            var quotaPct = r.UsagePercentOfMax.HasValue ? $"{r.UsagePercentOfMax}%" : "-";
            var quotaCap = r.MaxBytes > 0 ? FormatBytes(r.MaxBytes) : "∞";
            var age      = r.MaxAgeDays > 0 ? $"{r.MaxAgeDays}d" : "∞";
            var runs     = r.RunFolderCount.ToString();
            var oldest   = r.OldestRunUtc.HasValue ? r.OldestRunUtc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm") : "-";

            var row = _grid.Rows[rowIndex];
            row.Cells[1].Value = r.StoragePath;
            row.Cells[2].Value = HealthLabel(r.Health);
            row.Cells[3].Value = r.Detail;
            row.Cells[4].Value = usage;
            row.Cells[5].Value = $"{quotaPct} of {quotaCap}";
            row.Cells[6].Value = freeCol;
            row.Cells[7].Value = runs;
            row.Cells[8].Value = age;
            row.Cells[9].Value = oldest;
            row.DefaultCellStyle.ForeColor = ColorFor(r.Health);

            _lblLastRefresh.Text = $"Last reply: {DateTime.Now:HH:mm:ss} (Event Server)";

            int outstanding = _pending.Count(p => p.Value.CycleId == _refreshCycleId);
            _lblHint.Text = outstanding == 0
                ? $"{_grid.Rows.Count} job(s), all reports received"
                : $"{_grid.Rows.Count} job(s), {outstanding} pending reply...";
        }

        private int AddPendingRow(string jobName, string path)
        {
            return _grid.Rows.Add(jobName, path, "PROBING…", "Awaiting Event Server reply", "", "", "", "", "", "");
        }

        private void UpdateRowWithError(int rowIndex, string error)
        {
            if (rowIndex < 0 || rowIndex >= _grid.Rows.Count) return;
            var row = _grid.Rows[rowIndex];
            row.Cells[2].Value = "NO REPLY";
            row.Cells[3].Value = error;
            row.DefaultCellStyle.ForeColor = Color.FromArgb(180, 30, 30);
        }

        // ─── Formatting helpers ─────────────────────────────

        private static string HealthLabel(StorageHealth h)
        {
            switch (h)
            {
                case StorageHealth.Ok:            return "OK";
                case StorageHealth.NotConfigured: return "NOT CONFIGURED";
                case StorageHealth.PathMissing:   return "PATH MISSING";
                case StorageHealth.AccessDenied:  return "ACCESS DENIED";
                case StorageHealth.IOError:       return "IO ERROR";
                case StorageHealth.QuotaWarn:     return "NEAR QUOTA";
                case StorageHealth.QuotaFull:     return "AT QUOTA";
                default:                          return "?";
            }
        }

        private static Color ColorFor(StorageHealth h)
        {
            switch (h)
            {
                case StorageHealth.Ok:            return Color.Black;
                case StorageHealth.NotConfigured: return Color.DimGray;
                case StorageHealth.PathMissing:
                case StorageHealth.AccessDenied:
                case StorageHealth.IOError:
                case StorageHealth.QuotaFull:     return Color.FromArgb(180, 30, 30);
                case StorageHealth.QuotaWarn:     return Color.FromArgb(190, 130, 0);
                default:                          return Color.Black;
            }
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes < 1024)            return $"{bytes} B";
            if (bytes < 1024L * 1024)    return $"{bytes / 1024d:0.0} KB";
            if (bytes < 1024L * 1024 * 1024)   return $"{bytes / (1024d * 1024):0.0} MB";
            if (bytes < 1024L * 1024 * 1024 * 1024) return $"{bytes / (1024d * 1024 * 1024):0.0} GB";
            return $"{bytes / (1024d * 1024 * 1024 * 1024):0.0} TB";
        }

        private static string GetProp(Item item, string key, string def)
            => item.Properties.ContainsKey(key) ? item.Properties[key] : def;

        private static long ParseLong(string s, long def) => long.TryParse(s, out var v) ? v : def;
        private static int  ParseInt(string s, int def)   => int.TryParse(s, out var v)  ? v : def;

        private void BeginInvokeSafe(Action a)
        {
            if (IsDisposed || !IsHandleCreated) return;
            try { BeginInvoke(a); }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
        }

        private class PendingProbe
        {
            public int RowIndex;
            public Guid CycleId;
            public DateTime DispatchedUtc;
        }
    }
}
