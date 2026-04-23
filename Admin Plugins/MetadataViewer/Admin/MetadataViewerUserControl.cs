using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Xml.Linq;
using CommunitySDK;
using VideoOS.Platform;
using VideoOS.Platform.Live;
using VideoOS.Platform.UI;

namespace MetadataViewer.Admin
{
    public partial class MetadataViewerUserControl : UserControl
    {
        private const string PropMetadataId = "MetadataId";
        private const string PropMetadataName = "MetadataName";
        private const int MaxRows = 5000;

        private static readonly XNamespace NsTt   = "http://www.onvif.org/ver10/schema";
        private static readonly XNamespace NsWsnt = "http://docs.oasis-open.org/wsn/b-2";

        private static readonly PluginLog _log = new PluginLog("MetadataViewer");

        private Label _lblName;
        private TextBox _txtName;
        private Label _lblChannel;
        private Button _btnSelectChannel;
        private Button _btnStart;
        private Button _btnStop;
        private Button _btnClear;
        private Label _lblStatus;
        private Label _lblCounter;
        private Label _lblFilter;
        private TextBox _txtFilter;
        private Button _btnApplyFilter;
        private Button _btnClearFilter;
        private SplitContainer _split;
        private DataGridView _dgv;
        private RichTextBox _rtbPreview;

        private Item _selectedMetadataItem;
        private MetadataLiveSource _liveSource;
        private int _packetsReceived;
        private int _errorEvents;
        private int _statusEvents;
        private int _seqCounter;
        private bool _suppressUserChange;
        private string _currentFilter = "";
        private System.Windows.Forms.Timer _heartbeatTimer;
        private DateTime _subscribeStartUtc;

        private readonly List<MetaRow> _rows = new List<MetaRow>();

        internal event EventHandler ConfigurationChangedByUser;

        public MetadataViewerUserControl()
        {
            InitializeComponent();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                StopStream();
                StopHeartbeat();
            }
            base.Dispose(disposing);
        }

        public string DisplayName => _txtName.Text;

        private void InitializeComponent()
        {
            SuspendLayout();

            _lblName = new Label { Text = "Name:", Location = new Point(12, 15), AutoSize = true };
            _txtName = new TextBox { Location = new Point(110, 12), Width = 300 };
            _txtName.TextChanged += OnUserChange;

            _lblChannel = new Label { Text = "Metadata channel:", Location = new Point(12, 45), AutoSize = true };
            _btnSelectChannel = new Button
            {
                Text = "(Select metadata channel...)",
                Location = new Point(110, 42),
                Width = 300,
                TextAlign = ContentAlignment.MiddleLeft
            };
            _btnSelectChannel.Click += OnSelectChannelClick;

            _btnStart = new Button { Text = "Start", Location = new Point(420, 12), Width = 80 };
            _btnStart.Click += (s, e) => StartStream();

            _btnStop = new Button { Text = "Stop", Location = new Point(510, 12), Width = 80, Enabled = false };
            _btnStop.Click += (s, e) => StopStream();

            _btnClear = new Button { Text = "Clear", Location = new Point(510, 42), Width = 80 };
            _btnClear.Click += (s, e) => ClearRows();

            _lblStatus = new Label
            {
                Text = "Status: idle",
                Location = new Point(12, 75),
                AutoSize = true,
                ForeColor = Color.Gray
            };
            _lblCounter = new Label
            {
                Text = "",
                Location = new Point(200, 75),
                AutoSize = true,
                ForeColor = Color.Gray
            };

            _lblFilter = new Label { Text = "Filter:", Location = new Point(12, 105), AutoSize = true };
            _txtFilter = new TextBox { Location = new Point(60, 102), Width = 320 };
            _txtFilter.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter) { ApplyFilter(); e.SuppressKeyPress = true; }
            };
            _btnApplyFilter = new Button { Text = "Apply", Location = new Point(385, 100), Width = 70 };
            _btnApplyFilter.Click += (s, e) => ApplyFilter();
            _btnClearFilter = new Button { Text = "Clear", Location = new Point(460, 100), Width = 70 };
            _btnClearFilter.Click += (s, e) => { _txtFilter.Text = ""; ApplyFilter(); };

            _split = new SplitContainer
            {
                Orientation = Orientation.Horizontal,
                Location = new Point(12, 130),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
                Size = new Size(1180, 470),
                SplitterDistance = 260,
                SplitterWidth = 6,
                FixedPanel = FixedPanel.None
            };

            _dgv = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                ReadOnly = true,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                RowHeadersVisible = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
                AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize,
                AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle { BackColor = Color.FromArgb(245, 245, 245) }
            };
            _dgv.SelectionChanged += (s, e) => RenderPreviewFromSelection();
            _dgv.Columns.Add(new DataGridViewTextBoxColumn { Name = "Seq",     HeaderText = "Seq#",               Width = 60  });
            _dgv.Columns.Add(new DataGridViewTextBoxColumn { Name = "Capture", HeaderText = "Capture Time (UTC)", Width = 170 });
            _dgv.Columns.Add(new DataGridViewTextBoxColumn { Name = "Topic",   HeaderText = "Event Topic Tree",   Width = 380 });
            _dgv.Columns.Add(new DataGridViewTextBoxColumn { Name = "Data",    HeaderText = "Data",               Width = 220 });
            _dgv.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Info",
                HeaderText = "Info",
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                MinimumWidth = 200
            });

            _rtbPreview = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                WordWrap = false,
                Font = new Font("Consolas", 9f),
                BackColor = Color.White,
                DetectUrls = false,
                ScrollBars = RichTextBoxScrollBars.Both
            };

            _split.Panel1.Controls.Add(_dgv);
            _split.Panel2.Controls.Add(_rtbPreview);

            Controls.Add(_lblName);
            Controls.Add(_txtName);
            Controls.Add(_lblChannel);
            Controls.Add(_btnSelectChannel);
            Controls.Add(_btnStart);
            Controls.Add(_btnStop);
            Controls.Add(_btnClear);
            Controls.Add(_lblStatus);
            Controls.Add(_lblCounter);
            Controls.Add(_lblFilter);
            Controls.Add(_txtFilter);
            Controls.Add(_btnApplyFilter);
            Controls.Add(_btnClearFilter);
            Controls.Add(_split);

            Name = "MetadataViewerUserControl";
            Size = new Size(1200, 620);
            ResumeLayout(false);
            PerformLayout();
        }

        public void FillContent(Item item)
        {
            StopStream();

            if (item == null) { ClearContent(); return; }

            _suppressUserChange = true;
            try
            {
                _txtName.Text = item.Name ?? "";

                _selectedMetadataItem = null;
                if (item.Properties.ContainsKey(PropMetadataId)
                    && Guid.TryParse(item.Properties[PropMetadataId], out var metaId)
                    && metaId != Guid.Empty)
                {
                    try
                    {
                        _selectedMetadataItem = Configuration.Instance.GetItem(metaId, Kind.Metadata);
                    }
                    catch (Exception ex)
                    {
                        _log.Error($"GetItem failed for metadata {metaId}: {ex.Message}");
                    }

                    _btnSelectChannel.Text = _selectedMetadataItem != null
                        ? _selectedMetadataItem.Name
                        : (item.Properties.ContainsKey(PropMetadataName)
                            ? item.Properties[PropMetadataName] + " (not found)"
                            : "(Metadata channel not found)");
                }
                else
                {
                    _btnSelectChannel.Text = "(Select metadata channel...)";
                }
            }
            finally
            {
                _suppressUserChange = false;
            }

            UpdateButtons();
            SetStatus("idle", Color.Gray);
        }

        public void ClearContent()
        {
            StopStream();

            _suppressUserChange = true;
            try
            {
                _selectedMetadataItem = null;
                _txtName.Text = "";
                _btnSelectChannel.Text = "(Select metadata channel...)";
                ClearRows();
            }
            finally
            {
                _suppressUserChange = false;
            }

            UpdateButtons();
            SetStatus("idle", Color.Gray);
        }

        internal void StopStreamForRelease() => StopStream();

        public string ValidateInput()
        {
            if (string.IsNullOrWhiteSpace(_txtName.Text))
                return "Please enter a name.";
            return null;
        }

        public void UpdateItem(Item item)
        {
            if (item == null) return;
            item.Name = _txtName.Text;

            if (_selectedMetadataItem != null)
            {
                item.Properties[PropMetadataId] = _selectedMetadataItem.FQID.ObjectId.ToString();
                item.Properties[PropMetadataName] = _selectedMetadataItem.Name;
            }
            else
            {
                item.Properties[PropMetadataId] = Guid.Empty.ToString();
                item.Properties[PropMetadataName] = "";
            }
        }

        private void OnSelectChannelClick(object sender, EventArgs e)
        {
            var form = new ItemPickerWpfWindow
            {
                Items = Configuration.Instance.GetItemsByKind(Kind.Metadata),
                KindsFilter = new List<Guid> { Kind.Metadata },
                SelectionMode = SelectionModeOptions.AutoCloseOnSelect,
            };
            if (form.ShowDialog() == true && form.SelectedItems != null && form.SelectedItems.Any())
            {
                StopStream();
                _selectedMetadataItem = form.SelectedItems.First();
                _btnSelectChannel.Text = _selectedMetadataItem.Name;
                UpdateButtons();
                OnUserChange(sender, e);
            }
        }

        private void OnUserChange(object sender, EventArgs e)
        {
            if (_suppressUserChange) return;
            ConfigurationChangedByUser?.Invoke(this, EventArgs.Empty);
        }

        private void UpdateButtons()
        {
            bool hasChannel = _selectedMetadataItem != null;
            bool streaming = _liveSource != null;
            _btnStart.Enabled = hasChannel && !streaming;
            _btnStop.Enabled = streaming;
            _btnSelectChannel.Enabled = !streaming;
        }

        private void StartStream()
        {
            if (_selectedMetadataItem == null || _liveSource != null) return;

            try
            {
                _packetsReceived = 0;
                _errorEvents = 0;
                _statusEvents = 0;
                _subscribeStartUtc = DateTime.UtcNow;

                var item = _selectedMetadataItem;
                _log.Info($"StartStream: item='{item.Name}' env={EnvironmentManager.Instance.EnvironmentType} " +
                          $"kind={item.FQID?.Kind} objectId={item.FQID?.ObjectId} " +
                          $"serverId={item.FQID?.ServerId?.Id} parentId={item.FQID?.ParentId}");

                _liveSource = new MetadataLiveSource(item) { LiveModeStart = true };
                _liveSource.Init();
                _liveSource.LiveContentEvent += OnLiveContent;
                _liveSource.LiveStatusEvent += OnLiveStatus;
                _liveSource.ErrorEvent += OnLiveError;

                _log.Info("MetadataLiveSource Init() OK, handlers subscribed");
                SetStatus("streaming (waiting for first packet...)", Color.DarkGreen);
                StartHeartbeat();
            }
            catch (Exception ex)
            {
                _log.Error($"StartStream failed: {ex.Message}", ex);
                SetStatus("error: " + ex.Message, Color.DarkRed);
                CleanupSource();
            }

            UpdateButtons();
        }

        private void StartHeartbeat()
        {
            StopHeartbeat();
            _heartbeatTimer = new System.Windows.Forms.Timer { Interval = 5000 };
            _heartbeatTimer.Tick += OnHeartbeat;
            _heartbeatTimer.Start();
        }

        private void StopHeartbeat()
        {
            if (_heartbeatTimer != null)
            {
                _heartbeatTimer.Stop();
                _heartbeatTimer.Tick -= OnHeartbeat;
                _heartbeatTimer.Dispose();
                _heartbeatTimer = null;
            }
        }

        private void OnHeartbeat(object sender, EventArgs e)
        {
            if (_liveSource == null) { StopHeartbeat(); return; }
            var elapsed = (DateTime.UtcNow - _subscribeStartUtc).TotalSeconds;
            _log.Info($"[HEARTBEAT] +{elapsed:F0}s liveModeStart={_liveSource?.LiveModeStart} " +
                      $"packets={_packetsReceived} errors={_errorEvents} status={_statusEvents}");
        }

        private void StopStream()
        {
            StopHeartbeat();

            if (_liveSource == null)
            {
                UpdateButtons();
                return;
            }

            try
            {
                var elapsed = (DateTime.UtcNow - _subscribeStartUtc).TotalSeconds;
                _log.Info($"Stream stopped after {elapsed:F0}s — packets={_packetsReceived} " +
                          $"errors={_errorEvents} status={_statusEvents}");
                CleanupSource();
                SetStatus("idle", Color.Gray);
            }
            catch (Exception ex)
            {
                _log.Error($"StopStream failed: {ex.Message}", ex);
            }

            UpdateButtons();
        }

        private void CleanupSource()
        {
            if (_liveSource == null) return;
            try
            {
                _liveSource.LiveContentEvent -= OnLiveContent;
                _liveSource.LiveStatusEvent -= OnLiveStatus;
                _liveSource.ErrorEvent -= OnLiveError;
                _liveSource.Close();
            }
            catch { }
            _liveSource = null;
        }

        private void OnLiveContent(MetadataLiveSource source, MetadataLiveContent content)
        {
            try
            {
                _packetsReceived++;
                var receivedUtc = DateTime.UtcNow;

                string xml;
                try { xml = content?.Content?.GetMetadataString() ?? ""; }
                catch (Exception ex) { xml = "[GetMetadataString failed: " + ex.Message + "]"; }

                if (string.IsNullOrEmpty(xml)) return;

                var rows = ParseOnvifEvents(xml);
                if (rows.Count == 0)
                {
                    rows.Add(new MetaRow
                    {
                        Topic = "",
                        CaptureTimeUtc = null,
                        Data = xml,
                        Info = ""
                    });
                }

                // Fall back to receive time when ONVIF tt:Message/@UtcTime is missing
                // (the spec says it's mandatory, but some vendors omit it).
                foreach (var r in rows)
                    r.ReceivedUtc = receivedUtc;

                AppendRows(rows);
            }
            catch (Exception ex)
            {
                _log.Error($"OnLiveContent threw: {ex.Message}", ex);
            }
        }

        private void OnLiveStatus(MetadataLiveSource source, LiveStatusEventArgs args)
        {
            _statusEvents++;
            _log.Info($"LiveStatusEvent: current={args?.CurrentStatusFlags} changed={args?.ChangedStatusFlags}");
        }

        private void OnLiveError(MetadataLiveSource source, Exception ex)
        {
            _errorEvents++;
            var msg = ex != null ? $"{ex.GetType().Name}: {ex.Message}" : "(unknown error)";
            _log.Error($"LiveSource ErrorEvent: {msg}");
            SafeInvoke(() => SetStatusCore("error: " + msg, Color.DarkRed));
        }

        // ─────────── Parsing ───────────

        private static List<MetaRow> ParseOnvifEvents(string xml)
        {
            var rows = new List<MetaRow>();
            XDocument doc;
            try { doc = XDocument.Parse(xml); }
            catch { return rows; }

            foreach (var ev in doc.Descendants(NsTt + "Event"))
            {
                foreach (var nm in ev.Elements(NsWsnt + "NotificationMessage"))
                {
                    var topic = ((string)nm.Element(NsWsnt + "Topic") ?? "").Trim();
                    var msg = nm.Element(NsWsnt + "Message")?.Element(NsTt + "Message");
                    DateTime? captureUtc = null;
                    string info = "";

                    if (msg != null)
                    {
                        var utcAttr = (string)msg.Attribute("UtcTime");
                        if (utcAttr != null && DateTime.TryParse(utcAttr, CultureInfo.InvariantCulture,
                            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt))
                            captureUtc = dt;
                        info = BuildInfoFromMessage(msg);
                    }

                    rows.Add(new MetaRow
                    {
                        Topic = topic,
                        CaptureTimeUtc = captureUtc,
                        Data = nm.ToString(SaveOptions.DisableFormatting),
                        Info = info
                    });
                }
            }

            return rows;
        }

        private static string BuildInfoFromMessage(XElement message)
        {
            var parts = new List<string>();
            var source = message.Element(NsTt + "Source");
            if (source != null)
            {
                foreach (var si in source.Elements(NsTt + "SimpleItem"))
                {
                    var name = (string)si.Attribute("Name") ?? "";
                    var value = (string)si.Attribute("Value") ?? "";
                    if (!string.IsNullOrEmpty(name))
                        parts.Add($"[{name}={value}]");
                }
            }
            var data = message.Element(NsTt + "Data");
            if (data != null)
            {
                foreach (var si in data.Elements(NsTt + "SimpleItem"))
                {
                    var name = (string)si.Attribute("Name") ?? "";
                    var value = (string)si.Attribute("Value") ?? "";
                    if (!string.IsNullOrEmpty(name))
                        parts.Add($"{name}={value}");
                }
            }
            return string.Join("; ", parts);
        }

        // ─────────── Grid updates ───────────

        private void AppendRows(List<MetaRow> newRows)
        {
            SafeInvoke(() =>
            {
                foreach (var row in newRows)
                {
                    row.Seq = ++_seqCounter;
                    _rows.Add(row);
                    // Trim oldest from backing store + bottom of grid (oldest is at the bottom since we insert newest on top)
                    while (_rows.Count > MaxRows)
                    {
                        _rows.RemoveAt(0);
                        if (_dgv.Rows.Count > 0) _dgv.Rows.RemoveAt(_dgv.Rows.Count - 1);
                    }

                    if (PassesFilter(row))
                        InsertDgvRowAtTop(row);
                }

                _lblCounter.Text = $"Received: {_packetsReceived} packet(s) · {_rows.Count} row(s)";
                SetStatusCore($"streaming — {_packetsReceived} packet(s)", Color.DarkGreen);
            });
        }

        private void InsertDgvRowAtTop(MetaRow row)
        {
            // Prefer the ONVIF tt:Message/@UtcTime; fall back to our receive time
            // (plus a trailing "*" marker so the user can tell it wasn't on the wire).
            var stamp = row.CaptureTimeUtc ?? row.ReceivedUtc;
            var captureStr = stamp.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
            if (!row.CaptureTimeUtc.HasValue) captureStr += " *";

            _dgv.Rows.Insert(0, row.Seq, captureStr, row.Topic, TruncateForCell(row.Data), row.Info);
            _dgv.Rows[0].Tag = row;
        }

        private static string TruncateForCell(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            if (text.Length <= 400) return text;
            return text.Substring(0, 400) + "…";
        }

        private void ApplyFilter()
        {
            _currentFilter = (_txtFilter.Text ?? "").Trim();
            _dgv.SuspendLayout();
            _dgv.Rows.Clear();
            // Iterate oldest → newest and insert at top, so newest ends up on top
            foreach (var r in _rows)
            {
                if (PassesFilter(r)) InsertDgvRowAtTop(r);
            }
            _dgv.ResumeLayout();
            _lblCounter.Text = $"Received: {_packetsReceived} packet(s) · {_rows.Count} row(s) · {_dgv.Rows.Count} visible";
        }

        private bool PassesFilter(MetaRow r)
        {
            if (string.IsNullOrEmpty(_currentFilter)) return true;
            var needle = _currentFilter;
            return IndexOfIgnoreCase(r.Topic, needle) ||
                   IndexOfIgnoreCase(r.Info, needle) ||
                   IndexOfIgnoreCase(r.Data, needle);
        }

        private static bool IndexOfIgnoreCase(string haystack, string needle)
            => !string.IsNullOrEmpty(haystack) && haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;

        private void ClearRows()
        {
            _rows.Clear();
            _seqCounter = 0;
            _packetsReceived = 0;
            _dgv.Rows.Clear();
            _rtbPreview.Clear();
            _lblCounter.Text = "";
        }

        // ─────────── Preview pane ───────────

        private void RenderPreviewFromSelection()
        {
            if (_dgv.SelectedRows.Count == 0) { _rtbPreview.Clear(); return; }
            var row = _dgv.SelectedRows[0].Tag as MetaRow;
            if (row == null) { _rtbPreview.Clear(); return; }
            RenderHighlightedXml(row.Data);
        }

        private void RenderHighlightedXml(string rawXml)
        {
            _rtbPreview.SuspendLayout();
            try
            {
                _rtbPreview.Clear();

                string pretty;
                try { pretty = XDocument.Parse(rawXml).ToString(SaveOptions.None); }
                catch
                {
                    AppendColored(_rtbPreview, rawXml, Color.Black);
                    return;
                }

                int i = 0;
                while (i < pretty.Length)
                {
                    if (pretty[i] == '<')
                    {
                        int end = pretty.IndexOf('>', i);
                        if (end < 0)
                        {
                            AppendColored(_rtbPreview, pretty.Substring(i), Color.Black);
                            break;
                        }
                        HighlightTag(pretty.Substring(i, end - i + 1));
                        i = end + 1;
                    }
                    else
                    {
                        int next = pretty.IndexOf('<', i);
                        if (next < 0) { AppendColored(_rtbPreview, pretty.Substring(i), Color.Black); break; }
                        AppendColored(_rtbPreview, pretty.Substring(i, next - i), Color.Black);
                        i = next;
                    }
                }

                _rtbPreview.SelectionStart = 0;
                _rtbPreview.SelectionLength = 0;
            }
            finally
            {
                _rtbPreview.ResumeLayout();
            }
        }

        private void HighlightTag(string tag)
        {
            // Whole tag incl. '<' and '>'
            var gray   = Color.DimGray;   // punctuation < > /
            var navy   = Color.Navy;      // element name
            var attr   = Color.Firebrick; // attribute name
            var valCol = Color.SaddleBrown; // quoted value
            var text   = Color.Black;

            int pos = 0;
            int len = tag.Length;

            // Opening '<' (plus optional '/')
            AppendColored(_rtbPreview, "<", gray);
            pos++;
            if (pos < len - 1 && tag[pos] == '/')
            {
                AppendColored(_rtbPreview, "/", gray);
                pos++;
            }

            // Comment / PI / CDATA → just dump uncoloured
            if (pos < len - 1 && (tag[pos] == '!' || tag[pos] == '?'))
            {
                AppendColored(_rtbPreview, tag.Substring(pos, len - pos - 1), text);
                AppendColored(_rtbPreview, ">", gray);
                return;
            }

            // Element name
            int nameStart = pos;
            while (pos < len - 1 && !char.IsWhiteSpace(tag[pos]) && tag[pos] != '/' && tag[pos] != '>')
                pos++;
            if (pos > nameStart)
                AppendColored(_rtbPreview, tag.Substring(nameStart, pos - nameStart), navy);

            // Attributes / self-close slash
            while (pos < len - 1)
            {
                // whitespace
                int wsStart = pos;
                while (pos < len - 1 && char.IsWhiteSpace(tag[pos])) pos++;
                if (pos > wsStart)
                    AppendColored(_rtbPreview, tag.Substring(wsStart, pos - wsStart), text);
                if (pos >= len - 1) break;

                if (tag[pos] == '/')
                {
                    AppendColored(_rtbPreview, "/", gray);
                    pos++;
                    continue;
                }

                // Attribute name
                int attrStart = pos;
                while (pos < len - 1 && tag[pos] != '=' && !char.IsWhiteSpace(tag[pos]) && tag[pos] != '/' && tag[pos] != '>')
                    pos++;
                if (pos > attrStart)
                    AppendColored(_rtbPreview, tag.Substring(attrStart, pos - attrStart), attr);

                // '='
                if (pos < len - 1 && tag[pos] == '=')
                {
                    AppendColored(_rtbPreview, "=", text);
                    pos++;
                }

                // Quoted value
                if (pos < len - 1 && (tag[pos] == '"' || tag[pos] == '\''))
                {
                    char quote = tag[pos];
                    int valStart = pos;
                    pos++;
                    while (pos < len - 1 && tag[pos] != quote) pos++;
                    if (pos < len - 1) pos++; // include closing quote
                    AppendColored(_rtbPreview, tag.Substring(valStart, pos - valStart), valCol);
                }
            }

            // Closing '>'
            AppendColored(_rtbPreview, ">", gray);
        }

        private static void AppendColored(RichTextBox rtb, string text, Color color)
        {
            if (string.IsNullOrEmpty(text)) return;
            rtb.SelectionStart = rtb.TextLength;
            rtb.SelectionLength = 0;
            rtb.SelectionColor = color;
            rtb.AppendText(text);
            rtb.SelectionColor = rtb.ForeColor;
        }

        // ─────────── Marshalling helpers ───────────

        private bool SafeInvoke(Action action)
        {
            if (action == null) return false;
            if (IsDisposed || Disposing || !IsHandleCreated) return false;
            if (!InvokeRequired)
            {
                action();
                return true;
            }
            try
            {
                BeginInvoke(new Action(() =>
                {
                    if (IsDisposed || Disposing) return;
                    try { action(); }
                    catch (ObjectDisposedException) { }
                    catch (InvalidOperationException) { }
                }));
                return true;
            }
            catch (ObjectDisposedException) { return false; }
            catch (InvalidOperationException) { return false; }
        }

        private void SetStatus(string text, Color color) => SafeInvoke(() => SetStatusCore(text, color));

        private void SetStatusCore(string text, Color color)
        {
            _lblStatus.Text = "Status: " + text;
            _lblStatus.ForeColor = color;
        }

        // ─────────── Row DTO ───────────

        private class MetaRow
        {
            public int Seq;
            public string Topic;
            public DateTime? CaptureTimeUtc; // from ONVIF tt:Message/@UtcTime (nullable — spec-mandatory but not always present)
            public DateTime ReceivedUtc;     // set when the packet hit the plugin
            public string Data;
            public string Info;
        }
    }
}
