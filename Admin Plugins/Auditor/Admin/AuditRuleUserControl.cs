using CommunitySDK;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using VideoOS.Platform;
using VideoOS.Platform.ConfigurationItems;

namespace Auditor.Admin
{
    public partial class AuditRuleUserControl : UserControl
    {
        private Label _lblUsers;
        private CheckedListBox _lstUsers;
        private Button _btnRefresh;
        private Label _lblLoading;
        private CheckBox _chkAuditPlayback;
        private CheckBox _chkAuditExport;
        private CheckBox _chkAuditIndependentPlayback;
        private CheckBox _chkEnabled;

        private List<string> _allUsers = new List<string>();
        private Label label1;
        private Label label2;
        private Label label3;
        private readonly PluginLog _log = new PluginLog("Admin Auditor - AuditRule UC");

        internal event EventHandler ConfigurationChangedByUser;

        public AuditRuleUserControl()
        {
            InitializeComponent();
            _btnRefresh.Click += OnBtnRefreshClick;
            _lstUsers.ItemCheck += OnLstUsersItemCheck;
            _chkAuditPlayback.CheckedChanged += OnUserChange;
            _chkAuditExport.CheckedChanged += OnUserChange;
            _chkAuditIndependentPlayback.CheckedChanged += OnUserChange;
            _chkEnabled.CheckedChanged += OnUserChange;
            LoadUsersAsync();
        }

        internal void OnUserChange(object sender, EventArgs e)
        {
            ConfigurationChangedByUser?.Invoke(this, EventArgs.Empty);
        }

        private void OnBtnRefreshClick(object sender, EventArgs e)
        {
            LoadUsersAsync();
        }

        private void OnLstUsersItemCheck(object sender, ItemCheckEventArgs e)
        {
            BeginInvoke(new Action(() => OnUserChange(sender, e)));
        }

        private void InitializeComponent()
        {
            this._lblUsers = new System.Windows.Forms.Label();
            this._lstUsers = new System.Windows.Forms.CheckedListBox();
            this._btnRefresh = new System.Windows.Forms.Button();
            this._lblLoading = new System.Windows.Forms.Label();
            this._chkAuditPlayback = new System.Windows.Forms.CheckBox();
            this._chkAuditExport = new System.Windows.Forms.CheckBox();
            this._chkAuditIndependentPlayback = new System.Windows.Forms.CheckBox();
            this._chkEnabled = new System.Windows.Forms.CheckBox();
            this.label1 = new System.Windows.Forms.Label();
            this.label2 = new System.Windows.Forms.Label();
            this.label3 = new System.Windows.Forms.Label();
            this.SuspendLayout();
            // 
            // _lblUsers
            // 
            this._lblUsers.AutoSize = true;
            this._lblUsers.Location = new System.Drawing.Point(12, 15);
            this._lblUsers.Name = "_lblUsers";
            this._lblUsers.Size = new System.Drawing.Size(37, 13);
            this._lblUsers.TabIndex = 0;
            this._lblUsers.Text = "Users:";
            // 
            // _lstUsers
            // 
            this._lstUsers.CheckOnClick = true;
            this._lstUsers.Location = new System.Drawing.Point(15, 31);
            this._lstUsers.Name = "_lstUsers";
            this._lstUsers.Size = new System.Drawing.Size(458, 109);
            this._lstUsers.TabIndex = 1;
            // 
            // _btnRefresh
            // 
            this._btnRefresh.Location = new System.Drawing.Point(302, 146);
            this._btnRefresh.Name = "_btnRefresh";
            this._btnRefresh.Size = new System.Drawing.Size(70, 23);
            this._btnRefresh.TabIndex = 2;
            this._btnRefresh.Text = "Refresh";
            // 
            // _lblLoading
            // 
            this._lblLoading.AutoSize = true;
            this._lblLoading.Location = new System.Drawing.Point(290, 15);
            this._lblLoading.Name = "_lblLoading";
            this._lblLoading.Size = new System.Drawing.Size(82, 13);
            this._lblLoading.TabIndex = 3;
            this._lblLoading.Text = "Loading users...";
            this._lblLoading.Visible = false;
            // 
            // _chkAuditPlayback
            // 
            this._chkAuditPlayback.AutoSize = true;
            this._chkAuditPlayback.Checked = true;
            this._chkAuditPlayback.CheckState = System.Windows.Forms.CheckState.Checked;
            this._chkAuditPlayback.Location = new System.Drawing.Point(15, 181);
            this._chkAuditPlayback.Name = "_chkAuditPlayback";
            this._chkAuditPlayback.Size = new System.Drawing.Size(97, 17);
            this._chkAuditPlayback.TabIndex = 4;
            this._chkAuditPlayback.Text = "Audit Playback";
            // 
            // _chkAuditExport
            // 
            this._chkAuditExport.AutoSize = true;
            this._chkAuditExport.Checked = true;
            this._chkAuditExport.CheckState = System.Windows.Forms.CheckState.Checked;
            this._chkAuditExport.Location = new System.Drawing.Point(15, 228);
            this._chkAuditExport.Name = "_chkAuditExport";
            this._chkAuditExport.Size = new System.Drawing.Size(83, 17);
            this._chkAuditExport.TabIndex = 5;
            this._chkAuditExport.Text = "Audit Export";
            // 
            // _chkAuditIndependentPlayback
            // 
            this._chkAuditIndependentPlayback.AutoSize = true;
            this._chkAuditIndependentPlayback.Checked = true;
            this._chkAuditIndependentPlayback.CheckState = System.Windows.Forms.CheckState.Checked;
            this._chkAuditIndependentPlayback.Location = new System.Drawing.Point(15, 279);
            this._chkAuditIndependentPlayback.Name = "_chkAuditIndependentPlayback";
            this._chkAuditIndependentPlayback.Size = new System.Drawing.Size(160, 17);
            this._chkAuditIndependentPlayback.TabIndex = 6;
            this._chkAuditIndependentPlayback.Text = "Audit Independent Playback";
            // 
            // _chkEnabled
            // 
            this._chkEnabled.AutoSize = true;
            this._chkEnabled.Checked = true;
            this._chkEnabled.CheckState = System.Windows.Forms.CheckState.Checked;
            this._chkEnabled.Location = new System.Drawing.Point(15, 336);
            this._chkEnabled.Name = "_chkEnabled";
            this._chkEnabled.Size = new System.Drawing.Size(65, 17);
            this._chkEnabled.TabIndex = 7;
            this._chkEnabled.Text = "Enabled";
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.ForeColor = System.Drawing.SystemColors.ControlDarkDark;
            this.label1.Location = new System.Drawing.Point(14, 201);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(459, 13);
            this.label1.TabIndex = 8;
            this.label1.Text = "Shows a message prompt where user needs to enter the reason for entering the play" +
    "back mode.";
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.ForeColor = System.Drawing.SystemColors.ControlDarkDark;
            this.label2.Location = new System.Drawing.Point(14, 248);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(448, 13);
            this.label2.TabIndex = 9;
            this.label2.Text = "Shows a message prompt where user needs to enter the reason for enterting the exp" +
    "ort mode.";
            // 
            // label3
            // 
            this.label3.AutoSize = true;
            this.label3.ForeColor = System.Drawing.SystemColors.ControlDarkDark;
            this.label3.Location = new System.Drawing.Point(14, 299);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(433, 13);
            this.label3.TabIndex = 10;
            this.label3.Text = "Shows a message prompt where user needs to enter the reason for independent playb" +
    "ack.";
            // 
            // AuditRuleUserControl
            // 
            this.Controls.Add(this.label3);
            this.Controls.Add(this.label2);
            this.Controls.Add(this.label1);
            this.Controls.Add(this._lblUsers);
            this.Controls.Add(this._lstUsers);
            this.Controls.Add(this._btnRefresh);
            this.Controls.Add(this._lblLoading);
            this.Controls.Add(this._chkAuditPlayback);
            this.Controls.Add(this._chkAuditExport);
            this.Controls.Add(this._chkAuditIndependentPlayback);
            this.Controls.Add(this._chkEnabled);
            this.Name = "AuditRuleUserControl";
            this.Size = new System.Drawing.Size(1081, 590);
            this.ResumeLayout(false);
            this.PerformLayout();

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

            var userNames = item.Properties.ContainsKey("UserNames")
                ? item.Properties["UserNames"]
                : (item.Properties.ContainsKey("UserName") ? item.Properties["UserName"] : "");

            var selectedUsers = userNames.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(u => u.Trim())
                .ToList();

            // Check matching users in the list
            for (int i = 0; i < _lstUsers.Items.Count; i++)
            {
                var name = _lstUsers.Items[i].ToString();
                _lstUsers.SetItemChecked(i, selectedUsers.Any(u =>
                    string.Equals(u, name, StringComparison.OrdinalIgnoreCase)));
            }

            // Add any users from config that aren't in the discovered list
            foreach (var user in selectedUsers)
            {
                if (!string.IsNullOrEmpty(user) &&
                    !_allUsers.Any(u => string.Equals(u, user, StringComparison.OrdinalIgnoreCase)))
                {
                    _lstUsers.Items.Add(user, true);
                }
            }

            _chkAuditPlayback.Checked = !item.Properties.ContainsKey("AuditPlayback") || item.Properties["AuditPlayback"] == "Yes";
            _chkAuditExport.Checked = !item.Properties.ContainsKey("AuditExport") || item.Properties["AuditExport"] == "Yes";
            _chkAuditIndependentPlayback.Checked = !item.Properties.ContainsKey("AuditIndependentPlayback") || item.Properties["AuditIndependentPlayback"] == "Yes";
            _chkEnabled.Checked = !item.Properties.ContainsKey("Enabled") || item.Properties["Enabled"] == "Yes";
        }

        public void ClearContent()
        {
            for (int i = 0; i < _lstUsers.Items.Count; i++)
                _lstUsers.SetItemChecked(i, false);

            _chkAuditPlayback.Checked = true;
            _chkAuditExport.Checked = true;
            _chkAuditIndependentPlayback.Checked = true;
            _chkEnabled.Checked = true;
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
            var selected = GetSelectedUserNames();
            item.Properties["UserNames"] = string.Join(";", selected);
            item.Properties["AuditPlayback"] = _chkAuditPlayback.Checked ? "Yes" : "No";
            item.Properties["AuditExport"] = _chkAuditExport.Checked ? "Yes" : "No";
            item.Properties["AuditIndependentPlayback"] = _chkAuditIndependentPlayback.Checked ? "Yes" : "No";
            item.Properties["Enabled"] = _chkEnabled.Checked ? "Yes" : "No";
        }
    }
}
