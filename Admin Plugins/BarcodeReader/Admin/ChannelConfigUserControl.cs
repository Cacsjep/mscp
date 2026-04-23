using BarcodeReader.Background;
using BarcodeReader.Messaging;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using VideoOS.Platform;
using VideoOS.Platform.Client;
using VideoOS.Platform.Messaging;
using VideoOS.Platform.UI;

namespace BarcodeReader.Admin
{
    public partial class ChannelConfigUserControl : UserControl
    {
        // Controls (see Designer.cs):
        //  _txtName             Friendly channel name (shown in the tree and in logs).
        //  _btnSelectCamera     Opens the standard MIP camera picker.
        //  _chkEnabled          When off, the helper process for this channel is not launched.
        //  _pbPreview           Live preview of the selected camera (BitmapSource).
        //  _clbFormats          Multi-select list of barcode formats to try. Each entry's Token
        //                       is persisted and parsed by the helper; Display drives the UI.
        //  _chkTryHarder        Spend more CPU per frame to decode weak/rotated codes.
        //  _chkAutoRotate       Retry frame rotated 90/180/270 deg.
        //  _chkTryInverted      Retry with inverted colors.
        //  _chkCreateBookmarks  Persist each detection as a Milestone bookmark (2 s pre/post).
        //  _numTargetFps        Hard cap on decode attempts/sec.
        //  _cboDownscale        Optional downscale width in px (0 = native).
        //  _numDebounceMs       Suppress repeat detections of the same text within this window.
        //  _lblStatusValue      Status line (Connecting / Running / Error:*). Color-coded.
        //  _lblStatsValue       One-line stats readout from the helper's STATS line.
        //  _lblHint             Green/yellow recommendation derived from the numbers above.
        //  _dgvLog              Ring buffer of the last 40 stderr lines (incl. DETECT) from
        //                       the helper process. Newest first.

        private Item _selectedCameraItem;
        private Guid _currentItemId;

        private MessageCommunication _mc;
        private object _statusUpdateFilter;
        private object _statusResponseFilter;
        private Guid _subscribedItemId;
        private Timer _responseTimer;
        private int _lastLogHash;

        // ItemManager.FillUserControl is called BEFORE the control gets a window handle
        // (the Management Client only realises it once it's docked into the detail pane).
        // BeginInvoke throws in that window, so if FillContent arrives early we stash the
        // channel id and kick the subscribe off once the handle exists.
        private Guid _pendingSubscribeItemId;

        // Admin live-preview (see VideoPreview sample, https://github.com/milestonesys/mipsdk-samples-plugin/tree/main/VideoPreview).
        // BitmapSource + a playback controller is the supported way to render camera video
        // inside the Management Client without pulling in the full Smart Client viewer.
        private BitmapSource _bitmapSource;
        private FQID _playbackFQID;
        private bool _previewStarted;

        private static readonly Color ColorGood = Color.FromArgb(0, 120, 0);
        private static readonly Color ColorWarn = Color.FromArgb(180, 130, 0);
        private static readonly Color ColorBad  = Color.FromArgb(200, 0, 0);

        /// <summary>
        /// Item backing the barcode-format CheckedListBox. ToString drives the visible label;
        /// the Token is what the helper (and Item.Properties) persists. Kept as a nested type
        /// so the whole mapping lives in one place.
        /// </summary>
        private sealed class FormatChoice
        {
            public string Token;
            public string Display;
            public override string ToString() => Display;
        }

        private static readonly FormatChoice[] AllFormats =
        {
            new FormatChoice { Token = "qr",          Display = "QR Code" },
            new FormatChoice { Token = "data_matrix", Display = "Data Matrix" },
            new FormatChoice { Token = "aztec",       Display = "Aztec" },
            new FormatChoice { Token = "pdf417",      Display = "PDF417" },
            new FormatChoice { Token = "code128",     Display = "Code 128" },
            new FormatChoice { Token = "code39",      Display = "Code 39" },
            new FormatChoice { Token = "code93",      Display = "Code 93" },
            new FormatChoice { Token = "ean13",       Display = "EAN-13" },
            new FormatChoice { Token = "ean8",        Display = "EAN-8" },
            new FormatChoice { Token = "upca",        Display = "UPC-A" },
            new FormatChoice { Token = "upce",        Display = "UPC-E" },
            new FormatChoice { Token = "itf",         Display = "ITF" },
            new FormatChoice { Token = "codabar",     Display = "Codabar" },
        };

        internal event EventHandler ConfigurationChangedByUser;

        public ChannelConfigUserControl()
        {
            InitializeComponent();
            PopulateFormatList();
            PopulateDownscaleList();
            InitPreview();
        }

        private void InitPreview()
        {
            try
            {
                _playbackFQID = ClientControl.Instance.GeneratePlaybackController();
                _bitmapSource = new BitmapSource
                {
                    PlaybackFQID = _playbackFQID,
                    Selected = true,
                };
                _bitmapSource.NewBitmapEvent += OnNewPreviewBitmap;

                // Keep the PlaybackController in live mode. Skipping modes avoid it trying
                // to seek/buffer recorded video when we only want the current live frame.
                EnvironmentManager.Instance.SendMessage(
                    new VideoOS.Platform.Messaging.Message(
                        MessageId.SmartClient.PlaybackSkipModeCommand,
                        PlaybackSkipModeData.Skip),
                    _playbackFQID);
            }
            catch
            {
                // If the media env isn't available the preview is optional, not required.
                _bitmapSource = null;
            }
        }

        private void OnNewPreviewBitmap(Bitmap bitmap)
        {
            // BitmapSource disposes the original after the event returns, so we always need
            // a copy. Clone at NATIVE resolution and let the PictureBox's Zoom size-mode
            // scale the image while preserving aspect - pre-scaling to the box dimensions
            // (the pattern in the VideoPreview sample) squashes the picture on wide boxes.
            try
            {
                var copy = new Bitmap(bitmap);
                var old = _pbPreview.Image;
                _pbPreview.Image = copy;
                old?.Dispose();
            }
            catch { }
            finally { try { bitmap.Dispose(); } catch { } }
        }

        private void StartPreview(Item cameraItem)
        {
            if (_bitmapSource == null || cameraItem == null) return;
            try
            {
                if (_previewStarted) StopPreview();
                _bitmapSource.Item = cameraItem;
                _bitmapSource.Init();
                _bitmapSource.LiveStart();
                _previewStarted = true;
            }
            catch { _previewStarted = false; }
        }

        private void StopPreview()
        {
            if (_bitmapSource == null) return;
            try { if (_previewStarted) _bitmapSource.LiveStop(); } catch { }
            try { _bitmapSource.Close(); } catch { }
            _previewStarted = false;

            var old = _pbPreview.Image;
            _pbPreview.Image = null;
            old?.Dispose();
        }

        private void ClosePreview()
        {
            StopPreview();
            if (_bitmapSource != null)
            {
                try { _bitmapSource.NewBitmapEvent -= OnNewPreviewBitmap; } catch { }
                _bitmapSource = null;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                UnsubscribeLiveStatus();
                ClosePreview();
                components?.Dispose();
            }
            base.Dispose(disposing);
        }

        public string DisplayName => _txtName.Text;

        private void PopulateFormatList()
        {
            _clbFormats.Items.Clear();
            foreach (var f in AllFormats)
                _clbFormats.Items.Add(f, true);
        }

        private void PopulateDownscaleList()
        {
            _cboDownscale.Items.Clear();
            _cboDownscale.Items.AddRange(new object[]
            {
                "Native resolution",
                "1920 px wide",
                "1280 px wide",
                "960 px wide",
                "640 px wide",
            });
            _cboDownscale.SelectedIndex = 0;
        }

        public void FillContent(Item item)
        {
            if (item == null) { ClearContent(); return; }

            _currentItemId = item.FQID.ObjectId;
            _txtName.Text = item.Name ?? "";

            if (item.Properties.ContainsKey(ChannelConfig.KeyCameraId) &&
                Guid.TryParse(item.Properties[ChannelConfig.KeyCameraId], out var cameraId) &&
                cameraId != Guid.Empty)
            {
                var cameraItem = Configuration.Instance.GetItem(cameraId, Kind.Camera);
                if (cameraItem != null)
                {
                    _selectedCameraItem = cameraItem;
                    _btnSelectCamera.Text = cameraItem.Name;
                    StartPreview(cameraItem);
                }
                else
                {
                    _btnSelectCamera.Text = item.Properties.ContainsKey(ChannelConfig.KeyCameraName)
                        ? item.Properties[ChannelConfig.KeyCameraName] + " (not found)"
                        : "(Camera not found)";
                    StopPreview();
                }
            }
            else
            {
                _btnSelectCamera.Text = "(Select camera...)";
                StopPreview();
            }

            var cfg = ChannelConfig.FromItem(item);
            _chkEnabled.Checked         = cfg.Enabled;
            _chkTryHarder.Checked       = cfg.TryHarder;
            _chkAutoRotate.Checked      = cfg.AutoRotate;
            _chkTryInverted.Checked     = cfg.TryInverted;
            _chkCreateBookmarks.Checked = cfg.CreateBookmarks;
            _numTargetFps.Value      = Math.Max(_numTargetFps.Minimum, Math.Min(_numTargetFps.Maximum, cfg.TargetFps));
            _numDebounceMs.Value     = Math.Max(_numDebounceMs.Minimum, Math.Min(_numDebounceMs.Maximum, cfg.DebounceMs));

            SelectDownscale(cfg.DownscaleWidth);
            SelectFormats(cfg.Formats);

            if (_chkEnabled.Checked) SubscribeLiveStatus(item.FQID.ObjectId);
            else                     ShowDisabledStatus();
        }

        private void SelectDownscale(int width)
        {
            // Downscale items are "Native resolution" or "<width> px wide". Match on the
            // leading number so the persisted value and the visible label can evolve
            // independently.
            if (width <= 0) { _cboDownscale.SelectedIndex = 0; return; }
            for (int i = 1; i < _cboDownscale.Items.Count; i++)
            {
                var label = _cboDownscale.Items[i].ToString();
                var space = label.IndexOf(' ');
                if (space > 0 && int.TryParse(label.Substring(0, space), out var w) && w == width)
                {
                    _cboDownscale.SelectedIndex = i; return;
                }
            }
            _cboDownscale.SelectedIndex = 0;
        }

        private void SelectFormats(string csv)
        {
            var enabled = new HashSet<string>(
                (csv ?? "").Split(',').Select(s => s.Trim().ToLowerInvariant()).Where(s => s.Length > 0));

            for (int i = 0; i < _clbFormats.Items.Count; i++)
            {
                var token = (_clbFormats.Items[i] as FormatChoice)?.Token;
                _clbFormats.SetItemChecked(i, token != null && enabled.Contains(token));
            }
        }

        public string ValidateInput()
        {
            if (string.IsNullOrWhiteSpace(_txtName.Text)) return "Please enter a name for the channel.";
            if (_selectedCameraItem == null) return "Please select a camera.";
            if (_clbFormats.CheckedItems.Count == 0) return "Select at least one barcode format.";
            return null;
        }

        public void UpdateItem(Item item)
        {
            if (item == null) return;
            item.Name = _txtName.Text;

            if (_selectedCameraItem != null)
            {
                item.Properties[ChannelConfig.KeyCameraId]   = _selectedCameraItem.FQID.ObjectId.ToString();
                item.Properties[ChannelConfig.KeyCameraName] = _selectedCameraItem.Name;
            }

            item.Properties[ChannelConfig.KeyEnabled]     = _chkEnabled.Checked ? "Yes" : "No";
            item.Properties[ChannelConfig.KeyFormats]     = GetCheckedFormatsCsv();
            item.Properties[ChannelConfig.KeyTryHarder]   = _chkTryHarder.Checked   ? "Yes" : "No";
            item.Properties[ChannelConfig.KeyAutoRotate]  = _chkAutoRotate.Checked  ? "Yes" : "No";
            item.Properties[ChannelConfig.KeyTryInverted] = _chkTryInverted.Checked ? "Yes" : "No";
            item.Properties[ChannelConfig.KeyTargetFps]   = ((int)_numTargetFps.Value).ToString();
            item.Properties[ChannelConfig.KeyDebounceMs]  = ((int)_numDebounceMs.Value).ToString();
            item.Properties[ChannelConfig.KeyDownscaleWidth] = GetDownscaleValue().ToString();
            item.Properties[ChannelConfig.KeyCreateBookmarks] = _chkCreateBookmarks.Checked ? "Yes" : "No";
        }

        private string GetCheckedFormatsCsv()
        {
            var list = new List<string>();
            foreach (var obj in _clbFormats.CheckedItems)
            {
                if (obj is FormatChoice fc) list.Add(fc.Token);
            }
            return string.Join(",", list);
        }

        private int GetDownscaleValue()
        {
            if (_cboDownscale.SelectedIndex <= 0) return 0;
            var label = _cboDownscale.SelectedItem.ToString();
            var space = label.IndexOf(' ');
            return space > 0 && int.TryParse(label.Substring(0, space), out var v) ? v : 0;
        }

        public void ClearContent()
        {
            UnsubscribeLiveStatus();
            StopPreview();

            _currentItemId = Guid.Empty;
            _selectedCameraItem = null;
            _txtName.Text = "";
            _btnSelectCamera.Text = "(Select camera...)";
            _chkEnabled.Checked = true;
            _chkTryHarder.Checked = true;
            _chkAutoRotate.Checked = true;
            _chkTryInverted.Checked = false;
            _chkCreateBookmarks.Checked = true;
            _numTargetFps.Value = ChannelConfig.DefaultTargetFps;
            _numDebounceMs.Value = ChannelConfig.DefaultDebounceMs;
            _cboDownscale.SelectedIndex = 0;
            SelectFormats(ChannelConfig.DefaultFormats);

            _lblStatusValue.Text = "";
            _lblStatsValue.Text = "";
            _lblHint.Text = "";
            _dgvLog.Rows.Clear();
        }

        #region Live Status

        internal void SubscribeLiveStatus(Guid itemId)
        {
            if (!IsHandleCreated)
            {
                _pendingSubscribeItemId = itemId;
                this.HandleCreated -= OnHandleCreated_Subscribe;
                this.HandleCreated += OnHandleCreated_Subscribe;
                return;
            }

            // Synchronous register/unregister on the UI thread keeps the life-cycle linear:
            // each Subscribe call fully tears down the previous subscription before creating
            // a new one. An earlier ThreadPool-based version raced when the user rapidly
            // switched channels, occasionally double-registering StatusUpdate and tripping
            // MessageBroker's "Same messageId being registered multiple times" error.
            // MessageCommunicationManager.Start was already pre-warmed in
            // BarcodeChannelItemManager.Init, so this is not UI-blocking.
            UnsubscribeLiveStatus();

            _subscribedItemId = itemId;
            _lblStatusValue.Text = "Waiting for status...";
            _lblStatusValue.ForeColor = SystemColors.ControlText;
            _lblStatsValue.Text = "";
            _lblHint.Text = "";
            _lastLogHash = 0;
            _dgvLog.Rows.Clear();

            try
            {
                var serverId = EnvironmentManager.Instance.MasterSite.ServerId;
                MessageCommunicationManager.Start(serverId);
                _mc = MessageCommunicationManager.Get(serverId);

                _statusUpdateFilter = _mc.RegisterCommunicationFilter(
                    OnStatusMessage, new CommunicationIdFilter(BarcodeMessageIds.StatusUpdate));
                _statusResponseFilter = _mc.RegisterCommunicationFilter(
                    OnStatusMessage, new CommunicationIdFilter(BarcodeMessageIds.StatusResponse));

                _responseTimer = new Timer { Interval = 3000 };
                _responseTimer.Tick += OnResponseTimeout;
                _responseTimer.Start();

                var request = new ChannelStatusRequest { ItemId = itemId };
                _mc.TransmitMessage(
                    new VideoOS.Platform.Messaging.Message(BarcodeMessageIds.StatusRequest, request),
                    null, null, null);
            }
            catch (Exception)
            {
                _lblStatusValue.Text = "Event Server not reachable";
                _lblStatusValue.ForeColor = SystemColors.GrayText;
            }
        }

        private void OnHandleCreated_Subscribe(object sender, EventArgs e)
        {
            this.HandleCreated -= OnHandleCreated_Subscribe;
            var pending = _pendingSubscribeItemId;
            _pendingSubscribeItemId = Guid.Empty;
            if (pending != Guid.Empty) SubscribeLiveStatus(pending);
        }

        internal void UnsubscribeLiveStatus()
        {
            _pendingSubscribeItemId = Guid.Empty;
            this.HandleCreated -= OnHandleCreated_Subscribe;
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
                if (_statusUpdateFilter != null) { _mc.UnRegisterCommunicationFilter(_statusUpdateFilter); _statusUpdateFilter = null; }
                if (_statusResponseFilter != null) { _mc.UnRegisterCommunicationFilter(_statusResponseFilter); _statusResponseFilter = null; }
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
            var update = message.Data as ChannelStatusUpdate;
            if (update == null || update.ItemId != _subscribedItemId) return null;

            if (InvokeRequired)
            {
                try { BeginInvoke(new Action(() => ApplyStatusUpdate(update))); }
                catch (ObjectDisposedException) { }
            }
            else ApplyStatusUpdate(update);
            return null;
        }

        private void ApplyStatusUpdate(ChannelStatusUpdate update)
        {
            if (IsDisposed || !IsHandleCreated) return;

            if (_responseTimer != null)
            {
                _responseTimer.Stop();
                _responseTimer.Tick -= OnResponseTimeout;
                _responseTimer.Dispose();
                _responseTimer = null;
            }

            _lblStatusValue.Text = update.Status ?? "Unknown";
            if (update.Status != null)
            {
                if (update.Status.StartsWith("Running"))      _lblStatusValue.ForeColor = ColorGood;
                else if (update.Status.StartsWith("Error"))   _lblStatusValue.ForeColor = ColorBad;
                else                                           _lblStatusValue.ForeColor = SystemColors.ControlText;
            }

            _lblStatsValue.Text = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "Cam FPS: {0:F1} | Decode FPS: {1:F1} | Inference avg/p95: {2:F1}/{3:F1} ms | Max FPS: {4:F1} | Restarts: {5}",
                update.CameraFps, update.DecodeFps, update.InfMsAvg, update.InfMsP95, update.MaxFps, update.RestartCount);

            UpdateHint(update);
            UpdateLogGrid(update);
        }

        private void UpdateHint(ChannelStatusUpdate u)
        {
            if (u.InfMsAvg <= 0 || u.MaxFps <= 0) { _lblHint.Text = ""; return; }

            var supply = u.CameraFps > 0 ? u.CameraFps : double.MaxValue;
            var ceiling = Math.Min(u.MaxFps, supply);
            var utilization = ceiling > 0 ? u.DecodeFps / ceiling : 0;

            if (utilization > 0.9)
            {
                _lblHint.Text = "At pipeline limit - lowering TargetFps, enabling downscale, or disabling TryHarder will reduce CPU.";
                _lblHint.ForeColor = ColorWarn;
            }
            else if (utilization > 0.7)
            {
                _lblHint.Text = "Running close to limit - small bumps to TargetFps may start dropping frames.";
                _lblHint.ForeColor = ColorWarn;
            }
            else
            {
                _lblHint.Text = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "Headroom available: you can raise TargetFps up to ~{0:F0} fps before hitting the inference/camera ceiling.",
                    ceiling);
                _lblHint.ForeColor = ColorGood;
            }
        }

        private void UpdateLogGrid(ChannelStatusUpdate update)
        {
            if (update.RecentLogLines == null || update.RecentLogLines.Count == 0) return;

            var logHash = update.RecentLogLines.Count;
            unchecked
            {
                for (int i = 0; i < update.RecentLogLines.Count; i++)
                    logHash = logHash * 31 + (update.RecentLogLines[i]?.GetHashCode() ?? 0);
            }
            if (logHash == _lastLogHash) return;
            _lastLogHash = logHash;

            // Helper buffers lines oldest-first in a ring. Flip so the most recent detection
            // is on top without scrolling - matches the operator's expectation when watching
            // barcodes fly by.
            _dgvLog.Rows.Clear();
            for (int i = update.RecentLogLines.Count - 1; i >= 0; i--)
                _dgvLog.Rows.Add(update.RecentLogLines[i]);
            if (_dgvLog.Rows.Count > 0)
                _dgvLog.FirstDisplayedScrollingRowIndex = 0;
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
                StartPreview(_selectedCameraItem);
                OnUserChange(sender, e);
            }
        }

        internal void OnUserChange(object sender, EventArgs e)
        {
            if (sender == _chkEnabled && _currentItemId != Guid.Empty)
            {
                if (_chkEnabled.Checked) SubscribeLiveStatus(_currentItemId);
                else                     ShowDisabledStatus();
            }
            ConfigurationChangedByUser?.Invoke(this, EventArgs.Empty);
        }

        private void ShowDisabledStatus()
        {
            UnsubscribeLiveStatus();
            _lblStatusValue.Text = "Disabled";
            _lblStatusValue.ForeColor = SystemColors.GrayText;
            _lblStatsValue.Text = "";
            _lblHint.Text = "";
            _dgvLog.Rows.Clear();
        }
    }
}
