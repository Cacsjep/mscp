using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using AutoExporter.Admin;
using AutoExporter.Messaging;
using CommunitySDK;
using VideoOS.Platform;
using VideoOS.Platform.Background;
using VideoOS.Platform.Data;
using VideoOS.Platform.Messaging;

namespace AutoExporter.Background
{
    public class AutoExporterBackgroundPlugin : BackgroundPlugin
    {
        private static readonly PluginLog _log = new PluginLog("AutoExporter");
        private readonly SystemLog _sysLog = new SystemLog(_log);
        private readonly CrossMessageHandler _cmh = new CrossMessageHandler(_log);
        private readonly ExecutionLog _executionLog = new ExecutionLog();
        private readonly Exporter _exporter = new Exporter();

        private readonly object _configLock = new object();
        private List<Item> _jobs = new List<Item>();

        private object _msgJobsObj;

        private System.Threading.Timer _ringTimer;

        private readonly ConcurrentDictionary<Guid, RunHandle> _running = new ConcurrentDictionary<Guid, RunHandle>();
        private volatile bool _closing;

        internal static AutoExporterBackgroundPlugin Instance { get; private set; }

        public override Guid Id => AutoExporterDefinition.BackgroundPluginId;
        public override string Name => "Auto Exporter Background";

        public override List<EnvironmentType> TargetEnvironments =>
            new List<EnvironmentType> { EnvironmentType.Service };

        public override void Init()
        {
            Instance = this;
            _log.Info("Auto Exporter background plugin initializing");

            _sysLog.Register();
            LoadConfig();

            _msgJobsObj = EnvironmentManager.Instance.RegisterReceiver(
                OnConfigurationChanged,
                new MessageIdAndRelatedKindFilter(
                    MessageId.Server.ConfigurationChangedIndication,
                    AutoExporterDefinition.JobKindId));

            if (_cmh.Start())
            {
                _cmh.Register(OnRunNowMessage,        new CommunicationIdFilter(AutoExporterMessageIds.RunNowRequest));
                _cmh.Register(OnStorageProbeRequest,  new CommunicationIdFilter(AutoExporterMessageIds.StorageProbeRequest));
                _cmh.Register(OnClearExecutions,      new CommunicationIdFilter(AutoExporterMessageIds.ClearExecutionsRequest));
                _cmh.Register(OnGetExecutions,        new CommunicationIdFilter(AutoExporterMessageIds.GetExecutionsRequest));
            }

            // Hourly safety pass on the ring buffer.
            _ringTimer = new System.Threading.Timer(_ => SafeRingCleanup(), null,
                TimeSpan.FromMinutes(5), TimeSpan.FromHours(1));

            _log.Info("Auto Exporter background plugin initialized");
        }

        public override void Close()
        {
            _closing = true;
            _log.Info("Auto Exporter background plugin closing");

            _ringTimer?.Dispose();

            if (_msgJobsObj != null) { EnvironmentManager.Instance.UnRegisterReceiver(_msgJobsObj); _msgJobsObj = null; }

            // Cancel any in-flight runs.
            foreach (var rh in _running.Values)
            {
                try { rh.Cts.Cancel(); } catch { }
            }

            _cmh.Close();
            Instance = null;
        }

        // ─── Config ─────────────────────────────────────────

        private void LoadConfig()
        {
            try
            {
                var jobs = Configuration.Instance.GetItemConfigurations(
                    AutoExporterDefinition.PluginId,
                    null,
                    AutoExporterDefinition.JobKindId) ?? new List<Item>();

                lock (_configLock) { _jobs = jobs; }
                _log.Info($"Config loaded: jobs={jobs.Count}");
            }
            catch (Exception ex)
            {
                _log.Error($"LoadConfig failed: {ex.Message}", ex);
            }
        }

        private object OnConfigurationChanged(Message msg, FQID dest, FQID sender)
        {
            if (_closing) return null;
            _log.Info("Configuration changed, reloading");
            LoadConfig();
            return null;
        }

        // ─── Triggers ───────────────────────────────────────

        public void TriggerJob(Guid jobObjectId, BaseEvent triggeringEvent, string triggerSource)
        {
            if (_closing) return;

            Item job;
            lock (_configLock)
            {
                job = _jobs.FirstOrDefault(j => j.FQID.ObjectId == jobObjectId);
            }
            if (job == null)
            {
                _log.Error($"Job {jobObjectId} not found in config");
                return;
            }

            if (!IsJobEnabled(job))
            {
                _log.Info($"Job '{job.Name}' is disabled, skipping");
                return;
            }

            var newRun = new RunHandle
            {
                RunId = Guid.NewGuid(),
                JobObjectId = jobObjectId,
                Cts = new CancellationTokenSource()
            };

            if (!_running.TryAdd(jobObjectId, newRun))
            {
                // A trigger fired while the job's previous run is still going (e.g. the
                // trigger interval is shorter than the export takes). This is benign,
                // not a failure: record it as Skipped so it shows in the Executions
                // view, but do NOT fire the Job Failed event for it.
                _log.Info($"Job '{job.Name}' skipped: previous run still in progress");
                _sysLog.JobSkippedBusy(job.Name);
                AppendExecutionAndBroadcast(new ExecutionRecord
                {
                    RunId = newRun.RunId,
                    JobObjectId = jobObjectId,
                    JobName = job.Name,
                    StartedUtc = DateTime.UtcNow,
                    FinishedUtc = DateTime.UtcNow,
                    Format = GetProp(job, "Format", "XProtect"),
                    Trigger = triggerSource,
                    Success = false,
                    Outcome = "Skipped",
                    Error = "Previous run still in progress (trigger faster than export)"
                });
                return;
            }

            ThreadPool.QueueUserWorkItem(_ =>
            {
                try { RunJob(job, triggeringEvent, triggerSource, newRun); }
                catch (Exception ex) { _log.Error($"RunJob threw: {ex.Message}", ex); }
                finally
                {
                    RunHandle removed;
                    _running.TryRemove(jobObjectId, out removed);
                    newRun.Cts.Dispose();
                }
            });
        }

        private void RunJob(Item job, BaseEvent triggeringEvent, string triggerSource, RunHandle run)
        {
            var startedUtc = DateTime.UtcNow;
            var rangeValue = ParseInt(GetProp(job, "RangeValue", "1"), 1);
            var rangeUnit  = GetProp(job, "RangeUnit", "Days");
            var format     = GetProp(job, "Format", "XProtect") == "AVI" ? ExportFormat.Avi : ExportFormat.XProtect;

            var rangeEndUtc   = startedUtc;
            var rangeStartUtc = TimeRange.Subtract(rangeEndUtc, rangeValue, rangeUnit);

            string storage = GetProp(job, "StoragePath", "");
            if (string.IsNullOrWhiteSpace(storage))
            {
                FinishFailure(job, triggeringEvent, triggerSource, run, startedUtc, rangeStartUtc, rangeEndUtc, format,
                    "Storage path not configured on this job");
                return;
            }

            var ring = BuildRingStorage(job);
            try
            {
                var cr = ring?.Prune();
                if (cr != null && cr.PrunedFolders > 0)
                    _log.Info($"Pre-run cleanup for '{job.Name}': pruned {cr.PrunedFolders} folder(s), reclaimed {cr.BytesReclaimed / (1024 * 1024)} MB");
            }
            catch (Exception ex) { _log.Error($"Pre-run cleanup failed for '{job.Name}': {ex.Message}", ex); }

            var runFolder = Path.Combine(storage, rangeEndUtc.ToLocalTime().ToString("dd.MM.yyyy_HHmm"));

            var targets = JobUserControl.ReadTargets(job);
            if (targets.Count == 0)
            {
                FinishFailure(job, triggeringEvent, triggerSource, run, startedUtc, rangeStartUtc, rangeEndUtc, format,
                    "No cameras / groups configured on this job");
                return;
            }

            var cfg = new ExportJobConfig
            {
                JobObjectId   = job.FQID.ObjectId,
                JobName       = job.Name,
                Format        = format,
                Encrypt       = GetProp(job, "Encrypt", "No") == "Yes",
                Password      = GetProp(job, "Password", ""),
                IncludePlayer = GetProp(job, "IncludePlayer", "Yes") != "No",
                IncludeAudio  = GetProp(job, "IncludeAudio", "Yes") != "No",
                Targets       = targets.Select(t => new JobTargetSpec
                {
                    Kind     = t.Kind == JobTargetKind.Group ? "Group" : "Camera",
                    ObjectId = t.ObjectId,
                    Name     = t.Name
                }).ToList(),
                RangeStartUtc = rangeStartUtc,
                RangeEndUtc   = rangeEndUtc,
                OutputFolder  = runFolder
            };

            string serverUri = GetServerUri();
            _log.Info(
                $"Job started: name='{job.Name}' runId={run.RunId} trigger={triggerSource} " +
                $"format={cfg.Format} encrypt={cfg.Encrypt} targets={cfg.Targets.Count} " +
                $"range={rangeStartUtc:O}->{rangeEndUtc:O} outputFolder='{runFolder}' serverUri='{serverUri}'");

            FireStartedEvent(job, triggerSource, rangeStartUtc, rangeEndUtc);

            // Tell the admin view a run has started so it shows a "Running" row
            // immediately (it flips to the final outcome when the run completes).
            BroadcastExecutionStarted(run, cfg, triggerSource, startedUtc, rangeStartUtc, rangeEndUtc, format);
            BroadcastProgress(run.RunId, cfg, 0, 0, "", 0, startedUtc);

            int targetCount = targets.Count;
            Action<int, int, string> onProgress = (camIdx, pct, camName) =>
            {
                int denom = Math.Max(targetCount, camIdx + 1);
                int overall = (int)(((double)camIdx + (pct / 100.0)) / denom * 100);
                if (overall < 0) overall = 0;
                if (overall > 100) overall = 100;
                BroadcastProgress(run.RunId, cfg, overall, camIdx, camName, pct, startedUtc);
            };

            ExportRunResult result;
            try
            {
                result = _exporter.Run(cfg, serverUri, onProgress, run.Cts.Token);
            }
            catch (OperationCanceledException)
            {
                FinishFailure(job, triggeringEvent, triggerSource, run, startedUtc, rangeStartUtc, rangeEndUtc, format,
                    "Cancelled");
                return;
            }

            var finishedUtc = DateTime.UtcNow;
            var skippedCams = result.SkippedCameras ?? new List<string>();

            // Distinct outcome so the Executions view can show more than pass/fail.
            string outcome = ExecutionOutcome.Classify(result.Success, result.CameraCount, skippedCams.Count);

            var record = new ExecutionRecord
            {
                RunId          = run.RunId,
                JobObjectId    = job.FQID.ObjectId,
                JobName        = job.Name,
                StartedUtc     = startedUtc,
                FinishedUtc    = finishedUtc,
                RangeStartUtc  = rangeStartUtc,
                RangeEndUtc    = rangeEndUtc,
                Format         = format == ExportFormat.Avi ? "AVI" : "XProtect",
                Trigger        = triggerSource,
                Success        = result.Success,
                Outcome        = outcome,
                Error          = result.Error ?? "",
                CameraCount    = result.CameraCount,
                BytesWritten   = result.BytesWritten,
                OutputFolder   = runFolder,
                CameraNames    = result.CameraNames ?? new List<string>(),
                SkippedCameras = skippedCams
            };

            AppendExecutionAndBroadcast(record);
            BroadcastProgress(run.RunId, cfg, 100, Math.Max(0, record.CameraCount - 1), "", 100, startedUtc);

            if (result.Success)
            {
                var seconds = (finishedUtc - startedUtc).TotalSeconds;
                _sysLog.JobSucceeded(job.Name, result.CameraCount, result.BytesWritten / (1024 * 1024), seconds);
                FireSucceededEvent(job, triggeringEvent, record);
            }
            else
            {
                _sysLog.JobFailed(job.Name, result.Error ?? "Unknown");
                FireFailedEvent(job, triggeringEvent, result.Error ?? "Unknown");
            }
        }

        private void FinishFailure(Item job, BaseEvent triggeringEvent, string triggerSource, RunHandle run,
            DateTime startedUtc, DateTime rangeStartUtc, DateTime rangeEndUtc, ExportFormat format, string error)
        {
            var record = new ExecutionRecord
            {
                RunId = run.RunId,
                JobObjectId = job.FQID.ObjectId,
                JobName = job.Name,
                StartedUtc = startedUtc,
                FinishedUtc = DateTime.UtcNow,
                RangeStartUtc = rangeStartUtc,
                RangeEndUtc = rangeEndUtc,
                Format = format == ExportFormat.Avi ? "AVI" : "XProtect",
                Trigger = triggerSource,
                Success = false,
                Outcome = "Failed",
                Error = error
            };
            AppendExecutionAndBroadcast(record);
            _sysLog.JobFailed(job.Name, error);
            FireFailedEvent(job, triggeringEvent, error);
        }

        // Camera resolution moved to AutoExporterHelper.exe. The Service env can't
        // call ExportManager APIs, so the helper does both the SDK init AND the
        // camera/group resolution in its own standalone environment.

        private static string GetServerUri()
        {
            try
            {
                var sid = EnvironmentManager.Instance.MasterSite?.ServerId;
                return sid?.Uri?.ToString() ?? "";
            }
            catch { return ""; }
        }

        // ─── Manual trigger via MessageCommunication ──────

        private object OnStorageProbeRequest(Message message, FQID destination, FQID sender)
        {
            if (_closing) return null;
            try
            {
                if (message?.Data is StorageProbeRequest req)
                {
                    _log.Info($"Storage probe request: correlationId={req.CorrelationId} path='{req.Path}'");
                    var report = StorageStatus.Inspect(req.JobObjectId, req.JobName, req.Path, req.MaxBytes, req.MaxAgeDays);

                    if (_cmh.MessageCommunication != null)
                    {
                        _cmh.TransmitMessage(new Message(AutoExporterMessageIds.StorageProbeReply,
                            new StorageProbeReply { CorrelationId = req.CorrelationId, Report = report }));
                    }
                }
            }
            catch (Exception ex)
            {
                _log.Error($"Storage probe handler error: {ex.Message}", ex);
            }
            return null;
        }

        private object OnRunNowMessage(Message message, FQID destination, FQID sender)
        {
            if (_closing) return null;
            try
            {
                if (message?.Data is RunNowRequest req)
                {
                    _log.Info($"Manual Run Now request: job={req.JobObjectId}");
                    TriggerJob(req.JobObjectId, null, "Manual");
                }
            }
            catch (Exception ex)
            {
                _log.Error($"RunNow handler error: {ex.Message}", ex);
            }
            return null;
        }

        private object OnClearExecutions(Message message, FQID destination, FQID sender)
        {
            if (_closing) return null;
            try
            {
                _log.Info("Clear executions request");
                _executionLog.Clear();
            }
            catch (Exception ex)
            {
                _log.Error($"Clear executions handler error: {ex.Message}", ex);
            }
            return null;
        }

        // Serves the execution history to the admin view (the file lives here, on the
        // Event Server, not on the Management Client machine).
        private object OnGetExecutions(Message message, FQID destination, FQID sender)
        {
            if (_closing) return null;
            try
            {
                var req = message?.Data as GetExecutionsRequest;
                var reply = new GetExecutionsReply
                {
                    CorrelationId = req?.CorrelationId ?? Guid.Empty,
                    Records = _executionLog.LoadRecent()
                };
                if (_cmh.MessageCommunication != null)
                    _cmh.TransmitMessage(new Message(AutoExporterMessageIds.GetExecutionsReply, reply));
            }
            catch (Exception ex)
            {
                _log.Error($"Get executions handler error: {ex.Message}", ex);
            }
            return null;
        }

        // ─── Events ─────────────────────────────────────────

        private void FireStartedEvent(Item job, string trigger, DateTime rangeStartUtc, DateTime rangeEndUtc)
        {
            FireRuleEvent(job, AutoExporterDefinition.EvtJobStartedId,
                "AutoExportJobStarted", "Auto Export: Job Started",
                $"Trigger: {trigger} | Range: {rangeStartUtc.ToLocalTime():g} to {rangeEndUtc.ToLocalTime():g}",
                priority: 4);
        }

        private void FireSucceededEvent(Item job, BaseEvent triggeringEvent, ExecutionRecord rec)
        {
            FireRuleEvent(job, AutoExporterDefinition.EvtJobSucceededId,
                "AutoExportJobSucceeded", "Auto Export: Job Succeeded",
                $"Cameras: {rec.CameraCount} | Size: {rec.BytesWritten / (1024 * 1024)} MB | Folder: {rec.OutputFolder}",
                priority: 5);
        }

        private void FireFailedEvent(Item job, BaseEvent triggeringEvent, string error)
        {
            FireRuleEvent(job, AutoExporterDefinition.EvtJobFailedId,
                "AutoExportJobFailed", "Auto Export: Job Failed",
                "Error: " + error,
                priority: 3);
        }

        private void FireRuleEvent(Item job, Guid eventTypeId, string type, string message, string customTag, ushort priority)
        {
            try
            {
                _log.Info($"Firing rule event: type={type} job='{job.Name}' tag='{customTag}'");
                var header = new EventHeader
                {
                    ID = Guid.NewGuid(),
                    Class = "Operational",
                    Type = type,
                    Timestamp = DateTime.Now,
                    Name = job.Name,
                    Message = message,
                    CustomTag = customTag,
                    Priority = priority,
                    Source = new EventSource
                    {
                        Name = job.Name,
                        FQID = job.FQID
                    }
                };

                var ev = new AnalyticsEvent { EventHeader = header };
                EnvironmentManager.Instance.SendMessage(
                    new Message(MessageId.Server.NewEventCommand)
                    {
                        Data = ev,
                        RelatedFQID = job.FQID
                    });
            }
            catch (Exception ex)
            {
                _log.Error($"Failed to fire {type} event for job '{job.Name}': {ex.Message}", ex);
            }
        }

        // ─── Cross-environment broadcasting ───────────────

        private void BroadcastExecutionStarted(RunHandle run, ExportJobConfig cfg, string trigger,
            DateTime startedUtc, DateTime rangeStartUtc, DateTime rangeEndUtc, ExportFormat format)
        {
            if (_cmh.MessageCommunication == null) return;
            try
            {
                _cmh.TransmitMessage(new Message(AutoExporterMessageIds.ExecutionStarted, new ExecutionRecord
                {
                    RunId         = run.RunId,
                    JobObjectId   = cfg.JobObjectId,
                    JobName       = cfg.JobName,
                    StartedUtc    = startedUtc,
                    FinishedUtc   = DateTime.MinValue,
                    RangeStartUtc = rangeStartUtc,
                    RangeEndUtc   = rangeEndUtc,
                    Format        = format == ExportFormat.Avi ? "AVI" : "XProtect",
                    Trigger       = trigger,
                    Outcome       = "Running",
                    CameraCount   = cfg.Targets?.Count ?? 0
                }));
            }
            catch (Exception ex)
            {
                _log.Error($"BroadcastExecutionStarted failed for run {run.RunId}: {ex.Message}", ex);
            }
        }

        private void BroadcastProgress(Guid runId, ExportJobConfig cfg, int percent, int camIdx, string camName, int cameraPercent = 0, DateTime startedUtc = default(DateTime))
        {
            if (_cmh.MessageCommunication == null) return;
            try
            {
                _cmh.TransmitMessage(new Message(AutoExporterMessageIds.Progress, new ProgressUpdate
                {
                    RunId             = runId,
                    JobObjectId       = cfg.JobObjectId,
                    JobName           = cfg.JobName,
                    Percent           = percent,
                    CameraPercent     = cameraPercent,
                    CameraIndex       = camIdx,
                    CameraCount       = cfg.Targets?.Count ?? 0,
                    CurrentCameraName = camName ?? "",
                    Format            = cfg.Format == ExportFormat.Avi ? "AVI" : "XProtect",
                    StartedUtc        = startedUtc
                }));
            }
            catch (Exception ex)
            {
                _log.Error($"BroadcastProgress failed for run {runId}: {ex.Message}", ex);
            }
        }

        private void AppendExecutionAndBroadcast(ExecutionRecord rec)
        {
            try { _executionLog.Append(rec); }
            catch (Exception ex) { _log.Error($"ExecutionLog.Append failed for run {rec.RunId}: {ex.Message}", ex); }

            if (_cmh.MessageCommunication == null) return;
            try { _cmh.TransmitMessage(new Message(AutoExporterMessageIds.ExecutionAdded, rec)); }
            catch (Exception ex) { _log.Error($"Broadcast ExecutionAdded failed for run {rec.RunId}: {ex.Message}", ex); }
        }

        // ─── Ring cleanup ───────────────────────────────────

        private static RingStorage BuildRingStorage(Item job)
        {
            if (job == null) return null;
            var path = GetProp(job, "StoragePath", "");
            var maxGB = ParseLong(GetProp(job, "MaxGB", "0"), 0);
            var maxAge = ParseInt(GetProp(job, "MaxAgeDays", "0"), 0);
            return RingStorage.FromGigabytes(path, maxGB, maxAge);
        }

        private void SafeRingCleanup()
        {
            if (_closing) return;
            try
            {
                List<Item> jobs;
                lock (_configLock) { jobs = _jobs.ToList(); }

                foreach (var job in jobs)
                {
                    var ring = BuildRingStorage(job);
                    if (ring == null || !ring.IsConfigured) continue;
                    var r = ring.Prune();
                    if (r.PrunedFolders > 0)
                        _sysLog.RingCleanup(r.PrunedFolders, r.BytesReclaimed / (1024 * 1024), ring.Root);
                }
            }
            catch (Exception ex)
            {
                _log.Error($"Ring cleanup failed: {ex.Message}", ex);
            }
        }

        // ─── Helpers ────────────────────────────────────────

        private static bool IsJobEnabled(Item job)
            => !job.Properties.ContainsKey("Enabled") || job.Properties["Enabled"] != "No";

        private static string GetProp(Item item, string key, string defaultValue)
            => item.Properties.ContainsKey(key) ? item.Properties[key] : defaultValue;

        private static int ParseInt(string s, int def)
            => int.TryParse(s, out var v) ? v : def;

        private static long ParseLong(string s, long def)
            => long.TryParse(s, out var v) ? v : def;

        // ─── Per-job in-flight handle ────────────────────

        private class RunHandle
        {
            public Guid RunId;
            public Guid JobObjectId;
            public CancellationTokenSource Cts;
        }
    }
}
