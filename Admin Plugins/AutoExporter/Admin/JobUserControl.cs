using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using AutoExporter.Background;
using AutoExporter.Messaging;
using CommunitySDK;
using VideoOS.Platform;
using VideoOS.Platform.Messaging;
using VideoOS.Platform.UI;

namespace AutoExporter.Admin
{
    public partial class JobUserControl : UserControl
    {
        private static readonly PluginLog _log = new PluginLog("AutoExporter.JobUI");
        internal event EventHandler ConfigurationChangedByUser;
        internal event EventHandler DuplicateRequested;

        private readonly CrossMessageHandler _cmh = new CrossMessageHandler(_log);
        private Guid _pendingVerifyCorrelationId = Guid.Empty;
        private bool _cmhStarted;

        public JobUserControl()
        {
            InitializeComponent();
            _cboRangeUnit.Items.AddRange(new object[] { "Minutes", "Hours", "Days", "Months" });
            _cboRangeUnit.SelectedItem = "Days";
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            try { _cmh.Close(); } catch { }
            _cmhStarted = false;
            base.OnHandleDestroyed(e);
        }

        public string DisplayName
        {
            get => _txtName.Text;
            set => _txtName.Text = value;
        }

        public void FillContent(Item item)
        {
            if (item == null) { ClearContent(); return; }

            _txtName.Text         = item.Name;
            _chkEnabled.Checked   = GetProp(item, "Enabled", "Yes") != "No";

            var format = GetProp(item, "Format", "XProtect");
            _radXProtect.Checked  = format == "XProtect";
            _radAvi.Checked       = format == "AVI";

            _chkEncrypt.Checked   = GetProp(item, "Encrypt", "No") == "Yes";
            _txtPassword.Text     = GetProp(item, "Password", "");
            _chkIncludePlayer.Checked = GetProp(item, "IncludePlayer", "Yes") != "No";
            _chkIncludeAudio.Checked  = GetProp(item, "IncludeAudio", "Yes") != "No";

            if (int.TryParse(GetProp(item, "RangeValue", "1"), out var rv) && rv >= (int)_numRangeValue.Minimum && rv <= (int)_numRangeValue.Maximum)
                _numRangeValue.Value = rv;
            else
                _numRangeValue.Value = 1;

            var unit = GetProp(item, "RangeUnit", "Days");
            if (_cboRangeUnit.Items.Contains(unit))
                _cboRangeUnit.SelectedItem = unit;
            else
                _cboRangeUnit.SelectedItem = "Days";

            _lstTargets.Items.Clear();
            foreach (var t in ReadTargets(item))
                _lstTargets.Items.Add(t);

            _txtStoragePath.Text = GetProp(item, "StoragePath", "");
            _numMaxGB.Value      = ClampDecimal(GetProp(item, "MaxGB", "0"), _numMaxGB.Minimum, _numMaxGB.Maximum);
            _numMaxAgeDays.Value = ClampDecimal(GetProp(item, "MaxAgeDays", "0"), _numMaxAgeDays.Minimum, _numMaxAgeDays.Maximum);

            UpdateFormatUi();
        }

        public void ClearContent()
        {
            _txtName.Text = "";
            _chkEnabled.Checked = true;
            _radXProtect.Checked = true;
            _radAvi.Checked = false;
            _chkEncrypt.Checked = false;
            _txtPassword.Text = "";
            _chkIncludePlayer.Checked = true;
            _chkIncludeAudio.Checked = true;
            _numRangeValue.Value = 1;
            _cboRangeUnit.SelectedItem = "Days";
            _lstTargets.Items.Clear();
            _txtStoragePath.Text = "";
            _numMaxGB.Value = 0;
            _numMaxAgeDays.Value = 0;
            UpdateFormatUi();
        }

        public string ValidateInput()
        {
            if (string.IsNullOrWhiteSpace(_txtName.Text))
                return "Please enter a name for the job.";

            if (_lstTargets.Items.Count == 0)
                return "Please add at least one camera or camera group.";

            if (_chkEncrypt.Checked && string.IsNullOrEmpty(_txtPassword.Text))
                return "Encryption is enabled — please enter a password.";

            if (_radAvi.Checked && _chkEncrypt.Checked)
                return "AVI format does not support encryption. Disable encryption or switch to XProtect format.";

            var path = _txtStoragePath.Text.Trim();
            if (string.IsNullOrEmpty(path))
                return "Please enter a storage path (local folder or UNC \\\\server\\share).";

            try
            {
                var full = Path.GetFullPath(path);
                if (full.Length < 3) return "Storage path looks invalid.";
            }
            catch (Exception ex)
            {
                return "Storage path is invalid: " + ex.Message;
            }

            return null;
        }

        public void UpdateItem(Item item)
        {
            if (item == null) return;

            item.Name = _txtName.Text.Trim();
            item.Properties["Enabled"]       = _chkEnabled.Checked ? "Yes" : "No";
            item.Properties["Format"]        = _radAvi.Checked ? "AVI" : "XProtect";
            item.Properties["Encrypt"]       = _chkEncrypt.Checked ? "Yes" : "No";
            item.Properties["Password"]      = _txtPassword.Text;
            item.Properties["IncludePlayer"] = _chkIncludePlayer.Checked ? "Yes" : "No";
            item.Properties["IncludeAudio"]  = _chkIncludeAudio.Checked ? "Yes" : "No";
            item.Properties["RangeValue"]    = ((int)_numRangeValue.Value).ToString();
            item.Properties["RangeUnit"]     = _cboRangeUnit.SelectedItem?.ToString() ?? "Days";

            item.Properties["StoragePath"]   = _txtStoragePath.Text.Trim();
            item.Properties["MaxGB"]         = ((long)_numMaxGB.Value).ToString();
            item.Properties["MaxAgeDays"]    = ((int)_numMaxAgeDays.Value).ToString();

            // Clear old targets
            var keysToRemove = item.Properties.Keys
                .Where(k => k.StartsWith("Targets_"))
                .ToList();
            foreach (var k in keysToRemove) item.Properties.Remove(k);

            int count = 0;
            foreach (var obj in _lstTargets.Items)
            {
                if (obj is JobTarget t)
                {
                    item.Properties[$"Targets_{count}_Kind"]     = t.Kind == JobTargetKind.Group ? "Group" : "Camera";
                    item.Properties[$"Targets_{count}_ObjectId"] = t.ObjectId.ToString();
                    item.Properties[$"Targets_{count}_Name"]     = t.Name ?? "";
                    count++;
                }
            }
            item.Properties["Targets_Count"] = count.ToString();
        }

        // ─── Targets parsing ────────────────────────────────

        internal static List<JobTarget> ReadTargets(Item item)
        {
            var result = new List<JobTarget>();
            if (item == null) return result;

            if (!item.Properties.ContainsKey("Targets_Count")) return result;
            if (!int.TryParse(item.Properties["Targets_Count"], out var count)) return result;

            for (int i = 0; i < count; i++)
            {
                var kindStr = item.Properties.ContainsKey($"Targets_{i}_Kind") ? item.Properties[$"Targets_{i}_Kind"] : null;
                var oidStr  = item.Properties.ContainsKey($"Targets_{i}_ObjectId") ? item.Properties[$"Targets_{i}_ObjectId"] : null;
                var name    = item.Properties.ContainsKey($"Targets_{i}_Name") ? item.Properties[$"Targets_{i}_Name"] : "";

                if (string.IsNullOrEmpty(kindStr) || !Guid.TryParse(oidStr, out var oid)) continue;

                result.Add(new JobTarget
                {
                    Kind = string.Equals(kindStr, "Group", StringComparison.OrdinalIgnoreCase) ? JobTargetKind.Group : JobTargetKind.Camera,
                    ObjectId = oid,
                    Name = name
                });
            }
            return result;
        }

        // ─── UI handlers ────────────────────────────────────

        private void OnAddTargetsClick(object sender, EventArgs e)
        {
            try
            {
                var picker = new ItemPickerWpfWindow
                {
                    KindsFilter = new List<Guid> { Kind.Camera },
                    SelectionMode = SelectionModeOptions.MultiSelect,
                    Items = Configuration.Instance.GetItems()
                };

                if (picker.ShowDialog() != true) return;

                foreach (var item in picker.SelectedItems)
                {
                    // Camera groups appear inside Kind.Camera as folder-type items
                    // (FolderType != No). Detect at add-time so the UI list can label them.
                    var isGroup = item.FQID != null && item.FQID.FolderType != FolderType.No;
                    var t = new JobTarget
                    {
                        Kind = isGroup ? JobTargetKind.Group : JobTargetKind.Camera,
                        ObjectId = item.FQID.ObjectId,
                        Name = item.Name
                    };
                    if (!_lstTargets.Items.Cast<object>().OfType<JobTarget>().Any(x => x.ObjectId == t.ObjectId))
                        _lstTargets.Items.Add(t);
                }
                OnUserChange(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to open picker: " + ex.Message, "Picker", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OnRemoveTargetClick(object sender, EventArgs e)
        {
            var selected = _lstTargets.SelectedItems.Cast<object>().ToList();
            foreach (var s in selected) _lstTargets.Items.Remove(s);
            if (selected.Count > 0) OnUserChange(sender, EventArgs.Empty);
        }

        private void OnFormatChanged(object sender, EventArgs e)
        {
            UpdateFormatUi();
            OnUserChange(sender, e);
        }

        private void OnEncryptChanged(object sender, EventArgs e)
        {
            _txtPassword.Enabled = _chkEncrypt.Checked && _radXProtect.Checked;
            OnUserChange(sender, e);
        }

        private void UpdateFormatUi()
        {
            bool xp = _radXProtect.Checked;
            _chkEncrypt.Enabled       = xp;
            _txtPassword.Enabled      = xp && _chkEncrypt.Checked;
            _chkIncludePlayer.Enabled = xp;
            if (!xp)
            {
                _chkEncrypt.Checked = false;
                _chkIncludePlayer.Checked = false;
            }
        }

        private void OnDuplicateClick(object sender, EventArgs e)
            => DuplicateRequested?.Invoke(this, EventArgs.Empty);

        private void OnBrowseStorageClick(object sender, EventArgs e)
        {
            using (var dlg = new FolderBrowserDialog
            {
                Description = "Pick a folder on THIS machine. The path is just a string — at run time the Event Server must be able to reach it. Use Verify to check.",
                ShowNewFolderButton = true
            })
            {
                if (!string.IsNullOrWhiteSpace(_txtStoragePath.Text) && Directory.Exists(_txtStoragePath.Text))
                    dlg.SelectedPath = _txtStoragePath.Text;

                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    _txtStoragePath.Text = dlg.SelectedPath;
                    OnUserChange(sender, EventArgs.Empty);
                }
            }
        }

        // ─── Verify (Event Server probe) ────────────────────

        internal void OnVerifyStorageClick(object sender, EventArgs e)
        {
            var path = _txtStoragePath.Text.Trim();
            if (string.IsNullOrEmpty(path))
            {
                ShowVerifyResult("Enter a storage path first.", Color.FromArgb(180, 90, 0));
                return;
            }

            if (!_cmhStarted)
            {
                if (!_cmh.Start())
                {
                    ShowVerifyResult("Cross-environment messaging unavailable — is the Event Server running?", Color.FromArgb(180, 30, 30));
                    return;
                }
                _cmh.Register(OnVerifyReply, new CommunicationIdFilter(AutoExporterMessageIds.StorageProbeReply));
                _cmhStarted = true;
            }

            var maxGB  = (long)_numMaxGB.Value;
            var maxAge = (int)_numMaxAgeDays.Value;
            var maxBytes = maxGB > 0 ? maxGB * 1024L * 1024L * 1024L : 0;

            _pendingVerifyCorrelationId = Guid.NewGuid();
            var correlationId = _pendingVerifyCorrelationId;

            try
            {
                _cmh.TransmitMessage(new VideoOS.Platform.Messaging.Message(
                    AutoExporterMessageIds.StorageProbeRequest,
                    new StorageProbeRequest
                    {
                        CorrelationId = correlationId,
                        JobObjectId   = Guid.Empty,
                        JobName       = _txtName.Text,
                        Path          = path,
                        MaxBytes      = maxBytes,
                        MaxAgeDays    = maxAge
                    }));

                ShowVerifyResult("Probing Event Server…", Color.DimGray);
            }
            catch (Exception ex)
            {
                _log.Error($"Verify send failed: {ex.Message}", ex);
                ShowVerifyResult("Send failed: " + ex.Message, Color.FromArgb(180, 30, 30));
                return;
            }

            // 6-second watchdog
            var watchdog = new System.Windows.Forms.Timer { Interval = 6500 };
            watchdog.Tick += (s, ev) =>
            {
                watchdog.Stop(); watchdog.Dispose();
                if (_pendingVerifyCorrelationId != correlationId) return;
                _pendingVerifyCorrelationId = Guid.Empty;
                ShowVerifyResult("Event Server did not reply (>6s).", Color.FromArgb(180, 30, 30));
            };
            watchdog.Start();
        }

        private object OnVerifyReply(VideoOS.Platform.Messaging.Message m, FQID dest, FQID sender)
        {
            try
            {
                if (!(m?.Data is StorageProbeReply reply)) return null;
                if (reply.CorrelationId != _pendingVerifyCorrelationId) return null;
                _pendingVerifyCorrelationId = Guid.Empty;

                BeginInvokeSafe(() => ShowVerifyReportInline(reply.Report));
            }
            catch (Exception ex)
            {
                _log.Error($"Verify reply error: {ex.Message}", ex);
            }
            return null;
        }

        private void ShowVerifyReportInline(StorageStatusReport r)
        {
            if (r == null) { ShowVerifyResult("Empty report.", Color.FromArgb(180, 30, 30)); return; }

            string detail;
            Color color;
            switch (r.Health)
            {
                case StorageHealth.Ok:
                case StorageHealth.QuotaWarn:
                    color = r.Health == StorageHealth.QuotaWarn ? Color.FromArgb(190, 130, 0) : Color.FromArgb(0, 130, 0);
                    detail = $"✓ Event Server can reach this path. " +
                             $"{r.RunFolderCount} run(s), usage {BytesToHuman(r.UsageBytes)}";
                    if (r.FreeBytes >= 0) detail += $", disk free {BytesToHuman(r.FreeBytes)}";
                    if (r.Health == StorageHealth.QuotaWarn) detail += " — near quota";
                    break;
                default:
                    color = Color.FromArgb(180, 30, 30);
                    detail = $"✗ {r.Health}: {r.Detail}";
                    break;
            }
            ShowVerifyResult(detail, color);
        }

        private void ShowVerifyResult(string text, Color color)
        {
            _lblVerifyStatus.ForeColor = color;
            _lblVerifyStatus.Text = text;
        }

        private static string BytesToHuman(long bytes)
        {
            if (bytes < 1024)            return $"{bytes} B";
            if (bytes < 1024L * 1024)    return $"{bytes / 1024d:0.0} KB";
            if (bytes < 1024L * 1024 * 1024)        return $"{bytes / (1024d * 1024):0.0} MB";
            if (bytes < 1024L * 1024 * 1024 * 1024) return $"{bytes / (1024d * 1024 * 1024):0.0} GB";
            return $"{bytes / (1024d * 1024 * 1024 * 1024):0.0} TB";
        }

        private void BeginInvokeSafe(Action a)
        {
            if (IsDisposed || !IsHandleCreated) return;
            try { BeginInvoke(a); }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
        }

        private void OnUserChange(object sender, EventArgs e)
            => ConfigurationChangedByUser?.Invoke(this, EventArgs.Empty);

        private static decimal ClampDecimal(string s, decimal min, decimal max)
        {
            if (!decimal.TryParse(s, out var v)) return min;
            if (v < min) return min;
            if (v > max) return max;
            return v;
        }

        private static string GetProp(Item item, string key, string defaultValue)
            => item.Properties.ContainsKey(key) ? item.Properties[key] : defaultValue;
    }

    internal enum JobTargetKind { Camera, Group }

    internal class JobTarget
    {
        public JobTargetKind Kind;
        public Guid ObjectId;
        public string Name;

        public override string ToString()
        {
            var prefix = Kind == JobTargetKind.Group ? "[Group] " : "[Camera] ";
            return prefix + (string.IsNullOrEmpty(Name) ? ObjectId.ToString() : Name);
        }
    }
}
