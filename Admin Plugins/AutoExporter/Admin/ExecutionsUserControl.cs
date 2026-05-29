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

        // History is owned by the Event Server (the JSONL file lives there). We hold a
        // newest-first in-memory copy fetched over messaging and updated by broadcasts,
        // so the view works from a remote Management Client too.
        private readonly List<ExecutionRecord> _records = new List<ExecutionRecord>();

        public ExecutionsUserControl()
        {
            InitializeComponent();
            _progressBar.Visible = false;
            _progressBarCamera.Visible = false;
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
                    _cmh.Register(OnExecutionsReply, new CommunicationIdFilter(AutoExporterMessageIds.GetExecutionsReply));
                }
                _started = true;
            }

            RefreshRunNowMenu();
            RequestExecutions();
        }

        public void ClearContent()
        {
            _records.Clear();
            _grid.Rows.Clear();
        }

        internal void Shutdown()
        {
            try { _cmh.Close(); } catch { }
            _started = false;
        }

        // Safety net: close the message handler whenever the handle goes away, even if
        // the host never calls ReleaseUserControl/Shutdown. Idempotent.
        protected override void OnHandleDestroyed(EventArgs e)
        {
            Shutdown();
            base.OnHandleDestroyed(e);
        }

        // ─── History (fetched from the Event Server over messaging) ──

        private void RequestExecutions()
        {
            _lblHint.Text = "Loading executions from the Event Server...";
            try
            {
                if (_cmh.MessageCommunication != null)
                    _cmh.TransmitMessage(new VideoOS.Platform.Messaging.Message(
                        AutoExporterMessageIds.GetExecutionsRequest,
                        new GetExecutionsRequest { CorrelationId = Guid.NewGuid() }));
                else
                    _lblHint.Text = "Cross-environment messaging not available. Is the Event Server running?";
            }
            catch (Exception ex)
            {
                _log.Error($"RequestExecutions failed: {ex.Message}");
            }
        }

        private void RenderGrid()
        {
            _grid.Rows.Clear();
            foreach (var r in _records) AddRow(r);   // _records is newest-first
            _lblHint.Text = _records.Count == 0
                ? "No executions yet. Trigger a job via a Rule, or use Run Now above to test."
                : $"{_records.Count} executions";
        }

        private void AddRow(ExecutionRecord r)
        {
            var when     = r.StartedUtc == DateTime.MinValue ? "" : r.StartedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
            var range    = (r.RangeStartUtc == DateTime.MinValue || r.RangeEndUtc == DateTime.MinValue)
                ? ""
                : $"{r.RangeStartUtc.ToLocalTime():MM-dd HH:mm} to {r.RangeEndUtc.ToLocalTime():MM-dd HH:mm}";
            var sizeMB   = r.BytesWritten / (1024 * 1024);
            var duration = r.FinishedUtc > r.StartedUtc
                ? FormatDuration(r.FinishedUtc - r.StartedUtc)
                : "";

            var outcome = OutcomeOf(r);
            var detail  = DetailOf(r, outcome);

            var idx = _grid.Rows.Add(
                when,
                r.JobName,
                r.Trigger,
                r.Format,
                range,
                r.CameraCount,
                sizeMB > 0 ? $"{sizeMB} MB" : "",
                duration,
                outcome,
                detail);

            var row = _grid.Rows[idx];
            row.DefaultCellStyle.ForeColor = ColorFor(outcome);
        }

        // Falls back to the legacy Success bool for records written before Outcome existed.
        private static string OutcomeOf(ExecutionRecord r)
        {
            if (!string.IsNullOrEmpty(r.Outcome)) return r.Outcome;
            return r.Success ? "Success" : "Failed";
        }

        private static string DetailOf(ExecutionRecord r, string outcome)
        {
            var skipped = r.SkippedCameras ?? new List<string>();
            if ((outcome == "Partial" || outcome == "Skipped") && skipped.Count > 0)
            {
                var names = string.Join(", ", skipped);
                return skipped.Count == 1
                    ? $"No recordings in range: {names}"
                    : $"{skipped.Count} cameras had no recordings in range: {names}";
            }
            return string.IsNullOrEmpty(r.Error) ? "" : r.Error;
        }

        private static Color ColorFor(string outcome)
        {
            switch (outcome)
            {
                case "Failed":  return Color.FromArgb(180, 30, 30);
                case "Partial": return Color.FromArgb(180, 110, 0);
                case "Skipped": return Color.FromArgb(120, 120, 120);
                default:        return Color.Black;   // Success
            }
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
            // Dispose the old items (and their click handlers); ToolStrip.Items.Clear()
            // removes but does not dispose them. Snapshot first, clear, then dispose so
            // we don't mutate the collection while enumerating it.
            var oldItems = _menuRunNow.Items.Cast<ToolStripItem>().ToArray();
            _menuRunNow.Items.Clear();
            foreach (var old in oldItems) old.Dispose();
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

                // The Event Server has to spawn the helper, log in, and probe the
                // cameras before the first real percent arrives (several seconds), so
                // show an indeterminate bar right away instead of a dead 0%.
                _lblProgressText.Text = $"Starting export for '{jobName}'...";
                _lblProgressText.Visible = true;
                _progressBar.Style = ProgressBarStyle.Marquee;
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
            RequestExecutions();
        }

        private void OnClearClick(object sender, EventArgs e)
        {
            if (MessageBox.Show(
                    "Clear the entire execution history? This cannot be undone.",
                    "Clear list", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            // The history file lives on the Event Server, so route the clear through it.
            try
            {
                if (_cmh.MessageCommunication != null)
                {
                    _cmh.TransmitMessage(new VideoOS.Platform.Messaging.Message(
                        AutoExporterMessageIds.ClearExecutionsRequest,
                        new ClearExecutionsRequest { CorrelationId = Guid.NewGuid() }));
                }
            }
            catch (Exception ex)
            {
                _log.Error($"Clear request failed: {ex.Message}");
            }

            _records.Clear();
            _grid.Rows.Clear();
            _lblHint.Text = "No executions yet. Trigger a job via a Rule, or use Run Now to test.";
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
                        _progressBarCamera.Visible = false;
                        _lblProgressText.Visible = false;

                        // Insert the just-finished run at the top in memory (no disk read).
                        _records.Insert(0, r);
                        RenderGrid();
                    });
                }
            }
            catch { }
            return null;
        }

        private object OnExecutionsReply(VideoOS.Platform.Messaging.Message m, FQID dest, FQID sender)
        {
            try
            {
                if (m?.Data is GetExecutionsReply reply)
                {
                    BeginInvokeSafe(() =>
                    {
                        _records.Clear();
                        if (reply.Records != null)
                        {
                            // Server sends file order (oldest first); show newest first.
                            for (int i = reply.Records.Count - 1; i >= 0; i--)
                                _records.Add(reply.Records[i]);
                        }
                        RenderGrid();
                    });
                }
            }
            catch { }
            return null;
        }

        private void ApplyProgress(ProgressUpdate p)
        {
            _progressBar.Visible = true;
            _progressBarCamera.Visible = true;
            _lblProgressText.Visible = true;
            // Real percent has arrived: switch from the indeterminate marquee to
            // normal filling bars.
            if (_progressBar.Style != ProgressBarStyle.Blocks) _progressBar.Style = ProgressBarStyle.Blocks;
            if (_progressBarCamera.Style != ProgressBarStyle.Blocks) _progressBarCamera.Style = ProgressBarStyle.Blocks;

            _progressBar.Value = Math.Max(0, Math.Min(100, p.Percent));
            _progressBarCamera.Value = Math.Max(0, Math.Min(100, p.CameraPercent));

            var camPart = p.CameraCount > 0 ? $" - camera {p.CameraIndex + 1}/{p.CameraCount} at {p.CameraPercent}%" : "";
            var namePart = string.IsNullOrEmpty(p.CurrentCameraName) ? "" : $" '{p.CurrentCameraName}'";
            _lblProgressText.Text = $"{p.JobName}: {p.Percent}% overall{camPart}{namePart}";
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
