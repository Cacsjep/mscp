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

        // Live progress for in-flight runs, keyed by RunId, and the grid row currently
        // showing each one (so we can update it in place instead of re-rendering at 4 Hz).
        private readonly Dictionary<Guid, ProgressUpdate> _liveProgress = new Dictionary<Guid, ProgressUpdate>();
        private readonly Dictionary<Guid, DataGridViewRow> _runningRows = new Dictionary<Guid, DataGridViewRow>();

        private bool _hasJobs;
        private bool _runActive;   // a run is in progress; Run Now stays disabled until it finishes

        public ExecutionsUserControl()
        {
            InitializeComponent();
        }

        private static bool IsRunning(ExecutionRecord r)
            => string.Equals(r.Outcome, "Running", StringComparison.OrdinalIgnoreCase);

        public void FillContent(Item _)
        {
            if (!_started)
            {
                if (_cmh.Start())
                {
                    _cmh.Register(OnProgress, new CommunicationIdFilter(AutoExporterMessageIds.Progress));
                    _cmh.Register(OnExecutionStarted, new CommunicationIdFilter(AutoExporterMessageIds.ExecutionStarted));
                    _cmh.Register(OnExecutionAdded, new CommunicationIdFilter(AutoExporterMessageIds.ExecutionAdded));
                    _cmh.Register(OnExecutionsReply, new CommunicationIdFilter(AutoExporterMessageIds.GetExecutionsReply));
                }
                _started = true;
            }

            // Reset the in-progress guard when the view (re)opens, so a stuck state
            // (e.g. a helper that died without a completion record) can't leave Run Now
            // permanently disabled.
            _runActive = false;
            RefreshRunNowMenu();
            RequestExecutions();
        }

        public void ClearContent()
        {
            _records.Clear();
            _liveProgress.Clear();
            _runningRows.Clear();
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
            _runningRows.Clear();
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

            var outcome  = OutcomeOf(r);
            bool running = IsRunning(r);

            var sizeMB   = r.BytesWritten / (1024 * 1024);
            var sizeStr  = running ? "" : (sizeMB > 0 ? $"{sizeMB} MB" : "");
            var duration = running
                ? (r.StartedUtc != DateTime.MinValue ? FormatDuration(DateTime.UtcNow - r.StartedUtc) : "")
                : (r.FinishedUtc > r.StartedUtc ? FormatDuration(r.FinishedUtc - r.StartedUtc) : "");
            var detail   = running ? RunningDetail(r) : DetailOf(r, outcome);

            var idx = _grid.Rows.Add(
                when,
                r.JobName,
                r.Trigger,
                r.Format,
                range,
                r.CameraCount,
                sizeStr,
                duration,
                outcome,
                detail);

            var row = _grid.Rows[idx];
            row.Tag = r.RunId;
            row.DefaultCellStyle.ForeColor = ColorFor(outcome);
            if (running) _runningRows[r.RunId] = row;
        }

        // Live detail for a Running row, from the last progress message. AVI exposes a
        // real per-camera percent (and which camera); XProtect's DBExporter percent is
        // the overall export progress. Until the first progress arrives, "starting...".
        private string RunningDetail(ExecutionRecord r)
        {
            if (!_liveProgress.TryGetValue(r.RunId, out var p) || p == null)
                return "starting...";

            bool isAvi = string.Equals(p.Format ?? r.Format, "AVI", StringComparison.OrdinalIgnoreCase);
            if (isAvi && p.CameraCount > 0)
                return $"camera {p.CameraIndex + 1}/{p.CameraCount} - {p.CameraPercent}%";
            return $"{p.CameraPercent}%";
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
                case "Running": return Color.FromArgb(40, 90, 160);
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
                    _hasJobs = false;
                    UpdateRunNowEnabled();
                    return;
                }

                _hasJobs = true;
                foreach (var job in jobs.OrderBy(j => j.Name))
                {
                    var item = new ToolStripMenuItem(job.Name);
                    var jobId = job.FQID.ObjectId;
                    item.Click += (s, e) => InvokeRunNow(jobId, job.Name);
                    _menuRunNow.Items.Add(item);
                }
                UpdateRunNowEnabled();
            }
            catch (Exception ex)
            {
                _log.Error($"RefreshRunNowMenu failed: {ex.Message}");
            }
        }

        // Run Now is disabled while a run is in progress: the view has a single
        // progress bar, so a user must not start a second run from here on top of one
        // that is already running.
        private void UpdateRunNowEnabled()
        {
            _btnRunNow.Enabled = _hasJobs && !_runActive;
            _btnRunNow.ToolTipText = _runActive
                ? "An export is already running. Wait for it to finish."
                : "";
        }

        private void SetRunActive(bool active)
        {
            _runActive = active;
            UpdateRunNowEnabled();
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

                SetRunActive(true);   // block a second Run Now until this one finishes
                // The Event Server will broadcast ExecutionStarted within a moment,
                // which adds the "Running" row; show a hint until then.
                _lblHint.Text = $"Starting export for '{jobName}'...";
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

        // A run has started: add (or refresh) its "Running" row at the top.
        private object OnExecutionStarted(VideoOS.Platform.Messaging.Message m, FQID dest, FQID sender)
        {
            try
            {
                if (m?.Data is ExecutionRecord r)
                {
                    BeginInvokeSafe(() =>
                    {
                        SetRunActive(true);
                        if (!_records.Any(x => x.RunId == r.RunId))
                            _records.Insert(0, r);
                        RenderGrid();
                    });
                }
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
                        SetRunActive(false);   // run finished; allow Run Now again
                        _liveProgress.Remove(r.RunId);

                        // Replace the in-memory "Running" row for this run with the final
                        // record (or insert it at top if there was no running row).
                        _records.RemoveAll(x => x.RunId == r.RunId);
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
            // A run is active (this one, or one started by a rule) - keep Run Now off.
            SetRunActive(true);
            _liveProgress[p.RunId] = p;

            // If we already have a Running row for this run, update it in place (cheap,
            // no flicker). Otherwise the started message hasn't landed yet (or we opened
            // the view mid-run): synthesize a Running record and render once.
            if (_runningRows.TryGetValue(p.RunId, out var row) && row.DataGridView != null && row.Index >= 0)
            {
                var rec = _records.FirstOrDefault(x => x.RunId == p.RunId);
                if (rec != null)
                {
                    row.Cells[9].Value = RunningDetail(rec);
                    if (rec.StartedUtc != DateTime.MinValue)
                        row.Cells[7].Value = FormatDuration(DateTime.UtcNow - rec.StartedUtc);
                }
                return;
            }

            if (!_records.Any(x => x.RunId == p.RunId))
            {
                _records.Insert(0, new ExecutionRecord
                {
                    RunId       = p.RunId,
                    JobName     = p.JobName,
                    Format      = p.Format,
                    Outcome     = "Running",
                    // Use the real start time carried in the progress message so a
                    // view re-opened mid-run shows the correct elapsed, not 0.
                    StartedUtc  = p.StartedUtc != DateTime.MinValue ? p.StartedUtc : DateTime.UtcNow,
                    FinishedUtc = DateTime.MinValue,
                    CameraCount = p.CameraCount
                });
            }
            RenderGrid();
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
