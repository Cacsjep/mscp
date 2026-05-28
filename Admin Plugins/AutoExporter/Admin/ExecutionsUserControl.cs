using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using AutoExporter.Background;
using AutoExporter.Messaging;
using CommunitySDK;
using VideoOS.Platform;
using VideoOS.Platform.Messaging;

namespace AutoExporter.Admin
{
    public partial class ExecutionsUserControl : UserControl
    {
        private static readonly PluginLog _log = new PluginLog("AutoExporter.ExecutionsUI");

        private readonly CrossMessageHandler _cmh = new CrossMessageHandler(_log);
        private bool _started;

        public ExecutionsUserControl()
        {
            InitializeComponent();
            _progressBar.Visible = false;
            _lblProgressText.Visible = false;
        }

        public void FillContent(Item _)
        {
            if (!_started)
            {
                if (_cmh.Start())
                {
                    _cmh.Register(OnProgress, new CommunicationIdFilter(AutoExporterMessageIds.Progress));
                    _cmh.Register(OnExecutionAdded, new CommunicationIdFilter(AutoExporterMessageIds.ExecutionAdded));
                }
                _started = true;
            }

            RefreshRunNowMenu();
            RefreshGrid();
        }

        public void ClearContent()
        {
            _grid.Rows.Clear();
        }

        internal void Shutdown()
        {
            try { _cmh.Close(); } catch { }
            _started = false;
        }

        // ─── History ────────────────────────────────────────

        private void RefreshGrid()
        {
            _grid.Rows.Clear();

            var log = new ExecutionLog();
            var records = log.LoadRecent();
            records.Reverse();   // newest first

            foreach (var r in records) AddRow(r);

            _lblHint.Text = records.Count == 0
                ? "No executions yet. Trigger a job via a Rule, or use Run Now ▼ above to test."
                : $"{records.Count} executions";
        }

        private void AddRow(ExecutionRecord r)
        {
            var when     = r.StartedUtc == DateTime.MinValue ? "" : r.StartedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
            var range    = (r.RangeStartUtc == DateTime.MinValue || r.RangeEndUtc == DateTime.MinValue)
                ? ""
                : $"{r.RangeStartUtc.ToLocalTime():MM-dd HH:mm} → {r.RangeEndUtc.ToLocalTime():MM-dd HH:mm}";
            var sizeMB   = r.BytesWritten / (1024 * 1024);
            var duration = r.FinishedUtc > r.StartedUtc
                ? FormatDuration(r.FinishedUtc - r.StartedUtc)
                : "";

            var idx = _grid.Rows.Add(
                when,
                r.JobName,
                r.Trigger,
                r.Format,
                range,
                r.CameraCount,
                sizeMB > 0 ? $"{sizeMB} MB" : "",
                duration,
                r.Success ? "OK" : "FAIL",
                r.Success ? "" : r.Error);

            var row = _grid.Rows[idx];
            row.DefaultCellStyle.ForeColor = r.Success ? Color.Black : Color.FromArgb(180, 30, 30);
        }

        private static string FormatDuration(TimeSpan ts)
        {
            if (ts.TotalSeconds < 60) return $"{ts.TotalSeconds:0.0}s";
            if (ts.TotalMinutes < 60) return $"{(int)ts.TotalMinutes}m {ts.Seconds}s";
            return $"{(int)ts.TotalHours}h {ts.Minutes}m";
        }

        // ─── Run Now ────────────────────────────────────────

        private void RefreshRunNowMenu()
        {
            _menuRunNow.Items.Clear();
            try
            {
                var jobs = Configuration.Instance.GetItemConfigurations(
                    AutoExporterDefinition.PluginId, null, AutoExporterDefinition.JobKindId);

                if (jobs == null || jobs.Count == 0)
                {
                    var none = new ToolStripMenuItem("(no jobs configured)") { Enabled = false };
                    _menuRunNow.Items.Add(none);
                    _btnRunNow.Enabled = false;
                    return;
                }

                _btnRunNow.Enabled = true;
                foreach (var job in jobs.OrderBy(j => j.Name))
                {
                    var item = new ToolStripMenuItem(job.Name);
                    var jobId = job.FQID.ObjectId;
                    item.Click += (s, e) => InvokeRunNow(jobId, job.Name);
                    _menuRunNow.Items.Add(item);
                }
            }
            catch (Exception ex)
            {
                _log.Error($"RefreshRunNowMenu failed: {ex.Message}");
            }
        }

        private void InvokeRunNow(Guid jobObjectId, string jobName)
        {
            if (_cmh.MessageCommunication == null)
            {
                MessageBox.Show("Cross-environment messaging is not available. Make sure the Event Server is running.",
                    "Run Now", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                _cmh.TransmitMessage(new VideoOS.Platform.Messaging.Message(AutoExporterMessageIds.RunNowRequest,
                    new RunNowRequest { JobObjectId = jobObjectId }));

                _lblProgressText.Text = $"Queued '{jobName}' — waiting for Event Server…";
                _lblProgressText.Visible = true;
                _progressBar.Value = 0;
                _progressBar.Visible = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to send Run Now request: " + ex.Message,
                    "Run Now", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OnRefreshClick(object sender, EventArgs e)
        {
            RefreshRunNowMenu();
            RefreshGrid();
        }

        // ─── Background messages (marshalled to UI thread) ─

        private object OnProgress(VideoOS.Platform.Messaging.Message m, FQID dest, FQID sender)
        {
            try
            {
                if (m?.Data is ProgressUpdate p)
                    BeginInvokeSafe(() => ApplyProgress(p));
            }
            catch { }
            return null;
        }

        private object OnExecutionAdded(VideoOS.Platform.Messaging.Message m, FQID dest, FQID sender)
        {
            try
            {
                if (m?.Data is ExecutionRecord r)
                {
                    BeginInvokeSafe(() =>
                    {
                        _progressBar.Visible = false;
                        _lblProgressText.Visible = false;

                        // Insert at top
                        _grid.Rows.Clear();
                        RefreshGrid();
                    });
                }
            }
            catch { }
            return null;
        }

        private void ApplyProgress(ProgressUpdate p)
        {
            _progressBar.Visible = true;
            _lblProgressText.Visible = true;
            _progressBar.Value = Math.Max(0, Math.Min(100, p.Percent));
            var camPart = p.CameraCount > 0 ? $" — camera {p.CameraIndex + 1}/{p.CameraCount}" : "";
            var namePart = string.IsNullOrEmpty(p.CurrentCameraName) ? "" : $" '{p.CurrentCameraName}'";
            _lblProgressText.Text = $"{p.JobName}: {p.Percent}%{camPart}{namePart}";
        }

        private void BeginInvokeSafe(Action a)
        {
            if (IsDisposed || !IsHandleCreated) return;
            try { BeginInvoke(a); }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
        }
    }
}
