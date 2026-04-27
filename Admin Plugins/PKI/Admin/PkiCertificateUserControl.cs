using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using PKI.Crypto;
using PKI.Storage;
using VideoOS.Platform;

namespace PKI.Admin
{
    // Two-mode cert editor:
    //   Edit mode  - input form with two columns (required on the left,
    //                optional on the right). Save triggers generation.
    //   View mode  - styled read-only summary plus export buttons. The
    //                form never reverts to editable for an issued cert; if
    //                anything in the cert needs to change, delete and
    //                re-create.
    //
    // Notable UX:
    //   - The Issuing CA dropdown is shown only for non-Root roles, as the
    //     first field of Identity (no separate "Signed by" header).
    //   - "Fill from this machine" populates SAN entries with the local
    //     hostname, FQDN, and all IPv4/IPv6 addresses.
    //   - Display name auto-fills from CN until the admin edits it manually.
    //   - DNS and IP entries in SAN are validated; bad input is rejected.
    public class PkiCertificateUserControl : UserControl
    {
        public event EventHandler ConfigurationChangedByUser;

        private readonly RolePreset _role;
        private readonly PkiCertItemManager _owner;
        private Item _item;
        private bool _suppress;
        private bool _displayNameTouched;

        // Edit-mode controls
        private readonly TextBox  _displayName  = new TextBox();
        private readonly TextBox  _cn  = new TextBox();
        private readonly TextBox  _o   = new TextBox();
        private readonly TextBox  _ou  = new TextBox();
        private readonly ComboBox _country = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly NumericUpDown _validityDays = new NumericUpDown { Minimum = 1, Maximum = 30 * 365, Value = 397 };
        private readonly ComboBox _keyAlg  = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly ComboBox _issuer  = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly ComboBox _sanType = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly TextBox  _sanInput = new TextBox();
        private readonly Button   _sanAdd  = new Button { Text = "Add" };
        private readonly Button   _sanRm   = new Button { Text = "Remove" };
        private readonly Button   _sanFill = new Button { Text = "Add this machine's hostname and IPs" };
        private readonly ListBox  _sanList = new ListBox();

        private TableLayoutPanel _editForm;
        private FlowLayoutPanel  _host;
        private Panel _loader;
        private Label _loaderLabel;

        // View-mode controls
        private TableLayoutPanel _viewForm;
        private TableLayoutPanel _detailsTable;
        private FlowLayoutPanel  _sanRow;
        private readonly Button _exportPemCert = new Button { Text = "Export PEM (.pem)" };
        private readonly Button _exportDerCert = new Button { Text = "Export DER (.der)" };
        private readonly Button _exportCrt     = new Button { Text = "Export .crt" };
        private readonly Button _exportPfx     = new Button { Text = "Export PFX (cert + key)..." };
        private readonly Button _exportPemKey  = new Button { Text = "Export PEM key PKCS#8..." };
        private readonly Button _exportPemRsa  = new Button { Text = "Export PEM RSA key PKCS#1..." };
        private readonly Button _exportBundle  = new Button { Text = "Export PEM cert + key bundle..." };

        public PkiCertificateUserControl(RolePreset role, PkiCertItemManager owner)
        {
            _role = role;
            _owner = owner;
            Dock = DockStyle.Fill;
            AutoScroll = true;
            // Double-buffer the user control so populating the form during
            // FillContent doesn't paint each child as it appears.
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
            UpdateStyles();
            DoubleBuffered = true;

            InitKeyAlg();
            InitCountry();
            InitSanType();
            WireEvents();

            BuildEditForm();
            BuildViewForm();

            _host = new FlowLayoutPanel
            {
                Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.TopDown, WrapContents = false,
            };
            Controls.Add(_host);

            // Loader overlay sits on top of everything and only becomes
            // visible while FillContent is rebuilding the form. Keeps
            // the admin from seeing rows pop in one by one.
            _loader = new Panel
            {
                Dock = DockStyle.Fill, BackColor = SystemColors.Control,
                Visible = false,
            };
            _loaderLabel = new Label
            {
                Text = "Loading certificate...",
                AutoSize = true, ForeColor = Color.FromArgb(110, 110, 110),
                Font = new Font(Font.FontFamily, 11F, FontStyle.Regular),
            };
            _loader.Controls.Add(_loaderLabel);
            _loader.Resize += (s, e) =>
            {
                _loaderLabel.Left = Math.Max(0, (_loader.ClientSize.Width - _loaderLabel.Width) / 2);
                _loaderLabel.Top  = Math.Max(0, (_loader.ClientSize.Height - _loaderLabel.Height) / 2);
            };
            Controls.Add(_loader);
            _loader.BringToFront();
        }

        private void InitKeyAlg()
        {
            _keyAlg.Items.AddRange(new object[]
            {
                "RSA-2048", "RSA-3072", "RSA-4096", "ECDSA-P256", "ECDSA-P384"
            });
            _keyAlg.SelectedIndex = 0;
        }

        private void InitCountry()
        {
            _country.DisplayMember = "Name";
            _country.ValueMember = "Code";
            foreach (var e in Iso3166Codes.AsEnumerable()) _country.Items.Add(e);
            _country.SelectedIndex = 0;
        }

        private void InitSanType()
        {
            _sanType.Items.AddRange(new object[] { "DNS", "IP" });
            _sanType.SelectedIndex = 0;
        }

        private void WireEvents()
        {
            _sanAdd.Click  += (s, e) => OnSanAdd();
            _sanRm.Click   += (s, e) => OnSanRemove();
            _sanFill.Click += (s, e) => OnSanFillFromMachine();

            _exportPemCert.Click += (s, e) => DoExport("pem-cert");
            _exportDerCert.Click += (s, e) => DoExport("der-cert");
            _exportCrt.Click     += (s, e) => DoExport("crt");
            _exportPfx.Click     += (s, e) => DoExport("pfx");
            _exportPemKey.Click  += (s, e) => DoExport("pem-key-pkcs8");
            _exportPemRsa.Click  += (s, e) => DoExport("pem-key-pkcs1");
            _exportBundle.Click  += (s, e) => DoExport("pem-bundle");

            EventHandler change = (s, e) => Fire();
            _o.TextChanged += change;
            _ou.TextChanged += change;
            _country.SelectedIndexChanged += change;
            _validityDays.ValueChanged += change;
            _keyAlg.SelectedIndexChanged += change;
            _issuer.SelectedIndexChanged += change;

            _cn.TextChanged += (s, e) =>
            {
                if (!_displayNameTouched)
                {
                    _suppress = true;
                    try { _displayName.Text = _cn.Text; }
                    finally { _suppress = false; }
                }
                Fire();
            };
            _displayName.TextChanged += (s, e) =>
            {
                if (_suppress) return;
                _displayNameTouched = !string.IsNullOrEmpty(_displayName.Text);
                Fire();
            };
        }

        private void Fire()
        {
            if (_suppress) return;
            ConfigurationChangedByUser?.Invoke(this, EventArgs.Empty);
        }

        // ── Edit form ──────────────────────────────────────────────────────

        private void BuildEditForm()
        {
            // Two columns: required (left), optional (right). The issuing CA
            // for non-Root roles takes the very top row spanning both columns.
            var grid = new TableLayoutPanel
            {
                AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 4, Padding = new Padding(20, 16, 20, 16),
            };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 320));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 320));

            void Section(string text)
            {
                var lbl = new Label
                {
                    Text = text, Font = new Font(Font.FontFamily, 11F, FontStyle.Bold),
                    ForeColor = Color.FromArgb(50, 80, 120), AutoSize = true,
                    Margin = new Padding(0, 14, 0, 6),
                };
                grid.Controls.Add(lbl, 0, grid.RowCount);
                grid.SetColumnSpan(lbl, 4);
                grid.RowCount++;
            }

            // Required = red asterisk. Optional fields get NO suffix.
            void Pair(int col, string label, bool required, Control input, string hint, int width = 280)
            {
                input.Width = width;
                input.Anchor = AnchorStyles.Left;

                var lbl = new FieldLabel
                {
                    Text = label, Required = required,
                    Margin = new Padding(0, 9, 0, 0),
                };

                var inputBox = new FlowLayoutPanel
                {
                    AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false,
                    Margin = new Padding(0, 4, 14, 0),
                };
                inputBox.Controls.Add(input);
                if (!string.IsNullOrEmpty(hint))
                {
                    inputBox.Controls.Add(new Label
                    {
                        Text = hint, AutoSize = true, MaximumSize = new Size(width, 0),
                        ForeColor = Color.Gray, Font = new Font(Font.FontFamily, 8F),
                        Margin = new Padding(2, 2, 0, 0),
                    });
                }
                grid.Controls.Add(lbl, col, grid.RowCount);
                grid.Controls.Add(inputBox, col + 1, grid.RowCount);
            }

            void NewRow() { grid.RowCount++; }

            // ── Issuing CA (only when this isn't a Root) ──────────────────
            if (_role != RolePreset.RootCA)
            {
                Section("Issuing CA");
                _issuer.Width = 770;
                grid.Controls.Add(new Label { Text = "Sign with", AutoSize = true, Margin = new Padding(0, 6, 0, 0) }, 0, grid.RowCount);
                grid.Controls.Add(_issuer, 1, grid.RowCount);
                grid.SetColumnSpan(_issuer, 3);
                grid.RowCount++;
                grid.Controls.Add(new Label
                {
                    Text = "An existing Root or Intermediate certificate. Only CAs whose private key lives on this server are listed.",
                    AutoSize = true, ForeColor = Color.Gray, Font = new Font(Font.FontFamily, 8F),
                    Margin = new Padding(0, 0, 0, 4), MaximumSize = new Size(770, 0),
                }, 1, grid.RowCount);
                grid.SetColumnSpan(grid.GetControlFromPosition(1, grid.RowCount), 3);
                grid.RowCount++;
            }

            // ── Identity (required only) ──────────────────────────────────
            Section("Identity");
            Pair(0, "Display name", true,  _displayName, "Shown in the tree on the left. Auto-fills from CN if you set CN first.");
            NewRow();
            Pair(0, "Validity (days)", true, _validityDays,
                _role == RolePreset.RootCA      ? "Root CAs typically last 10 to 20 years." :
                _role == RolePreset.IntermediateCA ? "Intermediate CAs typically last 5 to 10 years." :
                "Leaf certs typically last 1 year (max 397 days for browser trust).");
            NewRow();
            Pair(0, "Key algorithm", true, _keyAlg, "RSA-3072 or ECDSA-P256 are good defaults.");
            NewRow();

            // ── Extended properties (collapsible) ─────────────────────────
            // CN, O, OU, Country are all optional for cert generation -
            // they get embedded in the Subject DN if set, otherwise we
            // fall back to defaults. Stuffing them behind a toggle keeps
            // the main form focused on the four fields admins almost
            // always need: display name, validity, key alg, and SANs.
            string cnHint;
            switch (_role)
            {
                case RolePreset.RootCA:
                case RolePreset.IntermediateCA:
                    cnHint = "Friendly label embedded in the cert (e.g. \"Acme Internal Root CA 2025\")."; break;
                case RolePreset.HttpsServer:
                    cnHint = "Primary FQDN. Modern browsers actually verify hostnames against the SAN list below; CN is for backwards compatibility."; break;
                case RolePreset.Dot1xClient:
                    cnHint = "Identity of the device or user (e.g. \"camera-01.example.local\" or \"alice@example.com\")."; break;
                default:
                    cnHint = "Primary identifier embedded in the cert."; break;
            }

            // The expander is just a Label that toggles a panel's
            // visibility. WinForms has no native Expander control and
            // pulling in a third-party UI lib for one accordion isn't
            // worth it.
            var extToggle = new Label
            {
                Text = "▶  Extended properties (CN, O, OU, Country)",
                AutoSize = true, Cursor = Cursors.Hand,
                Font = new Font(Font.FontFamily, 10F, FontStyle.Bold),
                ForeColor = Color.FromArgb(50, 80, 120),
                Margin = new Padding(0, 14, 0, 6),
            };
            grid.Controls.Add(extToggle, 0, grid.RowCount);
            grid.SetColumnSpan(extToggle, 4);
            grid.RowCount++;

            var extPanel = new TableLayoutPanel
            {
                AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 4, Visible = false,
                Margin = new Padding(0, 0, 0, 6),
            };
            extPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
            extPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 320));
            extPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
            extPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 320));

            void ExtPair(int col, string label, Control input, string hint, int width = 280)
            {
                input.Width = width;
                input.Anchor = AnchorStyles.Left;
                var lbl = new FieldLabel
                {
                    Text = label, Required = false,
                    Margin = new Padding(0, 9, 0, 0),
                };
                var inputBox = new FlowLayoutPanel
                {
                    AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false,
                    Margin = new Padding(0, 4, 14, 0),
                };
                inputBox.Controls.Add(input);
                if (!string.IsNullOrEmpty(hint))
                {
                    inputBox.Controls.Add(new Label
                    {
                        Text = hint, AutoSize = true, MaximumSize = new Size(width, 0),
                        ForeColor = Color.Gray, Font = new Font(Font.FontFamily, 8F),
                        Margin = new Padding(2, 2, 0, 0),
                    });
                }
                extPanel.Controls.Add(lbl, col, extPanel.RowCount);
                extPanel.Controls.Add(inputBox, col + 1, extPanel.RowCount);
            }

            ExtPair(0, "Common Name (CN)", _cn, cnHint + " Leave empty to use the display name.");
            ExtPair(2, "Organization (O)", _o,  "Your company or department.");
            extPanel.RowCount++;
            ExtPair(0, "Organizational Unit (OU)", _ou, "e.g. IT, Security, R&D.");
            ExtPair(2, "Country (C)", _country, "ISO 3166-1 alpha-2 code.");
            extPanel.RowCount++;

            grid.Controls.Add(extPanel, 0, grid.RowCount);
            grid.SetColumnSpan(extPanel, 4);
            grid.RowCount++;

            extToggle.Click += (s, e) =>
            {
                extPanel.Visible = !extPanel.Visible;
                extToggle.Text = (extPanel.Visible ? "▼" : "▶")
                               + "  Extended properties (CN, O, OU, Country)";
            };

            // ── Subject Alternative Names (only relevant for non-CA roles) ─
            if (_role != RolePreset.RootCA && _role != RolePreset.IntermediateCA)
            {
                Section("Subject Alternative Names");

                _sanType.Width = 70;
                _sanInput.Width = 280;
                _sanAdd.Width = 60;
                _sanRm.Width = 70;
                _sanFill.Width = 250;
                _sanFill.Height = 26;

                var sanInputPanel = new FlowLayoutPanel
                {
                    AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false,
                    Margin = new Padding(0, 4, 0, 0),
                };
                sanInputPanel.Controls.AddRange(new Control[] { _sanType, _sanInput, _sanAdd, _sanRm });

                grid.Controls.Add(new Label { Text = "Add entry", AutoSize = true, Margin = new Padding(0, 8, 0, 0) }, 0, grid.RowCount);
                grid.Controls.Add(sanInputPanel, 1, grid.RowCount);
                grid.SetColumnSpan(sanInputPanel, 3);
                grid.RowCount++;

                grid.Controls.Add(new Label(), 0, grid.RowCount);
                grid.Controls.Add(_sanFill, 1, grid.RowCount);
                grid.SetColumnSpan(_sanFill, 3);
                grid.RowCount++;

                _sanList.Width = 770;
                _sanList.Height = 90;
                grid.Controls.Add(new Label { Text = "Entries", AutoSize = true, Margin = new Padding(0, 8, 0, 0) }, 0, grid.RowCount);
                grid.Controls.Add(_sanList, 1, grid.RowCount);
                grid.SetColumnSpan(_sanList, 3);
                grid.RowCount++;

                grid.Controls.Add(new Label
                {
                    Text = _role == RolePreset.HttpsServer
                        ? "Required for HTTPS. Add every hostname clients will use, plus any direct-IP access points."
                        : "Optional for client/service certs.",
                    AutoSize = true, ForeColor = Color.Gray, Font = new Font(Font.FontFamily, 8F),
                    Margin = new Padding(0, 0, 0, 6), MaximumSize = new Size(770, 0),
                }, 1, grid.RowCount);
                grid.SetColumnSpan(grid.GetControlFromPosition(1, grid.RowCount), 3);
                grid.RowCount++;
            }

            _editForm = grid;
        }

        // ── View form ──────────────────────────────────────────────────────

        private void BuildViewForm()
        {
            var grid = new TableLayoutPanel
            {
                AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1, Padding = new Padding(20, 16, 20, 24),
            };

            void Section(string text, Color? color = null)
            {
                grid.Controls.Add(new Label
                {
                    Text = text, AutoSize = true,
                    Font = new Font(Font.FontFamily, 11F, FontStyle.Bold),
                    ForeColor = color ?? Color.FromArgb(50, 80, 120),
                    Margin = new Padding(0, 14, 0, 8),
                });
                grid.RowCount++;
            }

            // Identity card
            Section("Certificate details");
            _detailsTable = new TableLayoutPanel
            {
                AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2,
                Padding = new Padding(14, 12, 14, 12),
                CellBorderStyle = TableLayoutPanelCellBorderStyle.None,
            };
            _detailsTable.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
            _detailsTable.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 740));
            grid.Controls.Add(_detailsTable);
            grid.RowCount++;

            // SAN row (filled at FillContent time)
            _sanRow = new FlowLayoutPanel
            {
                AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false,
                Margin = new Padding(0, 12, 0, 0),
            };
            grid.Controls.Add(_sanRow);
            grid.RowCount++;

            Section("Export - public certificate only");
            var row1 = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = true };
            foreach (var b in new[] { _exportPemCert, _exportDerCert, _exportCrt })
            {
                b.Width = 200; b.Height = 30; b.Margin = new Padding(0, 0, 8, 6);
                row1.Controls.Add(b);
            }
            grid.Controls.Add(row1);
            grid.RowCount++;

            Section("Export - including private key");
            var row2 = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = true };
            foreach (var b in new[] { _exportPfx, _exportPemKey, _exportPemRsa, _exportBundle })
            {
                b.Width = 240; b.Height = 30; b.Margin = new Padding(0, 0, 8, 6);
                row2.Controls.Add(b);
            }
            grid.Controls.Add(row2);
            grid.RowCount++;

            grid.Controls.Add(new Label
            {
                Text = "PEM RSA (PKCS#1) is only valid for RSA keys. PFX and PEM key exports prompt for an output password where applicable.",
                AutoSize = true, ForeColor = Color.Gray, Font = new Font(Font.FontFamily, 8F),
                Margin = new Padding(0, 6, 0, 0),
            });
            grid.RowCount++;

            _viewForm = grid;
        }

        // ── Mode switching ────────────────────────────────────────────────

        public void FillContent(Item item)
        {
            _item = item;

            // Show the loader and force it to paint NOW. Without Refresh()
            // the overlay won't actually appear because the UI thread is
            // about to be busy in SuspendLayout/Show*.
            _loader.Visible = true;
            _loader.BringToFront();
            _loader.Refresh();

            _suppress = true;
            SuspendLayout();
            _host.SuspendLayout();
            try
            {
                bool issued = !string.IsNullOrEmpty(Get(item, "Thumbprint"));
                if (issued) ShowView();
                else        ShowEdit();
            }
            finally
            {
                _suppress = false;
                _host.ResumeLayout(false);
                ResumeLayout(true);
                _loader.Visible = false;
            }
        }

        public void ClearContent()
        {
            _host.Controls.Clear();
        }

        public void UpdateItem(Item item)
        {
            if (item == null) return;

            bool issued = !string.IsNullOrEmpty(Get(item, "Thumbprint"));
            if (issued)
            {
                if (!string.IsNullOrWhiteSpace(_displayName.Text))
                    item.Name = _displayName.Text.Trim();
                return;
            }

            if (!string.IsNullOrWhiteSpace(_displayName.Text)) item.Name = _displayName.Text.Trim();

            item.Properties["Subject_CN"] = (_cn.Text ?? "").Trim();
            item.Properties["Subject_O"]  = (_o.Text ?? "").Trim();
            item.Properties["Subject_OU"] = (_ou.Text ?? "").Trim();
            item.Properties["Subject_C"]  = SelectedCountryCode();
            item.Properties["ValidityDays"]        = ((int)_validityDays.Value).ToString();
            item.Properties["KeyAlgorithmRequest"] = _keyAlg.SelectedItem?.ToString() ?? "RSA-2048";
            item.Properties["RolePreset"]          = _role.ToString();

            var sans = new List<string>();
            foreach (var o in _sanList.Items) sans.Add(o.ToString());
            item.Properties["SubjectAlternativeNames"] = string.Join("|", sans);

            if (_role != RolePreset.RootCA)
            {
                if (_issuer.SelectedItem is IssuerEntry ie && !string.IsNullOrEmpty(ie.Thumbprint))
                    item.Properties["IssuerThumbprint"] = ie.Thumbprint;
            }
        }

        public string ValidateInput()
        {
            if (string.IsNullOrWhiteSpace(_displayName.Text)) return "Display name is required.";
            if (_validityDays.Value < 1) return "Validity must be at least 1 day.";
            if (_role != RolePreset.RootCA)
            {
                var ie = _issuer.SelectedItem as IssuerEntry;
                if (ie == null || string.IsNullOrEmpty(ie.Thumbprint))
                    return "Pick an issuing CA at the top of the form.";
            }
            foreach (var o in _sanList.Items)
            {
                var s = o.ToString();
                if (s.StartsWith("IP:", StringComparison.OrdinalIgnoreCase))
                {
                    var ipText = s.Substring(3);
                    if (!IsValidIp(ipText))
                        return "Invalid IP address in SAN: " + ipText;
                }
                else if (s.StartsWith("DNS:", StringComparison.OrdinalIgnoreCase))
                {
                    var dns = s.Substring(4);
                    if (!IsValidHostname(dns))
                        return "Invalid DNS name in SAN: " + dns;
                }
            }
            var c = SelectedCountryCode();
            if (!string.IsNullOrEmpty(c) && c.Length != 2)
                return "Country code must be 2 letters (ISO 3166-1).";
            return null;
        }

        // ── Edit-mode population ──────────────────────────────────────────

        private void ShowEdit()
        {
            _host.Controls.Clear();
            _host.Controls.Add(_editForm);

            _displayName.Text = _item?.Name ?? "";
            _cn.Text = Get(_item, "Subject_CN");
            _o.Text  = Get(_item, "Subject_O");
            _ou.Text = Get(_item, "Subject_OU");
            SelectCountry(Get(_item, "Subject_C"));

            // Treat the default seeded name as "untouched" so it auto-syncs from CN.
            _displayNameTouched = !string.IsNullOrEmpty(_displayName.Text)
                                  && !_displayName.Text.StartsWith("New ", StringComparison.OrdinalIgnoreCase);

            int days;
            if (!int.TryParse(Get(_item, "ValidityDays"), out days) || days <= 0)
                days = RolePresets.For(_role).DefaultValidityDays;
            _validityDays.Value = Math.Min(_validityDays.Maximum, Math.Max(_validityDays.Minimum, days));

            var keyAlg = Get(_item, "KeyAlgorithmRequest");
            if (!string.IsNullOrEmpty(keyAlg) && _keyAlg.Items.Contains(keyAlg))
                _keyAlg.SelectedItem = keyAlg;

            _sanList.Items.Clear();
            foreach (var entry in (Get(_item, "SubjectAlternativeNames") ?? "").Split('|'))
                if (!string.IsNullOrWhiteSpace(entry)) _sanList.Items.Add(entry.Trim());

            if (_role != RolePreset.RootCA)
            {
                _issuer.Items.Clear();
                _issuer.Items.Add(IssuerEntry.None);
                foreach (var ca in PkiCertItemManager.FindAllCAs())
                {
                    var thumb = ca.Properties.ContainsKey("Thumbprint") ? ca.Properties["Thumbprint"] : "";
                    if (string.IsNullOrEmpty(thumb)) continue;
                    var pfx = ca.Properties.ContainsKey("Pfx") ? ca.Properties["Pfx"] : "";
                    if (string.IsNullOrEmpty(pfx)) continue;
                    var cn = ca.Properties.ContainsKey("Subject_CN") ? ca.Properties["Subject_CN"] : "";
                    _issuer.Items.Add(new IssuerEntry { Name = ca.Name, Cn = cn, Thumbprint = thumb });
                }
                _issuer.SelectedIndex = 0;
                var current = Get(_item, "IssuerThumbprint");
                if (!string.IsNullOrEmpty(current))
                    foreach (var o in _issuer.Items)
                        if (o is IssuerEntry ie && ie.Thumbprint == current) { _issuer.SelectedItem = o; break; }
            }
        }

        // ── View-mode population ──────────────────────────────────────────

        private void ShowView()
        {
            _host.Controls.Clear();
            _host.Controls.Add(_viewForm);

            _displayName.Text = _item?.Name ?? "";

            _detailsTable.Controls.Clear();
            _detailsTable.RowStyles.Clear();
            _detailsTable.RowCount = 0;

            void Row(string k, string v, bool monospace = false)
            {
                var lbl = new Label
                {
                    Text = k, AutoSize = true, ForeColor = Color.FromArgb(110, 110, 110),
                    Margin = new Padding(0, 4, 12, 4),
                };
                var valFont = monospace
                    ? new Font("Cascadia Code", 9F, FontStyle.Regular)
                    : new Font(Font.FontFamily, 9.5F, FontStyle.Regular);
                var val = new Label
                {
                    Text = string.IsNullOrEmpty(v) ? "(none)" : v,
                    AutoSize = true, Font = valFont,
                    ForeColor = string.IsNullOrEmpty(v) ? Color.Gray : Color.FromArgb(30, 30, 30),
                    Margin = new Padding(0, 4, 0, 4),
                    MaximumSize = new Size(720, 0),
                };
                _detailsTable.Controls.Add(lbl, 0, _detailsTable.RowCount);
                _detailsTable.Controls.Add(val, 1, _detailsTable.RowCount);
                _detailsTable.RowCount++;
            }

            string subject = Get(_item, "Subject");
            string issuer  = Get(_item, "Issuer");
            string thumb   = Get(_item, "Thumbprint");
            string serial  = Get(_item, "SerialNumber");
            string nb      = Get(_item, "NotBefore");
            string na      = Get(_item, "NotAfter");
            string ka      = Get(_item, "KeyAlgorithm");
            string san     = Get(_item, "SubjectAlternativeNames");

            Row("Role",         RolePresets.DisplayName(_role));
            Row("Display name", _item?.Name);
            Row("Subject",      subject);
            Row("Issuer",       issuer);
            Row("Thumbprint",   thumb, monospace: true);
            Row("Serial",       serial, monospace: true);
            Row("Not before",   nb);
            Row("Not after",    na);
            Row("Key algorithm", ka);

            // SAN section as its own row of pretty chips
            _sanRow.Controls.Clear();
            if (!string.IsNullOrEmpty(san))
            {
                _sanRow.Controls.Add(new Label
                {
                    Text = "Subject Alternative Names",
                    Font = new Font(Font.FontFamily, 11F, FontStyle.Bold),
                    ForeColor = Color.FromArgb(50, 80, 120), AutoSize = true,
                    Margin = new Padding(0, 8, 0, 8),
                });
                var chips = new FlowLayoutPanel
                {
                    AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = true,
                    Margin = new Padding(0, 0, 0, 0),
                };
                foreach (var entry in san.Split('|'))
                {
                    var t = entry.Trim();
                    if (t.Length == 0) continue;
                    chips.Controls.Add(new Label
                    {
                        Text = t, AutoSize = true,
                        Padding = new Padding(8, 4, 8, 4),
                        Margin = new Padding(0, 0, 6, 6),
                        BackColor = Color.FromArgb(230, 240, 250),
                        ForeColor = Color.FromArgb(20, 80, 140),
                        BorderStyle = BorderStyle.FixedSingle,
                        Font = new Font("Cascadia Code", 8.5F, FontStyle.Regular),
                    });
                }
                _sanRow.Controls.Add(chips);
            }

            _exportPemRsa.Enabled = (ka != null && ka.StartsWith("RSA-"));
        }

        // ── SAN actions ───────────────────────────────────────────────────

        private void OnSanAdd()
        {
            var v = (_sanInput.Text ?? "").Trim();
            if (v.Length == 0) return;
            var prefix = (string)_sanType.SelectedItem + ":";
            string error = null;
            if (prefix == "DNS:" && !IsValidHostname(v)) error = "Not a valid DNS name: " + v;
            else if (prefix == "IP:" && !IsValidIp(v))   error = "Not a valid IP address: " + v;
            if (error != null)
            {
                MessageBox.Show(error, "PKI", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            var entry = prefix + v;
            if (!_sanList.Items.Contains(entry)) _sanList.Items.Add(entry);
            _sanInput.Clear();
            Fire();
        }

        private void OnSanRemove()
        {
            if (_sanList.SelectedIndex >= 0)
            {
                _sanList.Items.RemoveAt(_sanList.SelectedIndex);
                Fire();
            }
        }

        private void OnSanFillFromMachine()
        {
            try
            {
                var hostname = Environment.MachineName;
                if (!string.IsNullOrEmpty(hostname)) AddSanIfNew("DNS:" + hostname);

                try
                {
                    var fqdn = Dns.GetHostEntry("").HostName;
                    if (!string.IsNullOrEmpty(fqdn)
                        && !string.Equals(fqdn, hostname, StringComparison.OrdinalIgnoreCase))
                        AddSanIfNew("DNS:" + fqdn);
                }
                catch { }

                try
                {
                    var addresses = Dns.GetHostAddresses("");
                    foreach (var addr in addresses)
                    {
                        if (addr.AddressFamily != AddressFamily.InterNetwork
                            && addr.AddressFamily != AddressFamily.InterNetworkV6) continue;
                        if (IPAddress.IsLoopback(addr)) continue;
                        if (addr.IsIPv6LinkLocal) continue;
                        AddSanIfNew("IP:" + addr.ToString());
                    }
                }
                catch { }

                Fire();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Could not resolve this machine's network identity:\n\n" + ex.Message,
                    "PKI", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void AddSanIfNew(string entry)
        {
            if (!_sanList.Items.Contains(entry)) _sanList.Items.Add(entry);
        }

        // ── Export ────────────────────────────────────────────────────────

        private void DoExport(string format) => CertExporter.ExportInteractive(_item, format, this);

        // ── Helpers ───────────────────────────────────────────────────────

        private static readonly Regex _hostLabel =
            new Regex(@"^(?:\*|[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?)$", RegexOptions.Compiled);

        private static bool IsValidHostname(string s)
        {
            if (string.IsNullOrWhiteSpace(s) || s.Length > 253) return false;
            var labels = s.Split('.');
            for (int i = 0; i < labels.Length; i++)
            {
                if (!_hostLabel.IsMatch(labels[i])) return false;
                if (labels[i] == "*" && i != 0) return false;   // wildcard only as first label
            }
            return labels.Length >= 1;
        }

        private static bool IsValidIp(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return false;
            return IPAddress.TryParse(s, out _);
        }

        private string SelectedCountryCode()
            => (_country.SelectedItem as Iso3166Codes.Entry)?.Code ?? "";

        private void SelectCountry(string code)
        {
            for (int i = 0; i < _country.Items.Count; i++)
                if (_country.Items[i] is Iso3166Codes.Entry e
                    && string.Equals(e.Code, code ?? "", StringComparison.OrdinalIgnoreCase))
                { _country.SelectedIndex = i; return; }
            _country.SelectedIndex = 0;
        }

        private static string Get(Item item, string key)
            => (item != null && item.Properties.ContainsKey(key)) ? item.Properties[key] : "";

        private class IssuerEntry
        {
            public string Name;
            public string Cn;
            public string Thumbprint;

            public static readonly IssuerEntry None = new NoneEntry();
            public override string ToString()
                => string.IsNullOrEmpty(Cn) ? Name : $"{Name}  -  CN={Cn}";

            private class NoneEntry : IssuerEntry
            {
                public NoneEntry() { Name = "(select an issuing CA)"; Thumbprint = ""; }
                public override string ToString() => Name;
            }
        }

        // Label that paints an optional red "*" tightly after the text.
        // Building "label + asterisk" out of two AutoSize labels in a
        // FlowLayoutPanel left a noticeable gap between them because
        // WinForms' AutoSize Label measurement bakes in glyph-overhang
        // padding. Drawing both pieces in one ClientSize with
        // TextRenderer.NoPadding keeps the asterisk ~3px after the text.
        private class FieldLabel : Label
        {
            private static readonly Color AsteriskColor = Color.FromArgb(200, 40, 40);
            private const TextFormatFlags Flags =
                TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.Left;

            public bool Required { get; set; }

            public FieldLabel()
            {
                AutoSize = true;
                Padding = Padding.Empty;
            }

            public override Size GetPreferredSize(Size proposedSize)
            {
                var size = TextRenderer.MeasureText(Text ?? "", Font, proposedSize, Flags);
                if (Required)
                {
                    using (var bold = new Font(Font, FontStyle.Bold))
                        size.Width += TextRenderer.MeasureText("*", bold, proposedSize, Flags).Width;
                }
                return size;
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                TextRenderer.DrawText(e.Graphics, Text ?? "", Font, new Point(0, 0), ForeColor, Flags);
                if (!Required) return;
                var w = TextRenderer.MeasureText(e.Graphics, Text ?? "", Font, ClientSize, Flags).Width;
                using (var bold = new Font(Font, FontStyle.Bold))
                    TextRenderer.DrawText(e.Graphics, "*", bold, new Point(w, 0), AsteriskColor, Flags);
            }
        }
    }
}
