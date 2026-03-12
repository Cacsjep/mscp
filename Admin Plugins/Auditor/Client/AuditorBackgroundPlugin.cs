using Auditor.Messaging;
using CommunitySDK;
using System;
using System.Collections.Generic;
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
        private string _lastEmittedMode;
        private string _lastEmittedCameraSet;
        private bool _inExportWorkspace;
        private bool _returningFromExport;

        // Audit rule config - cached matched rule for current user
        private Item _cachedRule;
        private bool _cachedSpecifyCameras;
        private HashSet<Guid> _cachedCameraIds = new HashSet<Guid>();

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
            _msghandler.Register(OnWorkspaceChanged, new MessageIdFilter(MessageId.SmartClient.ShownWorkSpaceChangedIndication));

            _cmh.Start();

            // Track ImageViewerAddOns for independent playback
            ClientControl.Instance.NewImageViewerControlEvent += OnNewImageViewerControl;

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
            _msghandler.UnregisterAll();

            ClientControl.Instance.NewImageViewerControlEvent -= OnNewImageViewerControl;

            lock (_imageViewers)
            {
                foreach (var viewer in _imageViewers)
                    UnsubscribeFromViewer(viewer);
                _imageViewers.Clear();
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
                        "You are switching to Playback mode.\nPlease provide a reason for accessing recorded footage.");
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
            var now = DateTime.UtcNow;
            if ((now - _lastPlaybackPositionSent).TotalSeconds < PlaybackPositionIntervalSeconds)
                return null;

            _lastPlaybackPositionSent = now;

            DateTime? playbackDate = null;
            if (message.Data is DateTime dt)
                playbackDate = dt;

            SendAudit(AuditEventType.PlaybackPosition, null, playbackDate);
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
                        "You are entering the Export workspace.\nPlease provide a reason for exporting footage.");
                    _log.Info($"Export reason provided: '{reason}'");
                }
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
                        $"Independent playback has been enabled for '{cameraName ?? "unknown"}'.\nPlease provide a reason for accessing this camera's recordings.");
                    _log.Info($"Independent playback reason provided: '{reason}'");
                }
                if (promptEnabled || triggerEnabled)
                    SendAudit(AuditEventType.IndependentPlaybackEnabled, cameraName, null, reason: reason, fireEvent: triggerEnabled);
                else
                    SendAudit(AuditEventType.IndependentPlaybackEnabled, cameraName, null);
            }
            else
            {
                _log.Info($"Independent playback disabled for '{cameraName}'");
                if (viewer.IndependentPlaybackController != null)
                {
                    viewer.IndependentPlaybackController.PlaybackTimeChangedEvent -= OnIndependentPlaybackTimeChanged;
                }
                SendAudit(AuditEventType.IndependentPlaybackDisabled, cameraName, null);
            }
        }

        private void OnIndependentPlaybackTimeChanged(object sender, PlaybackController.TimeEventArgs e)
        {
            var now = DateTime.UtcNow;
            if ((now - _lastPlaybackPositionSent).TotalSeconds < PlaybackPositionIntervalSeconds)
                return;

            _lastPlaybackPositionSent = now;

            string cameraName = null;
            lock (_imageViewers)
            {
                foreach (var viewer in _imageViewers)
                {
                    if (viewer.IndependentPlaybackEnabled && viewer.IndependentPlaybackController == sender)
                    {
                        cameraName = GetCameraName(viewer);
                        break;
                    }
                }
            }

            SendAudit(AuditEventType.PlaybackPosition, cameraName, e.Time);
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

        private static string PromptForReason(string title, string promptText)
        {
            string reason = null;
            Application.Current.Dispatcher.Invoke(() =>
            {
                reason = ShowReasonDialog(title, promptText);
            });
            return reason;
        }

        private static string ShowReasonDialog(string title, string promptText)
        {
            var dialog = new Window
            {
                Title = title,
                Width = 480,
                Height = 310,
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

            panel.Children.Add(new TextBlock
            {
                Text = "Reason:",
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

            textBox.TextChanged += (s, e) =>
            {
                var enabled = !string.IsNullOrWhiteSpace(textBox.Text);
                okButton.IsEnabled = enabled;
                okButton.Background = new SolidColorBrush(enabled
                    ? Color.FromRgb(0x23, 0x86, 0x36)
                    : Color.FromRgb(0x3A, 0x3F, 0x44));
                okButton.BorderBrush = new SolidColorBrush(enabled
                    ? Color.FromRgb(0x2E, 0xA0, 0x43)
                    : Color.FromRgb(0x55, 0x55, 0x55));
            };

            panel.Children.Add(okButton);
            dialog.Content = panel;

            dialog.ShowDialog();
            return textBox.Text?.Trim();
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
