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
    public class AuditRuleUserControl : UserControl
    {
        private CheckedListBox _lstUsers;
        private Button _btnRefresh;
        private CheckBox _chkAuditPlayback;
        private CheckBox _chkAuditExport;
        private CheckBox _chkAuditIndependentPlayback;
        private CheckBox _chkEnabled;
        private Label _lblLoading;

        private List<string> _allUsers = new List<string>();
        private readonly PluginLog _log = new PluginLog("Admin Auditor - AuditRule UC");

        internal event EventHandler ConfigurationChangedByUser;

        public AuditRuleUserControl()
        {
            InitializeComponent();
            LoadUsersAsync();
        }

        internal void OnUserChange(object sender, EventArgs e)
        {
            ConfigurationChangedByUser?.Invoke(this, EventArgs.Empty);
        }

        private void InitializeComponent()
        {
            SuspendLayout();

            var y = 12;
            const int labelX = 12;
            const int controlX = 180;
            const int rowHeight = 30;

            // Users label
            var lblUsers = new Label
            {
                Text = "Users:",
                Location = new System.Drawing.Point(labelX, y + 3),
                AutoSize = true
            };
            Controls.Add(lblUsers);

            // Refresh button
            _btnRefresh = new Button
            {
                Text = "Refresh",
                Location = new System.Drawing.Point(controlX + 260, y),
                Width = 70,
                Height = 23
            };
            _btnRefresh.Click += (s, e) => LoadUsersAsync();
            Controls.Add(_btnRefresh);

            // Loading label (shown while loading)
            _lblLoading = new Label
            {
                Text = "Loading users...",
                Location = new System.Drawing.Point(controlX, y + 3),
                AutoSize = true,
                Visible = false
            };
            Controls.Add(_lblLoading);

            // Users CheckedListBox
            _lstUsers = new CheckedListBox
            {
                Location = new System.Drawing.Point(controlX, y),
                Width = 250,
                Height = 120,
                CheckOnClick = true
            };
            _lstUsers.ItemCheck += (s, e) => BeginInvoke(new Action(() => OnUserChange(s, e)));
            Controls.Add(_lstUsers);
            y += 124 + 4;

            // Audit Playback
            _chkAuditPlayback = new CheckBox
            {
                Text = "Audit Playback",
                Location = new System.Drawing.Point(controlX, y),
                Checked = true,
                AutoSize = true
            };
            _chkAuditPlayback.CheckedChanged += OnUserChange;
            Controls.Add(_chkAuditPlayback);
            y += rowHeight;

            // Audit Export
            _chkAuditExport = new CheckBox
            {
                Text = "Audit Export",
                Location = new System.Drawing.Point(controlX, y),
                Checked = true,
                AutoSize = true
            };
            _chkAuditExport.CheckedChanged += OnUserChange;
            Controls.Add(_chkAuditExport);
            y += rowHeight;

            // Audit Independent Playback
            _chkAuditIndependentPlayback = new CheckBox
            {
                Text = "Audit Independent Playback",
                Location = new System.Drawing.Point(controlX, y),
                Checked = true,
                AutoSize = true
            };
            _chkAuditIndependentPlayback.CheckedChanged += OnUserChange;
            Controls.Add(_chkAuditIndependentPlayback);
            y += rowHeight;

            // Enabled
            _chkEnabled = new CheckBox
            {
                Text = "Enabled",
                Location = new System.Drawing.Point(controlX, y),
                Checked = true,
                AutoSize = true
            };
            _chkEnabled.CheckedChanged += OnUserChange;
            Controls.Add(_chkEnabled);

            Name = "AuditRuleUserControl";
            Size = new System.Drawing.Size(520, y + rowHeight + 12);
            ResumeLayout(false);
            PerformLayout();
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
