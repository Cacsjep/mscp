using Auditor.Messaging;
using CommunitySDK;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using VideoOS.Platform;
using VideoOS.Platform.Background;
using VideoOS.Platform.Client;
using VideoOS.Platform.Login;
using VideoOS.Platform.Messaging;

namespace Auditor.Client
{
    public class AuditorBackgroundPlugin : BackgroundPlugin
    {
        internal const double PlaybackPositionIntervalSeconds = 1.0;
        internal const string AuditMessageId = "Auditor.AuditEvent";

        public override Guid Id => AuditorDefinition.BackgroundPluginId;
        public override string Name => "AuditorBackgroundPlugin";
        public override List<EnvironmentType> TargetEnvironments => new List<EnvironmentType> { EnvironmentType.SmartClient };

        private EnvMessageHandler _msghandler = new EnvMessageHandler();

        private readonly List<ImageViewerAddOn> _imageViewers = new List<ImageViewerAddOn>();
        private DateTime _lastPlaybackPositionSent = DateTime.MinValue;
        private DateTime? _lastKnownPlaybackTime;
        private readonly Dictionary<ImageViewerAddOn, DateTime?> _lastIndependentPlaybackTime = new Dictionary<ImageViewerAddOn, DateTime?>();
        private readonly Dictionary<ImageViewerAddOn, DateTime> _lastIndependentEventWallTime = new Dictionary<ImageViewerAddOn, DateTime>();
        private readonly Dictionary<ImageViewerAddOn, DateTime?> _independentPlayStartTime = new Dictionary<ImageViewerAddOn, DateTime?>();
        private readonly Dictionary<ImageViewerAddOn, bool> _independentPlaying = new Dictionary<ImageViewerAddOn, bool>();
        private DateTime? _playStartRecordingTime;
        private Timer _independentStopCheckTimer;
        private string _lastEmittedMode;
        private string _lastEmittedCameraSet;
        private bool _inExportWorkspace;
        private bool _returningFromExport;

        // Audit rule config - cached matched rule for current user
        private Item _cachedRule;
        private bool _cachedSpecifyCameras;
        private HashSet<Guid> _cachedCameraIds = new HashSet<Guid>();

        private string _lastExportReason;

        private static readonly PluginLog _log = new PluginLog("SC Auditor");
        private readonly CrossMessageHandler _cmh = new CrossMessageHandler(_log);

        public override void Init()
        {
            _log.Info("BackgroundPlugin Init starting");
            ReloadAuditRules();

            _msghandler.Register(OnExportRelay, new MessageIdFilter(AuditMessageId));
            _msghandler.Register(OnConfigChanged, new MessageIdFilter(MessageId.Server.ConfigurationChangedIndication));
            _msghandler.Register(OnModeChanged, new MessageIdFilter(MessageId.System.ModeChangedIndication));
            _msghandler.Register(OnPlaybackTime, new MessageIdFilter(MessageId.SmartClient.PlaybackCurrentTimeIndication));
            _msghandler.Register(OnPlaybackIndication, new MessageIdFilter(MessageId.SmartClient.PlaybackIndication));
            _msghandler.Register(OnWorkspaceChanged, new MessageIdFilter(MessageId.SmartClient.ShownWorkSpaceChangedIndication));

            _cmh.Start();

            // Track ImageViewerAddOns for independent playback
            ClientControl.Instance.NewImageViewerControlEvent += OnNewImageViewerControl;

            // Check for independent playback stops every 2 seconds
            _independentStopCheckTimer = new Timer(_ => CheckIndependentPlaybackStops(), null, 2000, 2000);

            // Emit current mode after a short delay to capture initial state
            _initTimer = new Timer(_ => EmitCurrentMode(), null, 2000, Timeout.Infinite);
            _log.Info("BackgroundPlugin Init complete - all receivers registered");
        }

        private void ReloadAuditRules()
        {
            try
            {
                var items = Configuration.Instance.GetItemConfigurations(
                    AuditorDefinition.PluginId, null, AuditorDefinition.AuditRuleKindId);
                var rules = items ?? new List<Item>();
                _log.Info($"Loaded {rules.Count} audit rule(s)");

                // Resolve matching rule for current user once
                string currentUser = null;
                try
                {
                    var ls = LoginSettingsCache.GetLoginSettings(EnvironmentManager.Instance.MasterSite);
                    currentUser = ls.FullyQualifiedUserName;
                }
                catch { }

                Item matched = null;
                if (!string.IsNullOrEmpty(currentUser))
                {
                    foreach (var rule in rules)
                    {
                        if (!rule.Properties.ContainsKey("Enabled") || rule.Properties["Enabled"] != "Yes")
                            continue;

                        var userNames = rule.Properties.ContainsKey("UserNames")
                            ? rule.Properties["UserNames"]
                            : (rule.Properties.ContainsKey("UserName") ? rule.Properties["UserName"] : "");

                        var users = userNames.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
                        foreach (var user in users)
                        {
                            if (string.Equals(user.Trim(), currentUser, StringComparison.OrdinalIgnoreCase))
                            {
                                matched = rule;
                                break;
                            }
                        }
                        if (matched != null) break;
                    }
                }

                _cachedRule = matched;

                _cachedCameraIds.Clear();
                _cachedSpecifyCameras = false;
                if (matched != null)
                {
                    _cachedSpecifyCameras = matched.Properties.ContainsKey("SpecifyCameras") && matched.Properties["SpecifyCameras"] == "Yes";
                    if (_cachedSpecifyCameras)
                    {
                        var cameraIds = matched.Properties.ContainsKey("CameraIds") ? matched.Properties["CameraIds"] : "";
                        foreach (var idStr in cameraIds.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                        {
                            if (Guid.TryParse(idStr.Trim(), out var id))
                                _cachedCameraIds.Add(id);
                        }
                    }
                }

                if (matched != null)
                    _log.Info($"Matched audit rule '{matched.Name}' for user '{currentUser}'" +
                        (_cachedSpecifyCameras ? $" (cameras: {_cachedCameraIds.Count})" : " (all cameras)"));
                else
                    _log.Info($"No matching audit rule for user '{currentUser ?? "(unknown)"}'");
            }
            catch (Exception ex)
            {
                _log.Error($"Failed to load audit rules: {ex.Message}");
            }
        }

        private object OnConfigChanged(Message message, FQID dest, FQID source)
        {
            _log.Info("ConfigurationChangedIndication received, reloading audit rules");
            ReloadAuditRules();
            return null;
        }

        private void EmitCurrentMode()
        {
            var mode = EnvironmentManager.Instance.Mode;
            var cameras = GetAllCamerasInView();
            var modeStr = mode.ToString();
            var cameraKey = string.Join("|", cameras);

            if (modeStr.Contains("Playback"))
            {
                _lastEmittedMode = "Playback";
                _lastEmittedCameraSet = cameraKey;
                SendAudit(AuditEventType.GeneralPlayback, null, null, cameras);
            }
            else if (modeStr.Contains("Live"))
            {
                _lastEmittedMode = "Live";
                _lastEmittedCameraSet = cameraKey;
                SendAudit(AuditEventType.GeneralLive, null, null, cameras);
            }
        }

        public override void Close()
        {
            _log.Info("BackgroundPlugin closing");
            _initTimer?.Dispose();
            _initTimer = null;
            _modeChangeTimer?.Dispose();
            _modeChangeTimer = null;
            _independentStopCheckTimer?.Dispose();
            _independentStopCheckTimer = null;
            _msghandler.UnregisterAll();

            ClientControl.Instance.NewImageViewerControlEvent -= OnNewImageViewerControl;

            lock (_imageViewers)
            {
                foreach (var viewer in _imageViewers)
                    UnsubscribeFromViewer(viewer);
                _imageViewers.Clear();
                _lastIndependentPlaybackTime.Clear();
                _lastIndependentEventWallTime.Clear();
                _independentPlayStartTime.Clear();
                _independentPlaying.Clear();
            }

            _cmh.Close();
            _msghandler = null;
            _log.Info("BackgroundPlugin closed");
        }

        private object OnExportRelay(Message message, FQID dest, FQID source)
        {
            // Relay export events from ExportManager to Event Server via MC
            var auditData = message.Data as AuditEventData;
            if (auditData == null) return null;

            if (auditData.EventType == AuditEventType.ExportStarted)
            {
                if (string.IsNullOrEmpty(auditData.Reason) && !string.IsNullOrEmpty(_lastExportReason))
                    auditData.Reason = _lastExportReason;
                _log.Info($"Relaying export event '{auditData.EventType}' from ExportManager to Event Server");
                SendToEventServer(auditData);
            }
            return null;
        }

        #region Camera Filtering

        private List<Guid> GetAllCameraIdsInView()
        {
            var ids = new List<Guid>();
            lock (_imageViewers)
            {
                foreach (var viewer in _imageViewers)
                {
                    try
                    {
                        if (viewer.CameraFQID != null && !ids.Contains(viewer.CameraFQID.ObjectId))
                            ids.Add(viewer.CameraFQID.ObjectId);
                    }
                    catch { }
                }
            }
            return ids;
        }

        private bool ShouldAuditForCameras()
        {
            if (!_cachedSpecifyCameras) return true;
            if (_cachedCameraIds.Count == 0) return false;

            var idsInView = GetAllCameraIdsInView();
            foreach (var id in idsInView)
            {
                if (_cachedCameraIds.Contains(id))
                    return true;
            }
            return false;
        }

        private bool ShouldAuditForCamera(FQID cameraFqid)
        {
            if (!_cachedSpecifyCameras) return true;
            if (cameraFqid == null) return true;
            return _cachedCameraIds.Contains(cameraFqid.ObjectId);
        }

        #endregion

        #region Mode Changed

        private string _pendingMode;
        private List<string> _pendingCameras;
        private Timer _modeChangeTimer;
        private Timer _initTimer;

        private object OnModeChanged(Message message, FQID destination, FQID sender)
        {
            var dataStr = message.Data?.ToString() ?? "";

            string mode = null;
            if (dataStr.Contains("Playback"))
                mode = "Playback";
            else if (dataStr.Contains("Live"))
                mode = "Live";

            if (mode == null)
                return null;

            _pendingMode = mode;
            _pendingCameras = GetAllCamerasInView();

            if (_modeChangeTimer != null)
                _modeChangeTimer.Change(300, Timeout.Infinite);
            else
                _modeChangeTimer = new Timer(_ => ProcessModeChange(), null, 300, Timeout.Infinite);

            return null;
        }

        private void ProcessModeChange()
        {
            var mode = _pendingMode;
            var cameras = _pendingCameras;
            if (mode == null)
                return;

            if (_inExportWorkspace)
                return;

            var cameraKey = string.Join("|", cameras);

            if (mode == _lastEmittedMode && cameraKey == _lastEmittedCameraSet)
                return;

            _lastEmittedMode = mode;
            _lastEmittedCameraSet = cameraKey;

            if (_returningFromExport)
            {
                _returningFromExport = false;
                if (mode == "Playback")
                {
                    SendAudit(AuditEventType.GeneralPlayback, null, null, cameras);
                    return;
                }
            }

            if (mode == "Playback")
            {
                _log.Info("Mode changed to Playback");
                var rule = _cachedRule;
                string reason = null;
                bool camerasMatch = rule != null && ShouldAuditForCameras();
                bool promptEnabled = camerasMatch && (!rule.Properties.ContainsKey("PromptPlayback") || rule.Properties["PromptPlayback"] == "Yes");
                bool triggerEnabled = camerasMatch && (!rule.Properties.ContainsKey("TriggerPlayback") || rule.Properties["TriggerPlayback"] == "Yes");
                if (promptEnabled)
                {
                    _log.Info("Showing playback reason prompt (rule matched)");
                    reason = PromptForReason("Entering Playback Mode",
                        "You are switching to Playback mode.\nPlease provide a reason for accessing recorded footage.",
                        GetPredefinedReasons("P"));
                    _log.Info($"Playback reason provided: '{reason}'");
                }
                if (promptEnabled || triggerEnabled)
                    SendAudit(AuditEventType.GeneralPlayback, null, null, cameras, reason, fireEvent: triggerEnabled);
                else
                    SendAudit(AuditEventType.GeneralPlayback, null, null, cameras);
            }
            else
            {
                _log.Info("Mode changed to Live");
                SendAudit(AuditEventType.GeneralLive, null, null, cameras);
            }
        }

        private List<string> GetAllCamerasInView()
        {
            var cameras = new List<string>();
            lock (_imageViewers)
            {
                foreach (var viewer in _imageViewers)
                {
                    var name = GetCameraName(viewer);
                    if (name != null && !cameras.Contains(name))
                        cameras.Add(name);
                }
            }
            return cameras;
        }

        #endregion

        #region Global Playback Time

        private object OnPlaybackTime(Message message, FQID destination, FQID sender)
        {
            if (message.Data is DateTime dt)
                _lastKnownPlaybackTime = dt;

            var now = DateTime.UtcNow;
            if ((now - _lastPlaybackPositionSent).TotalSeconds < PlaybackPositionIntervalSeconds)
                return null;

            _lastPlaybackPositionSent = now;
            SendAudit(AuditEventType.PlaybackPosition, null, _lastKnownPlaybackTime);
            return null;
        }

        private object OnPlaybackIndication(Message message, FQID destination, FQID sender)
        {
            var pcd = message.Data as PlaybackCommandData;
            if (pcd == null)
            {
                _log.Info($"PlaybackIndication received but data is {message.Data?.GetType().Name ?? "null"}, not PlaybackCommandData");
                return null;
            }

            var action = pcd.Command;
            if (pcd.DateTime != DateTime.MinValue)
                _lastKnownPlaybackTime = pcd.DateTime;

            _log.Info($"PlaybackIndication: command={action} speed={pcd.Speed} time={pcd.DateTime}");

            if (string.IsNullOrEmpty(action))
                return null;

            // Only log meaningful user actions
            string details;
            switch (action)
            {
                case PlaybackData.PlayForward:
                case PlaybackData.PlayReverse:
                    _playStartRecordingTime = _lastKnownPlaybackTime;
                    var timelineRange = GetTimelineSelectedInterval();
                    if (timelineRange != null)
                        details = $"{action} (timeline: {timelineRange})";
                    else if (_lastKnownPlaybackTime.HasValue)
                        details = $"{action} (from: {_lastKnownPlaybackTime.Value:yyyy-MM-dd HH:mm:ss})";
                    else
                        details = action;
                    break;
                case PlaybackData.PlayStop:
                    if (_playStartRecordingTime.HasValue && _lastKnownPlaybackTime.HasValue)
                        details = $"{action} (range: {_playStartRecordingTime.Value:yyyy-MM-dd HH:mm:ss} - {_lastKnownPlaybackTime.Value:yyyy-MM-dd HH:mm:ss})";
                    else
                        details = action;
                    _playStartRecordingTime = null;
                    break;
                case PlaybackData.Goto:
                case PlaybackData.NextSequence:
                case PlaybackData.PreviousSequence:
                    details = action;
                    break;
                default:
                    return null;
            }

            _log.Info($"Playback action: {details} at recording time {_lastKnownPlaybackTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "(unknown)"}");

            var cameras = GetAllCamerasInView();
            SendAudit(AuditEventType.PlaybackAction, null, _lastKnownPlaybackTime, cameras, details: details, fireEvent: true);
            return null;
        }

        #endregion


        #region Workspace Changed

        private object OnWorkspaceChanged(Message message, FQID destination, FQID sender)
        {
            var workspaceName = "";
            try
            {
                if (message.Data is Item item)
                    workspaceName = item.Name ?? "";
                else
                    workspaceName = message.Data?.ToString() ?? "";
            }
            catch { }

            var isExport = workspaceName.IndexOf("Export", StringComparison.OrdinalIgnoreCase) >= 0;

            if (isExport && !_inExportWorkspace)
            {
                _inExportWorkspace = true;
                _log.Info("Entered Export workspace");
                var rule = _cachedRule;
                string reason = null;
                bool camerasMatch = rule != null && ShouldAuditForCameras();
                bool promptEnabled = camerasMatch && (!rule.Properties.ContainsKey("PromptExport") || rule.Properties["PromptExport"] == "Yes");
                bool triggerEnabled = camerasMatch && (!rule.Properties.ContainsKey("TriggerExport") || rule.Properties["TriggerExport"] == "Yes");
                if (promptEnabled)
                {
                    _log.Info("Showing export reason prompt (rule matched)");
                    reason = PromptForReason("Entering Export",
                        "You are entering the Export workspace.\nPlease provide a reason for exporting footage.",
                        GetPredefinedReasons("E"));
                    _log.Info($"Export reason provided: '{reason}'");
                }
                _lastExportReason = reason;
                if (promptEnabled || triggerEnabled)
                    SendAudit(AuditEventType.ExportWorkspaceEntered, null, null, reason: reason, fireEvent: triggerEnabled);
                else
                    SendAudit(AuditEventType.ExportWorkspaceEntered, null, null);
            }
            else if (!isExport && _inExportWorkspace)
            {
                _log.Info("Left Export workspace");
                _inExportWorkspace = false;
                _returningFromExport = true;
            }

            return null;
        }

        #endregion

        #region ImageViewerAddOn Tracking

        private void OnNewImageViewerControl(ImageViewerAddOn viewer)
        {
            lock (_imageViewers)
            {
                _imageViewers.Add(viewer);
            }

            viewer.CloseEvent += OnImageViewerClose;
            viewer.IndependentPlaybackModeChangedEvent += OnIndependentPlaybackModeChanged;
        }

        private void OnImageViewerClose(object sender, EventArgs e)
        {
            var viewer = (ImageViewerAddOn)sender;
            UnsubscribeFromViewer(viewer);
            lock (_imageViewers)
            {
                _imageViewers.Remove(viewer);
                _lastIndependentPlaybackTime.Remove(viewer);
                _lastIndependentEventWallTime.Remove(viewer);
                _independentPlayStartTime.Remove(viewer);
                _independentPlaying.Remove(viewer);
            }
        }

        private void OnIndependentPlaybackModeChanged(object sender, IndependentPlaybackModeEventArgs e)
        {
            var viewer = (ImageViewerAddOn)sender;
            var cameraName = GetCameraName(viewer);

            if (e.IndependentPlaybackEnabled)
            {
                _log.Info($"Independent playback enabled for '{cameraName}'");
                if (viewer.IndependentPlaybackController != null)
                {
                    viewer.IndependentPlaybackController.PlaybackTimeChangedEvent += OnIndependentPlaybackTimeChanged;
                }
                var rule = _cachedRule;
                string reason = null;
                bool camerasMatch = rule != null && ShouldAuditForCamera(viewer.CameraFQID);
                bool promptEnabled = camerasMatch && (!rule.Properties.ContainsKey("PromptIndependentPlayback") || rule.Properties["PromptIndependentPlayback"] == "Yes");
                bool triggerEnabled = camerasMatch && (!rule.Properties.ContainsKey("TriggerIndependentPlayback") || rule.Properties["TriggerIndependentPlayback"] == "Yes");
                if (promptEnabled)
                {
                    _log.Info("Showing independent playback reason prompt (rule matched)");
                    reason = PromptForReason("Independent Playback Enabled",
                        $"Independent playback has been enabled for '{cameraName ?? "unknown"}'.\nPlease provide a reason for accessing this camera's recordings.",
                        GetPredefinedReasons("I"));
                    _log.Info($"Independent playback reason provided: '{reason}'");
                }
                if (promptEnabled || triggerEnabled)
                    SendAudit(AuditEventType.IndependentPlaybackEnabled, cameraName, null, reason: reason, fireEvent: triggerEnabled);
                else
                    SendAudit(AuditEventType.IndependentPlaybackEnabled, cameraName, null);
            }
            else
            {
                DateTime? lastTime = null;
                lock (_imageViewers)
                {
                    _lastIndependentPlaybackTime.TryGetValue(viewer, out lastTime);
                    _lastIndependentPlaybackTime.Remove(viewer);
                    _lastIndependentEventWallTime.Remove(viewer);
                    _independentPlayStartTime.Remove(viewer);
                    _independentPlaying.Remove(viewer);
                }
                _log.Info($"Independent playback disabled for '{cameraName}' (last position: {lastTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "(none)"})");
                if (viewer.IndependentPlaybackController != null)
                {
                    viewer.IndependentPlaybackController.PlaybackTimeChangedEvent -= OnIndependentPlaybackTimeChanged;
                }
                SendAudit(AuditEventType.IndependentPlaybackDisabled, cameraName, lastTime);
            }
        }

        private void OnIndependentPlaybackTimeChanged(object sender, PlaybackController.TimeEventArgs e)
        {
            ImageViewerAddOn matchedViewer = null;
            string cameraName = null;

            lock (_imageViewers)
            {
                foreach (var viewer in _imageViewers)
                {
                    if (viewer.IndependentPlaybackEnabled && viewer.IndependentPlaybackController == sender)
                    {
                        matchedViewer = viewer;
                        _lastIndependentPlaybackTime[viewer] = e.Time;
                        _lastIndependentEventWallTime[viewer] = DateTime.UtcNow;
                        cameraName = GetCameraName(viewer);

                        // Detect play start
                        if (!_independentPlaying.ContainsKey(viewer) || !_independentPlaying[viewer])
                        {
                            _independentPlaying[viewer] = true;
                            _independentPlayStartTime[viewer] = e.Time;
                            var timelineRange = GetTimelineSelectedInterval();
                            var playDetails = timelineRange != null
                                ? $"PlayForward (Independent) (timeline: {timelineRange})"
                                : $"PlayForward (Independent) (from: {e.Time:yyyy-MM-dd HH:mm:ss})";
                            _log.Info($"Independent playback playing: '{cameraName}' {playDetails}");
                            SendAudit(AuditEventType.PlaybackAction, cameraName, e.Time, details: playDetails, fireEvent: true);
                        }
                        break;
                    }
                }
            }

            var now = DateTime.UtcNow;
            if ((now - _lastPlaybackPositionSent).TotalSeconds < PlaybackPositionIntervalSeconds)
                return;

            _lastPlaybackPositionSent = now;
            SendAudit(AuditEventType.PlaybackPosition, cameraName, e.Time);
        }

        private void CheckIndependentPlaybackStops()
        {
            var now = DateTime.UtcNow;
            List<(ImageViewerAddOn, DateTime?, DateTime?)> stopped = null;

            lock (_imageViewers)
            {
                foreach (var kvp in _independentPlaying)
                {
                    if (!kvp.Value) continue;
                    if (_lastIndependentEventWallTime.TryGetValue(kvp.Key, out var lastWall)
                        && (now - lastWall).TotalSeconds > 2.0)
                    {
                        _lastIndependentPlaybackTime.TryGetValue(kvp.Key, out var lastTime);
                        _independentPlayStartTime.TryGetValue(kvp.Key, out var startTime);
                        if (stopped == null) stopped = new List<(ImageViewerAddOn, DateTime?, DateTime?)>();
                        stopped.Add((kvp.Key, startTime, lastTime));
                    }
                }

                if (stopped != null)
                {
                    foreach (var item in stopped)
                    {
                        _independentPlaying[item.Item1] = false;
                        _independentPlayStartTime.Remove(item.Item1);
                    }
                }
            }

            if (stopped == null) return;

            foreach (var item in stopped)
            {
                var viewer = item.Item1;
                var startTime = item.Item2;
                var lastTime = item.Item3;
                var cameraName = GetCameraName(viewer);
                string details;
                if (startTime.HasValue && lastTime.HasValue)
                    details = $"PlayStop (Independent) (range: {startTime.Value:yyyy-MM-dd HH:mm:ss} - {lastTime.Value:yyyy-MM-dd HH:mm:ss})";
                else
                    details = "PlayStop (Independent)";
                _log.Info($"Independent playback stopped: '{cameraName}' {details}");
                SendAudit(AuditEventType.PlaybackAction, cameraName, lastTime, details: details, fireEvent: true);
            }
        }

        private void UnsubscribeFromViewer(ImageViewerAddOn viewer)
        {
            viewer.CloseEvent -= OnImageViewerClose;
            viewer.IndependentPlaybackModeChangedEvent -= OnIndependentPlaybackModeChanged;
            if (viewer.IndependentPlaybackEnabled && viewer.IndependentPlaybackController != null)
            {
                viewer.IndependentPlaybackController.PlaybackTimeChangedEvent -= OnIndependentPlaybackTimeChanged;
            }
        }

        private string GetTimelineSelectedInterval()
        {
            try
            {
                var msg = new Message(MessageId.SmartClient.GetTimelineSelectedIntervalRequest);
                var result = EnvironmentManager.Instance.SendMessage(msg);

                // SendMessage returns Collection<object> with responses from all receivers
                var responses = result as System.Collections.IEnumerable;
                if (responses == null)
                {
                    _log.Info("Timeline interval: no response");
                    return null;
                }

                foreach (var item in responses)
                {
                    if (item == null) continue;
                    var type = item.GetType();
                    var startProp = type.GetProperty("StartTime") ?? type.GetProperty("Start") ?? type.GetProperty("BeginTime");
                    var endProp = type.GetProperty("EndTime") ?? type.GetProperty("End") ?? type.GetProperty("Stop");
                    if (startProp != null && endProp != null)
                    {
                        var start = startProp.GetValue(item);
                        var end = endProp.GetValue(item);
                        if (start is DateTime s && end is DateTime e && s != DateTime.MinValue && e != DateTime.MinValue)
                            return $"{s:yyyy-MM-dd HH:mm:ss} - {e:yyyy-MM-dd HH:mm:ss}";
                    }
                }
            }
            catch (Exception ex)
            {
                _log.Error($"Failed to get timeline interval: {ex.Message}");
            }
            return null;
        }

        private static string GetCameraName(ImageViewerAddOn viewer)
        {
            try
            {
                if (viewer.CameraFQID != null)
                {
                    var item = Configuration.Instance.GetItem(viewer.CameraFQID);
                    if (item != null)
                        return item.Name;
                }
            }
            catch { }
            return null;
        }

        #endregion

        #region Reason Prompt

        private List<string> GetPredefinedReasons(string flag)
        {
            var rule = _cachedRule;
            if (rule == null) return null;

            var data = rule.Properties.ContainsKey("PredefinedReasons") ? rule.Properties["PredefinedReasons"] : "";
            if (string.IsNullOrEmpty(data)) return null;

            var result = new List<string>();
            foreach (var entry in data.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var sep = entry.LastIndexOf('|');
                if (sep < 0) continue;
                if (entry.Substring(sep + 1).Contains(flag))
                    result.Add(entry.Substring(0, sep));
            }
            return result.Count > 0 ? result : null;
        }

        private static string PromptForReason(string title, string promptText, List<string> predefinedReasons = null)
        {
            string reason = null;
            Application.Current.Dispatcher.Invoke(() =>
            {
                reason = ShowReasonDialog(title, promptText, predefinedReasons);
            });
            return reason;
        }

        private static string ShowReasonDialog(string title, string promptText, List<string> predefinedReasons = null)
        {
            bool hasPresets = predefinedReasons != null && predefinedReasons.Count > 0;

            var dialog = new Window
            {
                Title = title,
                Width = 480,
                Height = hasPresets ? 380 : 310,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ResizeMode = ResizeMode.NoResize,
                WindowStyle = WindowStyle.None,
                Background = new SolidColorBrush(Color.FromRgb(0x1C, 0x23, 0x26)),
            };

            bool canClose = false;
            dialog.Closing += (s, e) =>
            {
                if (!canClose)
                    e.Cancel = true;
            };

            var panel = new StackPanel { Margin = new Thickness(24) };

            panel.Children.Add(new TextBlock
            {
                Text = title,
                Foreground = new SolidColorBrush(Color.FromRgb(0xE6, 0xED, 0xF3)),
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 10),
            });

            panel.Children.Add(new TextBlock
            {
                Text = promptText,
                Foreground = new SolidColorBrush(Color.FromRgb(0xC9, 0xD1, 0xD9)),
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 14),
            });

            ComboBox reasonCombo = null;
            if (hasPresets)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = "Select a reason:",
                    Foreground = new SolidColorBrush(Color.FromRgb(0x8B, 0x94, 0x9E)),
                    FontSize = 12,
                    Margin = new Thickness(0, 0, 0, 4),
                });

                reasonCombo = new ComboBox
                {
                    FontSize = 12,
                    Margin = new Thickness(0, 0, 0, 10),
                    Padding = new Thickness(6, 4, 6, 4),
                };
                foreach (var r in predefinedReasons)
                    reasonCombo.Items.Add(r);
                panel.Children.Add(reasonCombo);
            }

            panel.Children.Add(new TextBlock
            {
                Text = hasPresets ? "Additional notes (optional):" : "Reason:",
                Foreground = new SolidColorBrush(Color.FromRgb(0x8B, 0x94, 0x9E)),
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 4),
            });

            var textBox = new TextBox
            {
                Height = 80,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                FontSize = 12,
                Background = new SolidColorBrush(Color.FromRgb(0x05, 0x08, 0x0B)),
                Foreground = new SolidColorBrush(Color.FromRgb(0xE6, 0xED, 0xF3)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x30, 0x36, 0x3D)),
                CaretBrush = new SolidColorBrush(Colors.White),
                Padding = new Thickness(6, 4, 6, 4),
            };
            panel.Children.Add(textBox);

            var okButton = new Button
            {
                Content = "Acknowledge",
                Padding = new Thickness(24, 8, 24, 8),
                MinHeight = 32,
                FontSize = 13,
                IsEnabled = false,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 16, 0, 0),
                Background = new SolidColorBrush(Color.FromRgb(0x23, 0x86, 0x36)),
                Foreground = new SolidColorBrush(Colors.White),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x2E, 0xA0, 0x43)),
            };
            okButton.Click += (s, e) => { canClose = true; dialog.DialogResult = true; };

            Action updateOkState = () =>
            {
                var enabled = (reasonCombo != null && reasonCombo.SelectedItem != null)
                    || !string.IsNullOrWhiteSpace(textBox.Text);
                okButton.IsEnabled = enabled;
                okButton.Background = new SolidColorBrush(enabled
                    ? Color.FromRgb(0x23, 0x86, 0x36)
                    : Color.FromRgb(0x3A, 0x3F, 0x44));
                okButton.BorderBrush = new SolidColorBrush(enabled
                    ? Color.FromRgb(0x2E, 0xA0, 0x43)
                    : Color.FromRgb(0x55, 0x55, 0x55));
            };

            textBox.TextChanged += (s, e) => updateOkState();
            if (reasonCombo != null)
                reasonCombo.SelectionChanged += (s, e) => updateOkState();

            panel.Children.Add(okButton);
            dialog.Content = panel;

            dialog.ShowDialog();

            var selectedReason = reasonCombo?.SelectedItem?.ToString();
            var additionalText = textBox.Text?.Trim();

            if (!string.IsNullOrEmpty(selectedReason))
            {
                if (!string.IsNullOrEmpty(additionalText))
                    return $"{selectedReason} - {additionalText}";
                return selectedReason;
            }
            return additionalText;
        }

        #endregion

        #region Send Audit

        internal void SendAudit(AuditEventType eventType, string cameraName, DateTime? playbackDate, List<string> camerasInView = null, string reason = null, string details = null, bool fireEvent = true)
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
                CameraName = cameraName,
                PlaybackDate = playbackDate,
                CamerasInView = camerasInView,
                Reason = reason,
                Details = details,
                FireEvent = fireEvent,
            };

            _log.Info($"SendAudit type={eventType} user={auditData.UserName} camera={cameraName ?? "(none)"} reason={reason ?? "(none)"}");

            // Send local message (for any local listeners)
            EnvironmentManager.Instance.SendMessage(
                new Message(AuditMessageId) { Data = auditData });

            // Send to Event Server via MessageCommunication
            SendToEventServer(auditData);
        }

        private void SendToEventServer(AuditEventData auditData)
        {
            if (_cmh.MessageCommunication == null) return;

            // Only send auditable event types to Event Server
            var eventType = auditData.EventType.ToString();
            switch (auditData.EventType)
            {
                case AuditEventType.GeneralPlayback:
                case AuditEventType.ExportStarted:
                case AuditEventType.ExportWorkspaceEntered:
                case AuditEventType.IndependentPlaybackEnabled:
                case AuditEventType.PlaybackAction:
                    break;
                default:
                    return; // Don't send non-auditable events like PlaybackPosition, GeneralLive
            }

            try
            {
                var report = new AuditEventReport
                {
                    EventType = eventType,
                    Timestamp = auditData.Timestamp,
                    UserName = auditData.UserName,
                    CameraName = auditData.CameraName,
                    PlaybackDate = auditData.PlaybackDate,
                    CamerasInView = auditData.CamerasInView?.ToArray(),
                    Reason = auditData.Reason,
                    Details = auditData.Details,
                    FireEvent = auditData.FireEvent,
                };

                _cmh.TransmitMessage(new Message(AuditMessageIds.AuditEventReport, report));
                _log.Info($"Transmitted audit event '{eventType}' to Event Server via MC");
            }
            catch (Exception ex)
            {
                _log.Error($"Failed to send audit to ES: {ex.Message}");
            }
        }

        #endregion
    }

    public enum AuditEventType
    {
        GeneralPlayback,
        GeneralLive,
        IndependentPlaybackEnabled,
        IndependentPlaybackDisabled,
        PlaybackPosition,
        ExportWorkspaceEntered,
        ExportStarted,
        PlaybackAction,
    }

    public class AuditEventData
    {
        public AuditEventType EventType { get; set; }
        public DateTime Timestamp { get; set; }
        public string UserName { get; set; }
        public string CameraName { get; set; }
        public DateTime? PlaybackDate { get; set; }
        public List<string> CamerasInView { get; set; }
        public string Reason { get; set; }
        public string Details { get; set; }
        public bool FireEvent { get; set; }
    }
}
