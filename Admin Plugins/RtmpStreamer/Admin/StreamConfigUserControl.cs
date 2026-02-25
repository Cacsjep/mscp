using RTMPStreamer.Messaging;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using VideoOS.Platform;
using VideoOS.Platform.Messaging;
using VideoOS.Platform.UI;

namespace RTMPStreamer.Admin
{
    public partial class StreamConfigUserControl : UserControl
    {
        private Item _selectedCameraItem;
        private Guid _currentItemId;

        private MessageCommunication _mc;
        private object _statusUpdateFilter;
        private object _statusResponseFilter;
        private Guid _subscribedItemId;
        private Timer _responseTimer;
        private int _subscribeGeneration;
        private int _lastLogHash;

        private static readonly Regex LogLineRegex = new Regex(
            @"^(\d{2}:\d{2}:\d{2}\.\d{3})\s+(DEBUG|INFO|WARN|ERROR)\s+(.*)$",
            RegexOptions.Compiled);

        private static readonly Color ColorDebug = Color.Gray;
        private static readonly Color ColorInfo = Color.FromArgb(0, 100, 0);
        private static readonly Color ColorWarn = Color.FromArgb(180, 130, 0);
        private static readonly Color ColorError = Color.FromArgb(200, 0, 0);

        internal event EventHandler ConfigurationChangedByUser;

        public StreamConfigUserControl()
        {
            InitializeComponent();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                UnsubscribeLiveStatus();
                components?.Dispose();
            }
            base.Dispose(disposing);
        }

        public string DisplayName => _txtName.Text;

        public void FillContent(Item item)
        {
            if (item == null)
            {
                ClearContent();
                return;
            }

            _currentItemId = item.FQID.ObjectId;
            _txtName.Text = item.Name;

            // Load camera
            if (item.Properties.ContainsKey("CameraId"))
            {
                var cameraIdStr = item.Properties["CameraId"];
                if (Guid.TryParse(cameraIdStr, out var cameraId) && cameraId != Guid.Empty)
                {
                    var cameraItem = Configuration.Instance.GetItem(cameraId, Kind.Camera);
                    if (cameraItem != null)
                    {
                        _selectedCameraItem = cameraItem;
                        _btnSelectCamera.Text = cameraItem.Name;
                    }
                    else
                    {
                        _btnSelectCamera.Text = item.Properties.ContainsKey("CameraName")
                            ? item.Properties["CameraName"] + " (not found)"
                            : "(Camera not found)";
                    }
                }
                else
                {
                    _btnSelectCamera.Text = "(Select camera...)";
                }
            }
            else
            {
                _btnSelectCamera.Text = "(Select camera...)";
            }

            // Load RTMP URL
            _txtRtmpUrl.Text = item.Properties.ContainsKey("RtmpUrl")
                ? item.Properties["RtmpUrl"] : "";

            // Load enabled state
            _chkEnabled.Checked = !item.Properties.ContainsKey("Enabled")
                || item.Properties["Enabled"] != "No";

            // Load allow untrusted certs
            _chkAllowUntrustedCerts.Checked = item.Properties.ContainsKey("AllowUntrustedCerts")
                && item.Properties["AllowUntrustedCerts"] == "Yes";

            if (_chkEnabled.Checked)
                SubscribeLiveStatus(item.FQID.ObjectId);
            else
                ShowDisabledStatus();
        }

        public string ValidateInput()
        {
            if (string.IsNullOrWhiteSpace(_txtName.Text))
                return "Please enter a name for the stream.";

            if (_selectedCameraItem == null)
                return "Please select a camera.";

            var rtmpUrl = _txtRtmpUrl.Text.Trim();
            if (string.IsNullOrEmpty(rtmpUrl) || rtmpUrl == "rtmp://")
                return "Please enter an RTMP URL.";

            if (!Regex.IsMatch(rtmpUrl, @"^rtmps?://[^\s]+$", RegexOptions.IgnoreCase))
                return "RTMP URL must start with rtmp:// or rtmps:// and contain no spaces.";

            return null;
        }

        public void UpdateItem(Item item)
        {
            if (item == null) return;

            item.Name = _txtName.Text;

            if (_selectedCameraItem != null)
            {
                item.Properties["CameraId"] = _selectedCameraItem.FQID.ObjectId.ToString();
                item.Properties["CameraName"] = _selectedCameraItem.Name;
            }

            item.Properties["RtmpUrl"] = _txtRtmpUrl.Text.Trim();

            item.Properties["Enabled"] = _chkEnabled.Checked ? "Yes" : "No";
            item.Properties["AllowUntrustedCerts"] = _chkAllowUntrustedCerts.Checked ? "Yes" : "No";
        }

        public void ClearContent()
        {
            UnsubscribeLiveStatus();

            _currentItemId = Guid.Empty;
            _selectedCameraItem = null;
            _txtName.Text = "";
            _btnSelectCamera.Text = "(Select camera...)";
            _txtRtmpUrl.Text = "";
            _chkEnabled.Checked = true;
            _chkAllowUntrustedCerts.Checked = false;
            _lblStatusValue.Text = "";
            _lblStatsValue.Text = "";
            _dgvLog.Rows.Clear();
        }

        #region Live Status

        internal void SubscribeLiveStatus(Guid itemId)
        {
            UnsubscribeLiveStatus();

            _subscribedItemId = itemId;
            var generation = ++_subscribeGeneration;
            _lblStatusValue.Text = "Waiting for status...";
            _lblStatusValue.ForeColor = SystemColors.ControlText;
            _lblStatsValue.Text = "";
            _lastLogHash = 0;
            _dgvLog.Rows.Clear();

            // Do all MessageCommunication work off the UI thread —
            // Start() can block for seconds on first call.
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    var serverId = EnvironmentManager.Instance.MasterSite.ServerId;
                    MessageCommunicationManager.Start(serverId);
                    var mc = MessageCommunicationManager.Get(serverId);

                    // Register filters before checking generation — we'll clean up if stale
                    var updateFilter = mc.RegisterCommunicationFilter(
                        OnStatusMessage,
                        new CommunicationIdFilter(StreamMessageIds.StatusUpdate));

                    var responseFilter = mc.RegisterCommunicationFilter(
                        OnStatusMessage,
                        new CommunicationIdFilter(StreamMessageIds.StatusResponse));

                    // Marshal field stores to UI thread to avoid racing with UnsubscribeLiveStatus
                    try
                    {
                        BeginInvoke(new Action(() =>
                        {
                            if (_subscribeGeneration != generation)
                            {
                                // Stale — user switched items while we were connecting.
                                // Unregister the orphaned filters immediately.
                                mc.UnRegisterCommunicationFilter(updateFilter);
                                mc.UnRegisterCommunicationFilter(responseFilter);
                                return;
                            }

                            _mc = mc;
                            _statusUpdateFilter = updateFilter;
                            _statusResponseFilter = responseFilter;

                            _responseTimer = new Timer { Interval = 3000 };
                            _responseTimer.Tick += OnResponseTimeout;
                            _responseTimer.Start();
                        }));
                    }
                    catch (ObjectDisposedException)
                    {
                        mc.UnRegisterCommunicationFilter(updateFilter);
                        mc.UnRegisterCommunicationFilter(responseFilter);
                        return;
                    }

                    // Request initial state (safe to do even if filters get cleaned up)
                    var request = new StreamStatusRequest { ItemId = itemId };
                    mc.TransmitMessage(
                        new VideoOS.Platform.Messaging.Message(StreamMessageIds.StatusRequest, request), null, null, null);
                }
                catch (Exception)
                {
                    try
                    {
                        BeginInvoke(new Action(() =>
                        {
                            if (_subscribeGeneration != generation) return;
                            _lblStatusValue.Text = "Event Server not reachable";
                            _lblStatusValue.ForeColor = SystemColors.GrayText;
                        }));
                    }
                    catch (ObjectDisposedException) { }
                }
            });
        }

        internal void UnsubscribeLiveStatus()
        {
            _subscribedItemId = Guid.Empty;

            if (_responseTimer != null)
            {
                _responseTimer.Stop();
                _responseTimer.Tick -= OnResponseTimeout;
                _responseTimer.Dispose();
                _responseTimer = null;
            }

            if (_mc != null)
            {
                if (_statusUpdateFilter != null)
                {
                    _mc.UnRegisterCommunicationFilter(_statusUpdateFilter);
                    _statusUpdateFilter = null;
                }
                if (_statusResponseFilter != null)
                {
                    _mc.UnRegisterCommunicationFilter(_statusResponseFilter);
                    _statusResponseFilter = null;
                }
            }
            _mc = null;
        }

        private void OnResponseTimeout(object sender, EventArgs e)
        {
            if (_responseTimer != null)
            {
                _responseTimer.Stop();
                _responseTimer.Tick -= OnResponseTimeout;
                _responseTimer.Dispose();
                _responseTimer = null;
            }

            if (_lblStatusValue.Text == "Waiting for status...")
            {
                _lblStatusValue.Text = "No response from Event Server";
                _lblStatusValue.ForeColor = SystemColors.GrayText;
            }
        }

        private object OnStatusMessage(VideoOS.Platform.Messaging.Message message, FQID dest, FQID source)
        {
            var update = message.Data as StreamStatusUpdate;
            if (update == null || update.ItemId != _subscribedItemId)
                return null;

            if (InvokeRequired)
            {
                try { BeginInvoke(new Action(() => ApplyStatusUpdate(update))); }
                catch (ObjectDisposedException) { }
            }
            else
            {
                ApplyStatusUpdate(update);
            }
            return null;
        }

        private void ApplyStatusUpdate(StreamStatusUpdate update)
        {
            if (IsDisposed || !IsHandleCreated) return;

            // Cancel timeout timer on first response
            if (_responseTimer != null)
            {
                _responseTimer.Stop();
                _responseTimer.Tick -= OnResponseTimeout;
                _responseTimer.Dispose();
                _responseTimer = null;
            }

            // Status with color
            _lblStatusValue.Text = update.Status ?? "Unknown";
            if (update.Status != null)
            {
                if (update.Status.StartsWith("Streaming"))
                    _lblStatusValue.ForeColor = Color.Green;
                else if (update.Status.StartsWith("Error") || update.Status.StartsWith("Codec"))
                    _lblStatusValue.ForeColor = Color.Red;
                else
                    _lblStatusValue.ForeColor = SystemColors.ControlText;
            }

            // Stats line: FPS and bitrate only
            if (update.Fps > 0 || update.Bytes > 0)
            {
                var bitrateStr = FormatBitrate(update.Bytes, update.Fps, update.Frames);
                _lblStatsValue.Text = string.Format("FPS: {0:F1}  |  Bitrate: {1}  |  Restarts: {2}",
                    update.Fps, bitrateStr, update.RestartCount);
            }
            else
            {
                _lblStatsValue.Text = update.RestartCount > 0
                    ? $"Restarts: {update.RestartCount}"
                    : "";
            }

            // Log grid — only rebuild when content actually changed
            if (update.RecentLogLines != null && update.RecentLogLines.Count > 0)
            {
                var logHash = update.RecentLogLines.Count;
                unchecked
                {
                    for (int i = 0; i < update.RecentLogLines.Count; i++)
                        logHash = logHash * 31 + (update.RecentLogLines[i]?.GetHashCode() ?? 0);
                }

                if (logHash != _lastLogHash)
                {
                    _lastLogHash = logHash;
                    _dgvLog.Rows.Clear();
                    foreach (var line in update.RecentLogLines)
                    {
                        var match = LogLineRegex.Match(line);
                        int rowIdx;
                        if (match.Success)
                        {
                            rowIdx = _dgvLog.Rows.Add(match.Groups[1].Value, match.Groups[2].Value, match.Groups[3].Value);
                            var levelColor = GetLevelColor(match.Groups[2].Value);
                            _dgvLog.Rows[rowIdx].Cells[1].Style.ForeColor = levelColor;
                            _dgvLog.Rows[rowIdx].Cells[1].Style.SelectionForeColor = levelColor;
                        }
                        else
                        {
                            rowIdx = _dgvLog.Rows.Add("", "", line);
                        }
                    }

                    if (_dgvLog.Rows.Count > 0)
                        _dgvLog.FirstDisplayedScrollingRowIndex = _dgvLog.Rows.Count - 1;
                }
            }
        }

        private static Color GetLevelColor(string level)
        {
            switch (level)
            {
                case "DEBUG": return ColorDebug;
                case "INFO": return ColorInfo;
                case "WARN": return ColorWarn;
                case "ERROR": return ColorError;
                default: return SystemColors.ControlText;
            }
        }

        private static string FormatBitrate(long totalBytes, double fps, long totalFrames)
        {
            if (fps <= 0 || totalFrames <= 0) return "-- Mbps";
            // Estimate: bytes/sec = totalBytes / (totalFrames / fps)
            var seconds = totalFrames / fps;
            if (seconds <= 0) return "-- Mbps";
            var bitsPerSecond = (totalBytes * 8.0) / seconds;
            var mbps = bitsPerSecond / (1000.0 * 1000.0);
            return $"{mbps:F2} Mbps";
        }

        #endregion

        private void BtnSelectCamera_Click(object sender, EventArgs e)
        {
            var form = new ItemPickerWpfWindow
            {
                Items = Configuration.Instance.GetItemsByKind(Kind.Camera),
                KindsFilter = new List<Guid> { Kind.Camera },
                SelectionMode = SelectionModeOptions.AutoCloseOnSelect,
            };

            if (form.ShowDialog() == true && form.SelectedItems != null && form.SelectedItems.Any())
            {
                _selectedCameraItem = form.SelectedItems.First();
                _btnSelectCamera.Text = _selectedCameraItem.Name;
                OnUserChange(sender, e);
            }
        }

        internal void OnUserChange(object sender, EventArgs e)
        {
            if (sender == _chkEnabled && _currentItemId != Guid.Empty)
            {
                if (!_chkEnabled.Checked)
                    ShowDisabledStatus();
                else
                    SubscribeLiveStatus(_currentItemId);
            }

            ConfigurationChangedByUser?.Invoke(this, EventArgs.Empty);
        }

        private void ShowDisabledStatus()
        {
            UnsubscribeLiveStatus();
            _lblStatusValue.Text = "Disabled";
            _lblStatusValue.ForeColor = SystemColors.GrayText;
            _lblStatsValue.Text = "";
            _dgvLog.Rows.Clear();
        }
    }
}
