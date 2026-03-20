using CommunitySDK;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using VideoOS.Platform;
using VideoOS.Platform.ConfigurationItems;
using VideoOS.Platform.UI;

namespace Auditor.Admin
{
    public partial class AuditRuleUserControl : UserControl
    {
        private Label _lblName;
        private TextBox _txtName;
        private Label _lblUsers;
        private CheckedListBox _lstUsers;
        private Button _btnRefresh;
        private Label _lblLoading;
        private GroupBox _grpReasonPrompts;
        private CheckBox _chkPromptPlayback;
        private Label _lblPromptPlaybackDesc;
        private CheckBox _chkPromptExport;
        private Label _lblPromptExportDesc;
        private CheckBox _chkPromptIndependentPlayback;
        private Label _lblPromptIndependentPlaybackDesc;
        private GroupBox _grpEventTriggers;
        private CheckBox _chkTriggerPlayback;
        private Label _lblTriggerPlaybackDesc;
        private CheckBox _chkTriggerExport;
        private Label _lblTriggerExportDesc;
        private CheckBox _chkTriggerIndependentPlayback;
        private Label _lblTriggerIndependentPlaybackDesc;
        private CheckBox _chkEnabled;

        private List<string> _allUsers = new List<string>();
        private CheckBox _chkSpecifyCameras;
        private Label _lblSpecifyCamerasDesc;
        private GroupBox _grpCameras;
        private ListBox _lstCameras;
        private Button _btnAddCamera;
        private Button _btnRemoveCamera;
        private List<(Guid Id, string Name)> _selectedCameras = new List<(Guid, string)>();

        private GroupBox _grpPredefinedReasons;
        private ListBox _lstReasons;
        private Button _btnAddReason;
        private Button _btnRemoveReason;
        private List<(string Text, bool Playback, bool Export, bool IndependentPlayback)> _predefinedReasons = new List<(string, bool, bool, bool)>();

        private bool _filling;
        private readonly PluginLog _log = new PluginLog("Admin Auditor - AuditRule UC");

        internal event EventHandler ConfigurationChangedByUser;

        public AuditRuleUserControl()
        {
            InitializeComponent();
            SetupPredefinedReasonControls();
            SetupCameraControls();
            _txtName.TextChanged += OnUserChange;
            _btnRefresh.Click += OnBtnRefreshClick;
            _lstUsers.ItemCheck += OnLstUsersItemCheck;
            _chkPromptPlayback.CheckedChanged += OnUserChange;
            _chkPromptExport.CheckedChanged += OnUserChange;
            _chkPromptIndependentPlayback.CheckedChanged += OnUserChange;
            _chkTriggerPlayback.CheckedChanged += OnUserChange;
            _chkTriggerExport.CheckedChanged += OnUserChange;
            _chkTriggerIndependentPlayback.CheckedChanged += OnUserChange;
            _chkEnabled.CheckedChanged += OnUserChange;
            LoadUsersAsync();
        }

        internal string DisplayName
        {
            get { return _txtName.Text; }
            set { _txtName.Text = value; }
        }

        internal void OnUserChange(object sender, EventArgs e)
        {
            if (_filling) return;
            ConfigurationChangedByUser?.Invoke(this, EventArgs.Empty);
        }

        private void OnBtnRefreshClick(object sender, EventArgs e)
        {
            LoadUsersAsync();
        }

        private void OnLstUsersItemCheck(object sender, ItemCheckEventArgs e)
        {
            if (_filling) return;
            BeginInvoke(new Action(() => OnUserChange(sender, e)));
        }

        private void InitializeComponent()
        {
            this._lblName = new System.Windows.Forms.Label();
            this._txtName = new System.Windows.Forms.TextBox();
            this._lblUsers = new System.Windows.Forms.Label();
            this._lstUsers = new System.Windows.Forms.CheckedListBox();
            this._btnRefresh = new System.Windows.Forms.Button();
            this._lblLoading = new System.Windows.Forms.Label();
            this._grpReasonPrompts = new System.Windows.Forms.GroupBox();
            this._chkPromptPlayback = new System.Windows.Forms.CheckBox();
            this._lblPromptPlaybackDesc = new System.Windows.Forms.Label();
            this._chkPromptExport = new System.Windows.Forms.CheckBox();
            this._lblPromptExportDesc = new System.Windows.Forms.Label();
            this._chkPromptIndependentPlayback = new System.Windows.Forms.CheckBox();
            this._lblPromptIndependentPlaybackDesc = new System.Windows.Forms.Label();
            this._grpEventTriggers = new System.Windows.Forms.GroupBox();
            this._chkTriggerPlayback = new System.Windows.Forms.CheckBox();
            this._lblTriggerPlaybackDesc = new System.Windows.Forms.Label();
            this._chkTriggerExport = new System.Windows.Forms.CheckBox();
            this._lblTriggerExportDesc = new System.Windows.Forms.Label();
            this._chkTriggerIndependentPlayback = new System.Windows.Forms.CheckBox();
            this._lblTriggerIndependentPlaybackDesc = new System.Windows.Forms.Label();
            this._chkEnabled = new System.Windows.Forms.CheckBox();
            this._grpReasonPrompts.SuspendLayout();
            this._grpEventTriggers.SuspendLayout();
            this.SuspendLayout();
            // 
            // _lblName
            // 
            this._lblName.AutoSize = true;
            this._lblName.Location = new System.Drawing.Point(12, 15);
            this._lblName.Name = "_lblName";
            this._lblName.Size = new System.Drawing.Size(38, 13);
            this._lblName.TabIndex = 0;
            this._lblName.Text = "Name:";
            // 
            // _txtName
            // 
            this._txtName.Location = new System.Drawing.Point(15, 31);
            this._txtName.Name = "_txtName";
            this._txtName.Size = new System.Drawing.Size(458, 20);
            this._txtName.TabIndex = 1;
            // 
            // _lblUsers
            // 
            this._lblUsers.AutoSize = true;
            this._lblUsers.Location = new System.Drawing.Point(12, 60);
            this._lblUsers.Name = "_lblUsers";
            this._lblUsers.Size = new System.Drawing.Size(37, 13);
            this._lblUsers.TabIndex = 2;
            this._lblUsers.Text = "Users:";
            // 
            // _lstUsers
            // 
            this._lstUsers.CheckOnClick = true;
            this._lstUsers.Location = new System.Drawing.Point(15, 76);
            this._lstUsers.Name = "_lstUsers";
            this._lstUsers.Size = new System.Drawing.Size(458, 109);
            this._lstUsers.TabIndex = 3;
            // 
            // _btnRefresh
            // 
            this._btnRefresh.Location = new System.Drawing.Point(403, 191);
            this._btnRefresh.Name = "_btnRefresh";
            this._btnRefresh.Size = new System.Drawing.Size(70, 23);
            this._btnRefresh.TabIndex = 4;
            this._btnRefresh.Text = "Refresh";
            // 
            // _lblLoading
            // 
            this._lblLoading.AutoSize = true;
            this._lblLoading.Location = new System.Drawing.Point(391, 60);
            this._lblLoading.Name = "_lblLoading";
            this._lblLoading.Size = new System.Drawing.Size(82, 13);
            this._lblLoading.TabIndex = 5;
            this._lblLoading.Text = "Loading users...";
            this._lblLoading.Visible = false;
            // 
            // _grpReasonPrompts
            // 
            this._grpReasonPrompts.Controls.Add(this._chkPromptPlayback);
            this._grpReasonPrompts.Controls.Add(this._lblPromptPlaybackDesc);
            this._grpReasonPrompts.Controls.Add(this._chkPromptExport);
            this._grpReasonPrompts.Controls.Add(this._lblPromptExportDesc);
            this._grpReasonPrompts.Controls.Add(this._chkPromptIndependentPlayback);
            this._grpReasonPrompts.Controls.Add(this._lblPromptIndependentPlaybackDesc);
            this._grpReasonPrompts.Location = new System.Drawing.Point(15, 223);
            this._grpReasonPrompts.Name = "_grpReasonPrompts";
            this._grpReasonPrompts.Size = new System.Drawing.Size(458, 155);
            this._grpReasonPrompts.TabIndex = 4;
            this._grpReasonPrompts.TabStop = false;
            this._grpReasonPrompts.Text = "Reason Prompts (Audit Log)";
            // 
            // _chkPromptPlayback
            // 
            this._chkPromptPlayback.AutoSize = true;
            this._chkPromptPlayback.Checked = true;
            this._chkPromptPlayback.CheckState = System.Windows.Forms.CheckState.Checked;
            this._chkPromptPlayback.Location = new System.Drawing.Point(10, 22);
            this._chkPromptPlayback.Name = "_chkPromptPlayback";
            this._chkPromptPlayback.Size = new System.Drawing.Size(182, 17);
            this._chkPromptPlayback.TabIndex = 0;
            this._chkPromptPlayback.Text = "Enable Playback Reason Prompt";
            // 
            // _lblPromptPlaybackDesc
            // 
            this._lblPromptPlaybackDesc.AutoSize = true;
            this._lblPromptPlaybackDesc.ForeColor = System.Drawing.SystemColors.ControlDarkDark;
            this._lblPromptPlaybackDesc.Location = new System.Drawing.Point(27, 42);
            this._lblPromptPlaybackDesc.Name = "_lblPromptPlaybackDesc";
            this._lblPromptPlaybackDesc.Size = new System.Drawing.Size(366, 13);
            this._lblPromptPlaybackDesc.TabIndex = 1;
            this._lblPromptPlaybackDesc.Text = "Prompts user for a reason when entering playback mode. Writes to audit log.";
            // 
            // _chkPromptExport
            // 
            this._chkPromptExport.AutoSize = true;
            this._chkPromptExport.Checked = true;
            this._chkPromptExport.CheckState = System.Windows.Forms.CheckState.Checked;
            this._chkPromptExport.Location = new System.Drawing.Point(10, 65);
            this._chkPromptExport.Name = "_chkPromptExport";
            this._chkPromptExport.Size = new System.Drawing.Size(168, 17);
            this._chkPromptExport.TabIndex = 2;
            this._chkPromptExport.Text = "Enable Export Reason Prompt";
            // 
            // _lblPromptExportDesc
            // 
            this._lblPromptExportDesc.AutoSize = true;
            this._lblPromptExportDesc.ForeColor = System.Drawing.SystemColors.ControlDarkDark;
            this._lblPromptExportDesc.Location = new System.Drawing.Point(27, 85);
            this._lblPromptExportDesc.Name = "_lblPromptExportDesc";
            this._lblPromptExportDesc.Size = new System.Drawing.Size(352, 13);
            this._lblPromptExportDesc.TabIndex = 3;
            this._lblPromptExportDesc.Text = "Prompts user for a reason when entering export mode. Writes to audit log.";
            // 
            // _chkPromptIndependentPlayback
            // 
            this._chkPromptIndependentPlayback.AutoSize = true;
            this._chkPromptIndependentPlayback.Checked = true;
            this._chkPromptIndependentPlayback.CheckState = System.Windows.Forms.CheckState.Checked;
            this._chkPromptIndependentPlayback.Location = new System.Drawing.Point(10, 108);
            this._chkPromptIndependentPlayback.Name = "_chkPromptIndependentPlayback";
            this._chkPromptIndependentPlayback.Size = new System.Drawing.Size(245, 17);
            this._chkPromptIndependentPlayback.TabIndex = 4;
            this._chkPromptIndependentPlayback.Text = "Enable Independent Playback Reason Prompt";
            // 
            // _lblPromptIndependentPlaybackDesc
            // 
            this._lblPromptIndependentPlaybackDesc.AutoSize = true;
            this._lblPromptIndependentPlaybackDesc.ForeColor = System.Drawing.SystemColors.ControlDarkDark;
            this._lblPromptIndependentPlaybackDesc.Location = new System.Drawing.Point(27, 128);
            this._lblPromptIndependentPlaybackDesc.Name = "_lblPromptIndependentPlaybackDesc";
            this._lblPromptIndependentPlaybackDesc.Size = new System.Drawing.Size(401, 13);
            this._lblPromptIndependentPlaybackDesc.TabIndex = 5;
            this._lblPromptIndependentPlaybackDesc.Text = "Prompts user for a reason when enabling independent playback. Writes to audit log" +
    ".";
            // 
            // _grpEventTriggers
            // 
            this._grpEventTriggers.Controls.Add(this._chkTriggerPlayback);
            this._grpEventTriggers.Controls.Add(this._lblTriggerPlaybackDesc);
            this._grpEventTriggers.Controls.Add(this._chkTriggerExport);
            this._grpEventTriggers.Controls.Add(this._lblTriggerExportDesc);
            this._grpEventTriggers.Controls.Add(this._chkTriggerIndependentPlayback);
            this._grpEventTriggers.Controls.Add(this._lblTriggerIndependentPlaybackDesc);
            this._grpEventTriggers.Location = new System.Drawing.Point(15, 387);
            this._grpEventTriggers.Name = "_grpEventTriggers";
            this._grpEventTriggers.Size = new System.Drawing.Size(458, 155);
            this._grpEventTriggers.TabIndex = 5;
            this._grpEventTriggers.TabStop = false;
            this._grpEventTriggers.Text = "Event Triggers (Rule Events)";
            // 
            // _chkTriggerPlayback
            // 
            this._chkTriggerPlayback.AutoSize = true;
            this._chkTriggerPlayback.Checked = true;
            this._chkTriggerPlayback.CheckState = System.Windows.Forms.CheckState.Checked;
            this._chkTriggerPlayback.Location = new System.Drawing.Point(10, 22);
            this._chkTriggerPlayback.Name = "_chkTriggerPlayback";
            this._chkTriggerPlayback.Size = new System.Drawing.Size(173, 17);
            this._chkTriggerPlayback.TabIndex = 0;
            this._chkTriggerPlayback.Text = "Enable Playback Trigger Event";
            // 
            // _lblTriggerPlaybackDesc
            // 
            this._lblTriggerPlaybackDesc.AutoSize = true;
            this._lblTriggerPlaybackDesc.ForeColor = System.Drawing.SystemColors.ControlDarkDark;
            this._lblTriggerPlaybackDesc.Location = new System.Drawing.Point(27, 42);
            this._lblTriggerPlaybackDesc.Name = "_lblTriggerPlaybackDesc";
            this._lblTriggerPlaybackDesc.Size = new System.Drawing.Size(343, 13);
            this._lblTriggerPlaybackDesc.TabIndex = 1;
            this._lblTriggerPlaybackDesc.Text = "Fires an event when user enters playback mode. Combine it with Rules.";
            // 
            // _chkTriggerExport
            // 
            this._chkTriggerExport.AutoSize = true;
            this._chkTriggerExport.Checked = true;
            this._chkTriggerExport.CheckState = System.Windows.Forms.CheckState.Checked;
            this._chkTriggerExport.Location = new System.Drawing.Point(10, 65);
            this._chkTriggerExport.Name = "_chkTriggerExport";
            this._chkTriggerExport.Size = new System.Drawing.Size(159, 17);
            this._chkTriggerExport.TabIndex = 2;
            this._chkTriggerExport.Text = "Enable Export Trigger Event";
            // 
            // _lblTriggerExportDesc
            // 
            this._lblTriggerExportDesc.AutoSize = true;
            this._lblTriggerExportDesc.ForeColor = System.Drawing.SystemColors.ControlDarkDark;
            this._lblTriggerExportDesc.Location = new System.Drawing.Point(27, 85);
            this._lblTriggerExportDesc.Name = "_lblTriggerExportDesc";
            this._lblTriggerExportDesc.Size = new System.Drawing.Size(329, 13);
            this._lblTriggerExportDesc.TabIndex = 3;
            this._lblTriggerExportDesc.Text = "Fires an event when user enters export mode. Combine it with Rules.";
            // 
            // _chkTriggerIndependentPlayback
            // 
            this._chkTriggerIndependentPlayback.AutoSize = true;
            this._chkTriggerIndependentPlayback.Checked = true;
            this._chkTriggerIndependentPlayback.CheckState = System.Windows.Forms.CheckState.Checked;
            this._chkTriggerIndependentPlayback.Location = new System.Drawing.Point(10, 108);
            this._chkTriggerIndependentPlayback.Name = "_chkTriggerIndependentPlayback";
            this._chkTriggerIndependentPlayback.Size = new System.Drawing.Size(236, 17);
            this._chkTriggerIndependentPlayback.TabIndex = 4;
            this._chkTriggerIndependentPlayback.Text = "Enable Independent Playback Trigger Event";
            // 
            // _lblTriggerIndependentPlaybackDesc
            // 
            this._lblTriggerIndependentPlaybackDesc.AutoSize = true;
            this._lblTriggerIndependentPlaybackDesc.ForeColor = System.Drawing.SystemColors.ControlDarkDark;
            this._lblTriggerIndependentPlaybackDesc.Location = new System.Drawing.Point(27, 128);
            this._lblTriggerIndependentPlaybackDesc.Name = "_lblTriggerIndependentPlaybackDesc";
            this._lblTriggerIndependentPlaybackDesc.Size = new System.Drawing.Size(384, 13);
            this._lblTriggerIndependentPlaybackDesc.TabIndex = 5;
            this._lblTriggerIndependentPlaybackDesc.Text = "Fires an event when user enables independent playback. Combine it with Rules.";
            // 
            // _chkEnabled
            // 
            this._chkEnabled.AutoSize = true;
            this._chkEnabled.Checked = true;
            this._chkEnabled.CheckState = System.Windows.Forms.CheckState.Checked;
            this._chkEnabled.Location = new System.Drawing.Point(15, 553);
            this._chkEnabled.Name = "_chkEnabled";
            this._chkEnabled.Size = new System.Drawing.Size(65, 17);
            this._chkEnabled.TabIndex = 6;
            this._chkEnabled.Text = "Enabled";
            // 
            // AuditRuleUserControl
            // 
            this.Controls.Add(this._lblName);
            this.Controls.Add(this._txtName);
            this.Controls.Add(this._lblUsers);
            this.Controls.Add(this._lstUsers);
            this.Controls.Add(this._btnRefresh);
            this.Controls.Add(this._lblLoading);
            this.Controls.Add(this._grpReasonPrompts);
            this.Controls.Add(this._grpEventTriggers);
            this.Controls.Add(this._chkEnabled);
            this.Name = "AuditRuleUserControl";
            this.Size = new System.Drawing.Size(490, 585);
            this._grpReasonPrompts.ResumeLayout(false);
            this._grpReasonPrompts.PerformLayout();
            this._grpEventTriggers.ResumeLayout(false);
            this._grpEventTriggers.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        private void SetupCameraControls()
        {
            _grpCameras = new GroupBox
            {
                Location = new System.Drawing.Point(15, 220),
                Size = new System.Drawing.Size(458, 40),
                TabIndex = 10,
                TabStop = false,
                Text = "Camera Selection",
            };

            _chkSpecifyCameras = new CheckBox
            {
                AutoSize = true,
                Location = new System.Drawing.Point(10, 22),
                Text = "Specify Cameras",
                TabIndex = 0,
            };

            _lblSpecifyCamerasDesc = new Label
            {
                AutoSize = true,
                ForeColor = System.Drawing.SystemColors.ControlDarkDark,
                Location = new System.Drawing.Point(27, 42),
                Text = "When enabled, auditing only applies to the listed cameras.",
                TabIndex = 1,
            };

            _lstCameras = new ListBox
            {
                Location = new System.Drawing.Point(10, 62),
                Size = new System.Drawing.Size(438, 90),
                TabIndex = 2,
            };

            _btnAddCamera = new Button
            {
                Location = new System.Drawing.Point(10, 155),
                Size = new System.Drawing.Size(100, 23),
                Text = "Add Camera...",
                TabIndex = 3,
            };

            _btnRemoveCamera = new Button
            {
                Location = new System.Drawing.Point(116, 155),
                Size = new System.Drawing.Size(70, 23),
                Text = "Remove",
                TabIndex = 4,
            };

            _grpCameras.Controls.Add(_chkSpecifyCameras);
            _grpCameras.Controls.Add(_lblSpecifyCamerasDesc);
            _grpCameras.Controls.Add(_lstCameras);
            _grpCameras.Controls.Add(_btnAddCamera);
            _grpCameras.Controls.Add(_btnRemoveCamera);

            Controls.Add(_grpCameras);

            _chkSpecifyCameras.CheckedChanged += OnSpecifyCamerasChanged;
            _btnAddCamera.Click += OnBtnAddCameraClick;
            _btnRemoveCamera.Click += OnBtnRemoveCameraClick;

            UpdateCameraGroupLayout();
        }

        private void SetupPredefinedReasonControls()
        {
            _grpPredefinedReasons = new GroupBox
            {
                Location = new System.Drawing.Point(15, 400),
                Size = new System.Drawing.Size(458, 175),
                TabIndex = 11,
                TabStop = false,
                Text = "Predefined Reasons",
            };

            var lblDesc = new Label
            {
                AutoSize = true,
                ForeColor = System.Drawing.SystemColors.ControlDarkDark,
                Location = new System.Drawing.Point(7, 18),
                Text = "Define reasons shown as dropdown options in the Smart Client prompt.",
            };

            _lstReasons = new ListBox
            {
                Location = new System.Drawing.Point(10, 36),
                Size = new System.Drawing.Size(438, 95),
            };

            _btnAddReason = new Button
            {
                Location = new System.Drawing.Point(10, 135),
                Size = new System.Drawing.Size(70, 23),
                Text = "Add...",
            };

            _btnRemoveReason = new Button
            {
                Location = new System.Drawing.Point(86, 135),
                Size = new System.Drawing.Size(70, 23),
                Text = "Remove",
            };

            _grpPredefinedReasons.Controls.Add(lblDesc);
            _grpPredefinedReasons.Controls.Add(_lstReasons);
            _grpPredefinedReasons.Controls.Add(_btnAddReason);
            _grpPredefinedReasons.Controls.Add(_btnRemoveReason);
            Controls.Add(_grpPredefinedReasons);

            _btnAddReason.Click += OnBtnAddReasonClick;
            _btnRemoveReason.Click += OnBtnRemoveReasonClick;
        }

        private void OnBtnAddReasonClick(object sender, EventArgs e)
        {
            using (var dlg = new Form())
            {
                dlg.Text = "Add Predefined Reason";
                dlg.Size = new System.Drawing.Size(400, 240);
                dlg.FormBorderStyle = FormBorderStyle.FixedDialog;
                dlg.StartPosition = FormStartPosition.CenterParent;
                dlg.MaximizeBox = false;
                dlg.MinimizeBox = false;

                var lblText = new Label { Text = "Reason:", Location = new System.Drawing.Point(12, 15), AutoSize = true };
                var txtReason = new TextBox { Location = new System.Drawing.Point(12, 35), Size = new System.Drawing.Size(360, 20) };
                var lblApplies = new Label { Text = "Show for:", Location = new System.Drawing.Point(12, 65), AutoSize = true };
                var chkPlayback = new CheckBox { Text = "Playback", Location = new System.Drawing.Point(12, 85), AutoSize = true, Checked = true };
                var chkExport = new CheckBox { Text = "Export", Location = new System.Drawing.Point(12, 108), AutoSize = true, Checked = true };
                var chkIndependent = new CheckBox { Text = "Independent Playback", Location = new System.Drawing.Point(12, 131), AutoSize = true, Checked = true };

                var btnOk = new Button { Text = "OK", DialogResult = DialogResult.OK, Location = new System.Drawing.Point(216, 165), Size = new System.Drawing.Size(75, 23) };
                var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new System.Drawing.Point(297, 165), Size = new System.Drawing.Size(75, 23) };

                dlg.AcceptButton = btnOk;
                dlg.CancelButton = btnCancel;
                dlg.Controls.AddRange(new Control[] { lblText, txtReason, lblApplies, chkPlayback, chkExport, chkIndependent, btnOk, btnCancel });

                if (dlg.ShowDialog() == DialogResult.OK && !string.IsNullOrWhiteSpace(txtReason.Text))
                {
                    _predefinedReasons.Add((txtReason.Text.Trim(), chkPlayback.Checked, chkExport.Checked, chkIndependent.Checked));
                    RefreshReasonsList();
                    OnUserChange(sender, e);
                }
            }
        }

        private void OnBtnRemoveReasonClick(object sender, EventArgs e)
        {
            var idx = _lstReasons.SelectedIndex;
            if (idx >= 0)
            {
                _predefinedReasons.RemoveAt(idx);
                RefreshReasonsList();
                OnUserChange(sender, e);
            }
        }

        private void RefreshReasonsList()
        {
            _lstReasons.Items.Clear();
            foreach (var r in _predefinedReasons)
            {
                var flags = new List<string>();
                if (r.Playback) flags.Add("Playback");
                if (r.Export) flags.Add("Export");
                if (r.IndependentPlayback) flags.Add("Ind. Playback");
                _lstReasons.Items.Add($"{r.Text}  [{string.Join(", ", flags)}]");
            }
        }

        private string SerializePredefinedReasons()
        {
            var parts = new List<string>();
            foreach (var r in _predefinedReasons)
            {
                var flags = new List<string>();
                if (r.Playback) flags.Add("P");
                if (r.Export) flags.Add("E");
                if (r.IndependentPlayback) flags.Add("I");
                parts.Add($"{r.Text}|{string.Join(",", flags)}");
            }
            return string.Join(";", parts);
        }

        private void DeserializePredefinedReasons(string data)
        {
            _predefinedReasons.Clear();
            if (string.IsNullOrEmpty(data)) return;
            foreach (var entry in data.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var sep = entry.LastIndexOf('|');
                if (sep < 0) continue;
                var text = entry.Substring(0, sep);
                var flags = entry.Substring(sep + 1);
                _predefinedReasons.Add((text, flags.Contains("P"), flags.Contains("E"), flags.Contains("I")));
            }
        }

        private void UpdateCameraGroupLayout()
        {
            bool expanded = _chkSpecifyCameras.Checked;
            _lblSpecifyCamerasDesc.Visible = expanded;
            _lstCameras.Visible = expanded;
            _btnAddCamera.Visible = expanded;
            _btnRemoveCamera.Visible = expanded;
            _grpCameras.Height = expanded ? 185 : 45;

            _grpReasonPrompts.Top = _grpCameras.Bottom + 9;
            _grpPredefinedReasons.Top = _grpReasonPrompts.Bottom + 9;
            _grpEventTriggers.Top = _grpPredefinedReasons.Bottom + 9;
            _chkEnabled.Top = _grpEventTriggers.Bottom + 11;
            this.Height = _chkEnabled.Bottom + 15;
        }

        private void OnSpecifyCamerasChanged(object sender, EventArgs e)
        {
            UpdateCameraGroupLayout();
            if (!_filling)
                ConfigurationChangedByUser?.Invoke(this, EventArgs.Empty);
        }

        private void OnBtnAddCameraClick(object sender, EventArgs e)
        {
            var form = new ItemPickerWpfWindow
            {
                Items = Configuration.Instance.GetItemsByKind(Kind.Camera),
                KindsFilter = new List<Guid> { Kind.Camera },
                SelectionMode = SelectionModeOptions.AutoCloseOnSelect,
            };

            if (form.ShowDialog() == true && form.SelectedItems != null && form.SelectedItems.Any())
            {
                var selected = form.SelectedItems.First();
                var id = selected.FQID.ObjectId;
                if (!_selectedCameras.Any(c => c.Id == id))
                {
                    _selectedCameras.Add((id, selected.Name));
                    _lstCameras.Items.Add(selected.Name);
                    OnUserChange(sender, e);
                }
            }
        }

        private void OnBtnRemoveCameraClick(object sender, EventArgs e)
        {
            var idx = _lstCameras.SelectedIndex;
            if (idx >= 0)
            {
                _selectedCameras.RemoveAt(idx);
                _lstCameras.Items.RemoveAt(idx);
                OnUserChange(sender, e);
            }
        }

        private void LoadUsersAsync()
        {
            _btnRefresh.Enabled = false;
            _lblLoading.Visible = true;

            // Save current selection before clearing
            var currentSelection = GetSelectedUserNames();

            ThreadPool.QueueUserWorkItem(_ =>
            {
                var users = new List<string>();
                try
                {
                    _log.Info("Loading users...");
                    var management = new ManagementServer(EnvironmentManager.Instance.MasterSite);

                    // Load basic (local) users
                    try
                    {
                        foreach (var basicUser in management.BasicUserFolder.BasicUsers)
                        {
                            if (!string.IsNullOrEmpty(basicUser.Name) && !users.Contains(basicUser.Name))
                            {
                                users.Add(basicUser.Name);
                                _log.Info($"Found basic user: {basicUser.Name}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _log.Error($"Error reading basic users: {ex.Message}");
                    }

                    // Load users from roles (includes Windows/AD users)
                    try
                    {
                        foreach (var role in management.RoleFolder.Roles)
                        {
                            _log.Info($"Scanning role: {role.Name}");
                            try
                            {
                                foreach (var user in role.UserFolder.Users)
                                {
                                    if (!string.IsNullOrEmpty(user.Name) && !users.Contains(user.Name))
                                    {
                                        users.Add(user.Name);
                                        _log.Info($"Found role user: {user.Name} (role: {role.Name})");
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                _log.Error($"Error reading users from role {role.Name}: {ex.Message}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _log.Error($"Error reading roles: {ex.Message}");
                    }
                    users.Sort(StringComparer.OrdinalIgnoreCase);
                    _log.Info($"Loaded {users.Count} user(s)");
                }
                catch (Exception ex)
                {
                    _log.Error($"Failed to load users: {ex.Message}");
                }

                BeginInvoke(new Action(() =>
                {
                    _allUsers = users;
                    _lstUsers.Items.Clear();
                    foreach (var user in users)
                    {
                        var isChecked = currentSelection.Contains(user);
                        _lstUsers.Items.Add(user, isChecked);
                    }
                    _btnRefresh.Enabled = true;
                    _lblLoading.Visible = false;
                }));
            });
        }

        private List<string> GetSelectedUserNames()
        {
            var selected = new List<string>();
            for (int i = 0; i < _lstUsers.Items.Count; i++)
            {
                if (_lstUsers.GetItemChecked(i))
                    selected.Add(_lstUsers.Items[i].ToString());
            }
            return selected;
        }

        public void FillContent(Item item)
        {
            if (item == null) return;
            _filling = true;
            try
            {
                _txtName.Text = item.Name;

                var userNames = item.Properties.ContainsKey("UserNames")
                    ? item.Properties["UserNames"]
                    : (item.Properties.ContainsKey("UserName") ? item.Properties["UserName"] : "");

                var selectedUsers = userNames.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(u => u.Trim())
                    .ToList();

                for (int i = 0; i < _lstUsers.Items.Count; i++)
                {
                    var name = _lstUsers.Items[i].ToString();
                    _lstUsers.SetItemChecked(i, selectedUsers.Any(u =>
                        string.Equals(u, name, StringComparison.OrdinalIgnoreCase)));
                }

                foreach (var user in selectedUsers)
                {
                    if (!string.IsNullOrEmpty(user) &&
                        !_allUsers.Any(u => string.Equals(u, user, StringComparison.OrdinalIgnoreCase)))
                    {
                        _lstUsers.Items.Add(user, true);
                    }
                }

                _chkPromptPlayback.Checked = !item.Properties.ContainsKey("PromptPlayback") || item.Properties["PromptPlayback"] == "Yes";
                _chkPromptExport.Checked = !item.Properties.ContainsKey("PromptExport") || item.Properties["PromptExport"] == "Yes";
                _chkPromptIndependentPlayback.Checked = !item.Properties.ContainsKey("PromptIndependentPlayback") || item.Properties["PromptIndependentPlayback"] == "Yes";
                _chkTriggerPlayback.Checked = !item.Properties.ContainsKey("TriggerPlayback") || item.Properties["TriggerPlayback"] == "Yes";
                _chkTriggerExport.Checked = !item.Properties.ContainsKey("TriggerExport") || item.Properties["TriggerExport"] == "Yes";
                _chkTriggerIndependentPlayback.Checked = !item.Properties.ContainsKey("TriggerIndependentPlayback") || item.Properties["TriggerIndependentPlayback"] == "Yes";
                _chkEnabled.Checked = !item.Properties.ContainsKey("Enabled") || item.Properties["Enabled"] == "Yes";

                // Camera selection
                _chkSpecifyCameras.Checked = item.Properties.ContainsKey("SpecifyCameras") && item.Properties["SpecifyCameras"] == "Yes";

                _selectedCameras.Clear();
                _lstCameras.Items.Clear();
                var cameraIds = item.Properties.ContainsKey("CameraIds") ? item.Properties["CameraIds"] : "";
                var cameraNames = item.Properties.ContainsKey("CameraNames") ? item.Properties["CameraNames"] : "";
                var ids = cameraIds.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
                var names = cameraNames.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < ids.Length; i++)
                {
                    if (Guid.TryParse(ids[i].Trim(), out var camId))
                    {
                        var camName = i < names.Length ? names[i].Trim() : "(unknown)";
                        try
                        {
                            var camItem = Configuration.Instance.GetItem(camId, Kind.Camera);
                            if (camItem != null) camName = camItem.Name;
                        }
                        catch { }
                        _selectedCameras.Add((camId, camName));
                        _lstCameras.Items.Add(camName);
                    }
                }
                var reasonsData = item.Properties.ContainsKey("PredefinedReasons") ? item.Properties["PredefinedReasons"] : "";
                DeserializePredefinedReasons(reasonsData);
                RefreshReasonsList();

                UpdateCameraGroupLayout();
            }
            finally
            {
                _filling = false;
            }
        }

        public void ClearContent()
        {
            _filling = true;
            try
            {
                _txtName.Text = "";
                for (int i = 0; i < _lstUsers.Items.Count; i++)
                    _lstUsers.SetItemChecked(i, false);

                _chkPromptPlayback.Checked = true;
                _chkPromptExport.Checked = true;
                _chkPromptIndependentPlayback.Checked = true;
                _chkTriggerPlayback.Checked = true;
                _chkTriggerExport.Checked = true;
                _chkTriggerIndependentPlayback.Checked = true;
                _chkEnabled.Checked = true;

                _chkSpecifyCameras.Checked = false;
                _selectedCameras.Clear();
                _lstCameras.Items.Clear();
                _predefinedReasons.Clear();
                _lstReasons.Items.Clear();
                UpdateCameraGroupLayout();
            }
            finally
            {
                _filling = false;
            }
        }

        public string ValidateInput()
        {
            if (GetSelectedUserNames().Count == 0)
                return "At least one user must be selected.";
            return null;
        }

        public void UpdateItem(Item item)
        {
            if (item == null) return;
            item.Name = _txtName.Text;
            var selected = GetSelectedUserNames();
            item.Properties["UserNames"] = string.Join(";", selected);
            item.Properties["PromptPlayback"] = _chkPromptPlayback.Checked ? "Yes" : "No";
            item.Properties["PromptExport"] = _chkPromptExport.Checked ? "Yes" : "No";
            item.Properties["PromptIndependentPlayback"] = _chkPromptIndependentPlayback.Checked ? "Yes" : "No";
            item.Properties["TriggerPlayback"] = _chkTriggerPlayback.Checked ? "Yes" : "No";
            item.Properties["TriggerExport"] = _chkTriggerExport.Checked ? "Yes" : "No";
            item.Properties["TriggerIndependentPlayback"] = _chkTriggerIndependentPlayback.Checked ? "Yes" : "No";
            item.Properties["Enabled"] = _chkEnabled.Checked ? "Yes" : "No";

            // Auto-disable camera filter when list is empty
            var specifyCameras = _chkSpecifyCameras.Checked && _selectedCameras.Count > 0;
            if (_chkSpecifyCameras.Checked && _selectedCameras.Count == 0)
            {
                _filling = true;
                _chkSpecifyCameras.Checked = false;
                UpdateCameraGroupLayout();
                _filling = false;
            }
            item.Properties["SpecifyCameras"] = specifyCameras ? "Yes" : "No";
            item.Properties["CameraIds"] = string.Join(";", _selectedCameras.Select(c => c.Id.ToString()));
            item.Properties["CameraNames"] = string.Join(";", _selectedCameras.Select(c => c.Name));
            item.Properties["PredefinedReasons"] = SerializePredefinedReasons();
        }
    }
}
