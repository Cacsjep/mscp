using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Net;
using System.Windows.Forms;
using PKI.Crypto;
using PKI.Storage;
using VideoOS.Platform;

namespace PKI.Admin
{
    // Two-mode certificate editor:
    //   - Edit mode: input form for a not-yet-issued certificate. Shows
    //     required-field markers, hint text under each field, country code
    //     dropdown, SAN list, and (for non-Root) issuer dropdown. Validation
    //     runs on Save; the framework Save button triggers cert generation
    //     in the ItemManager.
    //   - View mode: certificate has been issued. Form is read-only, shows
    //     a summary panel and Export buttons (PEM / DER / PFX / CRT / PEM
    //     bundle of cert + key). No re-issue: if the admin needs different
    //     subject / SAN / validity / key alg, they delete and re-create.
    public class PkiCertificateUserControl : UserControl
    {
        public event EventHandler ConfigurationChangedByUser;

        private readonly RolePreset _role;
        private readonly PkiCertItemManager _owner;
        private Item _item;
        private bool _suppress;

        // Edit-mode controls
        private readonly TextBox _displayName  = new TextBox();
        private readonly TextBox _cn  = new TextBox();
        private readonly TextBox _o   = new TextBox();
        private readonly TextBox _ou  = new TextBox();
        private readonly ComboBox _country = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly NumericUpDown _validityDays = new NumericUpDown { Minimum = 1, Maximum = 30 * 365, Value = 397 };
        private readonly ComboBox _keyAlg = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly ComboBox _issuer = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly Label _issuerLabel = new Label { Text = "Issuing CA", AutoSize = true };
        private readonly Label _issuerHint  = new Label { AutoSize = true };
        private readonly ComboBox _sanType = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly TextBox _sanInput = new TextBox();
        private readonly Button _sanAdd  = new Button { Text = "Add" };
        private readonly Button _sanRm   = new Button { Text = "Remove" };
        private readonly ListBox _sanList = new ListBox();

        // Edit-mode containers
        private TableLayoutPanel _editForm;

        // View-mode controls
        private readonly Label _viewSummary = new Label { AutoSize = true };
        private readonly Button _exportPemCert = new Button { Text = "Export PEM cert (.pem)" };
        private readonly Button _exportDerCert = new Button { Text = "Export DER cert (.der)" };
        private readonly Button _exportCrt     = new Button { Text = "Export .crt" };
        private readonly Button _exportPfx     = new Button { Text = "Export PFX (cert + key)…" };
        private readonly Button _exportPemKey  = new Button { Text = "Export PEM key (PKCS#8)…" };
        private readonly Button _exportPemRsa  = new Button { Text = "Export PEM RSA key (PKCS#1)…" };
        private readonly Button _exportBundle  = new Button { Text = "Export PEM cert + key bundle…" };
        private TableLayoutPanel _viewForm;

        // Switchable host
        private Panel _host;

        public PkiCertificateUserControl(RolePreset role, PkiCertItemManager owner)
        {
            _role = role;
            _owner = owner;
            BackColor = Color.White;
            Dock = DockStyle.Fill;
            AutoScroll = true;

            InitKeyAlg();
            InitCountry();
            InitSanType();
            InitButtons();

            BuildEditForm();
            BuildViewForm();

            _host = new Panel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
            Controls.Add(_host);
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
            foreach (var e in Iso3166Codes.AsEnumerable())
                _country.Items.Add(e);
            _country.SelectedIndex = 0; // "(none)"
        }

        private void InitSanType()
        {
            _sanType.Items.AddRange(new object[] { "DNS", "IP" });
            _sanType.SelectedIndex = 0;
        }

        private void InitButtons()
        {
            _sanAdd.Click += (s, e) =>
            {
                var v = (_sanInput.Text ?? "").Trim();
                if (v.Length == 0) return;
                var prefix = ((string)_sanType.SelectedItem) + ":";
                _sanList.Items.Add(prefix + v);
                _sanInput.Clear();
                Fire();
            };
            _sanRm.Click += (s, e) =>
            {
                if (_sanList.SelectedIndex >= 0)
                {
                    _sanList.Items.RemoveAt(_sanList.SelectedIndex);
                    Fire();
                }
            };

            _exportPemCert.Click += (s, e) => DoExport("pem-cert");
            _exportDerCert.Click += (s, e) => DoExport("der-cert");
            _exportCrt.Click     += (s, e) => DoExport("crt");
            _exportPfx.Click     += (s, e) => DoExport("pfx");
            _exportPemKey.Click  += (s, e) => DoExport("pem-key-pkcs8");
            _exportPemRsa.Click  += (s, e) => DoExport("pem-key-pkcs1");
            _exportBundle.Click  += (s, e) => DoExport("pem-bundle");

            EventHandler change = (s, e) => Fire();
            _displayName.TextChanged += change;
            _cn.TextChanged += change;
            _o.TextChanged += change;
            _ou.TextChanged += change;
            _country.SelectedIndexChanged += change;
            _validityDays.ValueChanged += change;
            _keyAlg.SelectedIndexChanged += change;
            _issuer.SelectedIndexChanged += change;
        }

        private void Fire()
        {
            if (_suppress) return;
            ConfigurationChangedByUser?.Invoke(this, EventArgs.Empty);
        }

        // ── Edit form layout (two columns) ────────────────────────────────

        private void BuildEditForm()
        {
            var grid = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 4,
                Padding = new Padding(20, 16, 20, 16),
            };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170)); // label
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 280)); // input
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170)); // label
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 280)); // input

            void AddSection(string text)
            {
                var lbl = new Label
                {
                    Text = text,
                    Font = new Font(Font.FontFamily, 11F, FontStyle.Bold),
                    ForeColor = Color.FromArgb(50, 80, 120),
                    AutoSize = true,
                    Margin = new Padding(0, 14, 0, 6),
                };
                grid.Controls.Add(lbl, 0, grid.RowCount);
                grid.SetColumnSpan(lbl, 4);
                grid.RowCount++;
            }

            void AddPair(int col, string label, bool required, Control input, string hint, int width = 260)
            {
                input.Width = width;
                input.Anchor = AnchorStyles.Left;

                var lblPanel = new FlowLayoutPanel
                {
                    AutoSize = true, FlowDirection = FlowDirection.LeftToRight,
                    Margin = new Padding(0, 6, 0, 0), WrapContents = false,
                };
                lblPanel.Controls.Add(new Label
                {
                    Text = label, AutoSize = true,
                    Margin = new Padding(0, 3, 0, 0),
                });
                if (required)
                {
                    lblPanel.Controls.Add(new Label
                    {
                        Text = "*", AutoSize = true, ForeColor = Color.FromArgb(200, 40, 40),
                        Font = new Font(Font, FontStyle.Bold),
                        Margin = new Padding(2, 3, 0, 0),
                    });
                }
                else
                {
                    lblPanel.Controls.Add(new Label
                    {
                        Text = "  (optional)", AutoSize = true, ForeColor = Color.Gray,
                        Margin = new Padding(2, 3, 0, 0),
                    });
                }

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

                grid.Controls.Add(lblPanel, col, grid.RowCount);
                grid.Controls.Add(inputBox, col + 1, grid.RowCount);
            }

            void NewRow() { grid.RowCount++; }

            // ── Identity ──
            AddSection("Identity");
            AddPair(0, "Display name", true, _displayName, "Shown in the tree on the left");
            AddPair(2, "Common Name (CN)", true, _cn,
                _role == RolePreset.HttpsServer
                    ? "The fully-qualified domain name, e.g. server.example.com"
                    : "The primary identifier of the certificate");
            NewRow();

            AddPair(0, "Organization (O)", false, _o, "Your company / department name");
            AddPair(2, "Organizational Unit (OU)", false, _ou, "e.g. IT, Security");
            NewRow();

            AddPair(0, "Country (C)", false, _country, "ISO 3166 two-letter code");
            AddPair(2, "Validity (days)", true, _validityDays,
                _role == RolePreset.RootCA ? "Root CAs typically last 10 to 20 years"
                : _role == RolePreset.IntermediateCA ? "Intermediate CAs typically last 5 to 10 years"
                : "Leaf certs typically last 1 year (max 397 days for browser trust)");
            NewRow();

            AddPair(0, "Key algorithm", true, _keyAlg, "RSA-3072 or ECDSA-P256 are good defaults");
            AddPair(2, "", false, new Label { Text = "", AutoSize = true, Visible = false }, "");
            NewRow();

            // ── Subject Alternative Names ──
            AddSection(_role == RolePreset.HttpsServer
                ? "Subject Alternative Names (required for HTTPS / browser trust)"
                : "Subject Alternative Names (optional)");

            var sanInputPanel = new FlowLayoutPanel
            {
                AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false,
                Margin = new Padding(0, 4, 0, 0),
            };
            _sanType.Width = 70;
            _sanInput.Width = 260;
            _sanAdd.Width = 60;
            _sanRm.Width = 70;
            sanInputPanel.Controls.AddRange(new Control[] { _sanType, _sanInput, _sanAdd, _sanRm });

            grid.Controls.Add(new Label { Text = "Add entry", AutoSize = true, Margin = new Padding(0, 8, 0, 0) }, 0, grid.RowCount);
            grid.Controls.Add(sanInputPanel, 1, grid.RowCount);
            grid.SetColumnSpan(sanInputPanel, 3);
            grid.RowCount++;

            _sanList.Width = 540;
            _sanList.Height = 80;
            grid.Controls.Add(new Label { Text = "Entries", AutoSize = true, Margin = new Padding(0, 8, 0, 0) }, 0, grid.RowCount);
            grid.Controls.Add(_sanList, 1, grid.RowCount);
            grid.SetColumnSpan(_sanList, 3);
            grid.RowCount++;

            // ── Issuer ──
            AddSection("Signed by");
            _issuerLabel.AutoSize = true;
            _issuer.Width = 540;
            _issuerHint.Text = "An existing Root or Intermediate certificate. Selected CA must have its private key on this machine.";
            _issuerHint.ForeColor = Color.Gray;
            _issuerHint.Font = new Font(Font.FontFamily, 8F);

            grid.Controls.Add(_issuerLabel, 0, grid.RowCount);
            grid.Controls.Add(_issuer, 1, grid.RowCount);
            grid.SetColumnSpan(_issuer, 3);
            grid.RowCount++;
            grid.Controls.Add(new Label { Text = "" }, 0, grid.RowCount);
            grid.Controls.Add(_issuerHint, 1, grid.RowCount);
            grid.SetColumnSpan(_issuerHint, 3);
            grid.RowCount++;

            // ── Footer ──
            AddSection("Save");
            grid.Controls.Add(new Label
            {
                Text = "Click Save to generate the certificate. After issuing, this form becomes read-only and offers export options.",
                AutoSize = true, ForeColor = Color.FromArgb(100, 100, 100),
                Margin = new Padding(0, 0, 0, 8), MaximumSize = new Size(800, 0),
            }, 0, grid.RowCount);
            grid.SetColumnSpan(grid.GetControlFromPosition(0, grid.RowCount), 4);

            _editForm = grid;
        }

        private void BuildViewForm()
        {
            var grid = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                Padding = new Padding(20, 16, 20, 16),
            };

            void Section(string text)
            {
                grid.Controls.Add(new Label
                {
                    Text = text,
                    Font = new Font(Font.FontFamily, 11F, FontStyle.Bold),
                    ForeColor = Color.FromArgb(50, 80, 120),
                    AutoSize = true,
                    Margin = new Padding(0, 14, 0, 6),
                });
                grid.RowCount++;
            }

            Section("Issued certificate");
            _viewSummary.Font = new Font("Cascadia Code", 9F, FontStyle.Regular);
            _viewSummary.ForeColor = Color.FromArgb(40, 40, 40);
            _viewSummary.MaximumSize = new Size(900, 0);
            grid.Controls.Add(_viewSummary);
            grid.RowCount++;

            Section("Export — certificate only");
            var row1 = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = true };
            foreach (var b in new[] { _exportPemCert, _exportDerCert, _exportCrt })
            {
                b.Width = 200; b.Height = 28; b.Margin = new Padding(0, 0, 8, 0);
                row1.Controls.Add(b);
            }
            grid.Controls.Add(row1);
            grid.RowCount++;

            Section("Export — including private key");
            var row2 = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = true };
            foreach (var b in new[] { _exportPfx, _exportPemKey, _exportPemRsa, _exportBundle })
            {
                b.Width = 230; b.Height = 28; b.Margin = new Padding(0, 0, 8, 6);
                row2.Controls.Add(b);
            }
            grid.Controls.Add(row2);
            grid.RowCount++;

            grid.Controls.Add(new Label
            {
                Text = "PEM RSA (PKCS#1) is only valid for RSA keys; the button errors otherwise.\n"
                     + "PFX / PEM key exports prompt for an output password where applicable.",
                AutoSize = true, ForeColor = Color.Gray, Font = new Font(Font.FontFamily, 8F),
                Margin = new Padding(0, 6, 0, 0),
            });
            grid.RowCount++;

            _viewForm = grid;
        }

        // ── Mode switch ───────────────────────────────────────────────────

        public void FillContent(Item item)
        {
            _item = item;
            _suppress = true;
            try
            {
                var thumb = Get(item, "Thumbprint");
                if (string.IsNullOrEmpty(thumb))
                    ShowEdit();
                else
                    ShowView();
            }
            finally { _suppress = false; }
        }

        public void ClearContent() { _host.Controls.Clear(); }

        public void UpdateItem(Item item)
        {
            if (item == null) return;

            // In view mode, only allow display-name changes.
            var thumb = Get(item, "Thumbprint");
            if (!string.IsNullOrEmpty(thumb))
            {
                if (!string.IsNullOrWhiteSpace(_displayName.Text))
                    item.Name = _displayName.Text.Trim();
                return;
            }

            // Edit mode — write all editable properties.
            if (!string.IsNullOrWhiteSpace(_displayName.Text))
                item.Name = _displayName.Text.Trim();

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
                if (_issuer.SelectedItem is IssuerEntry ie)
                    item.Properties["IssuerThumbprint"] = ie.Thumbprint;
            }
        }

        public string ValidateInput()
        {
            if (string.IsNullOrWhiteSpace(_displayName.Text)) return "Display name is required.";
            if (string.IsNullOrWhiteSpace(_cn.Text)) return "Common Name (CN) is required.";
            if (_validityDays.Value < 1) return "Validity must be at least 1 day.";
            if (_role != RolePreset.RootCA)
            {
                if (!(_issuer.SelectedItem is IssuerEntry))
                    return "Pick an issuing CA.";
            }
            // SAN syntax: each IP must parse, each DNS must look like a hostname.
            foreach (var o in _sanList.Items)
            {
                var s = o.ToString();
                if (s.StartsWith("IP:", StringComparison.OrdinalIgnoreCase))
                {
                    var ipText = s.Substring(3);
                    if (!IPAddress.TryParse(ipText, out _))
                        return $"Invalid IP address in SAN: {ipText}";
                }
                else if (s.StartsWith("DNS:", StringComparison.OrdinalIgnoreCase))
                {
                    var dns = s.Substring(4);
                    if (string.IsNullOrWhiteSpace(dns) || dns.Contains(" "))
                        return $"Invalid DNS name in SAN: {dns}";
                }
            }
            // ISO country code: 2 letters or empty.
            var c = SelectedCountryCode();
            if (!string.IsNullOrEmpty(c) && c.Length != 2)
                return "Country code must be 2 letters (ISO 3166-1 alpha-2).";

            return null;
        }

        // ── Edit mode wiring ─────────────────────────────────────────────

        private void ShowEdit()
        {
            _host.Controls.Clear();
            _host.Controls.Add(_editForm);

            // Defaults from the item (or sensible fallbacks).
            _displayName.Text = _item?.Name ?? "";
            _cn.Text  = Get(_item, "Subject_CN");
            _o.Text   = Get(_item, "Subject_O");
            _ou.Text  = Get(_item, "Subject_OU");
            SelectCountry(Get(_item, "Subject_C"));

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

            // Issuer dropdown: visible only for non-Root.
            bool isRoot = _role == RolePreset.RootCA;
            _issuerLabel.Visible = !isRoot;
            _issuer.Visible = !isRoot;
            _issuerHint.Visible = !isRoot;
            if (!isRoot)
            {
                _issuer.Items.Clear();
                _issuer.Items.Add(IssuerEntry.None);
                foreach (var ca in PkiCertItemManager.FindAllCAs())
                {
                    var thumb = ca.Properties.ContainsKey("Thumbprint") ? ca.Properties["Thumbprint"] : "";
                    if (string.IsNullOrEmpty(thumb)) continue;
                    var pfx = ca.Properties.ContainsKey("EncryptedPfx") ? ca.Properties["EncryptedPfx"] : "";
                    if (string.IsNullOrEmpty(pfx)) continue;   // can't sign without the key
                    _issuer.Items.Add(new IssuerEntry { Name = ca.Name, Thumbprint = thumb });
                }

                var current = Get(_item, "IssuerThumbprint");
                _issuer.SelectedIndex = 0;
                if (!string.IsNullOrEmpty(current))
                {
                    foreach (var o in _issuer.Items)
                        if (o is IssuerEntry ie && ie.Thumbprint == current)
                        {
                            _issuer.SelectedItem = o;
                            break;
                        }
                }
            }
        }

        // ── View mode wiring ─────────────────────────────────────────────

        private void ShowView()
        {
            _host.Controls.Clear();
            _host.Controls.Add(_viewForm);

            _displayName.Text = _item?.Name ?? "";

            string subject = Get(_item, "Subject");
            string issuer  = Get(_item, "Issuer");
            string thumb   = Get(_item, "Thumbprint");
            string serial  = Get(_item, "SerialNumber");
            string nb      = Get(_item, "NotBefore");
            string na      = Get(_item, "NotAfter");
            string ka      = Get(_item, "KeyAlgorithm");
            string san     = Get(_item, "SubjectAlternativeNames");

            _viewSummary.Text =
                $"Role         : {RolePresets.DisplayName(_role)}\n" +
                $"Display name : {_item?.Name}\n" +
                $"Subject      : {subject}\n" +
                $"Issuer       : {issuer}\n" +
                $"Thumbprint   : {thumb}\n" +
                $"Serial       : {serial}\n" +
                $"Not before   : {nb}\n" +
                $"Not after    : {na}\n" +
                $"Key alg      : {ka}\n" +
                $"SAN          : {(string.IsNullOrEmpty(san) ? "(none)" : san.Replace("|", ", "))}";

            // Hide RSA-only key export when the cert isn't RSA.
            _exportPemRsa.Enabled = (ka != null && ka.StartsWith("RSA-"));
        }

        // ── Export ───────────────────────────────────────────────────────

        private void DoExport(string format)
        {
            try
            {
                var pfxB64 = Get(_item, "EncryptedPfx");
                if (string.IsNullOrEmpty(pfxB64))
                {
                    MessageBox.Show("This certificate has no key material on this machine.",
                        "Export", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                var pfx = CertVault.DecryptFromBase64(pfxB64);
                var loaded = Pkcs12Bundle.Load(pfx, "");

                string baseName = SafeFileName(_item.Name ?? "certificate");
                byte[] bytes;
                string filter, defaultExt;

                switch (format)
                {
                    case "pem-cert":
                        bytes = System.Text.Encoding.UTF8.GetBytes(PemIo.WriteCertPem(loaded.Certificate));
                        filter = "PEM (*.pem)|*.pem"; defaultExt = "pem";
                        break;
                    case "der-cert":
                        bytes = loaded.Certificate.GetEncoded();
                        filter = "DER (*.der)|*.der"; defaultExt = "der";
                        break;
                    case "crt":
                        bytes = loaded.Certificate.GetEncoded();
                        filter = "Certificate (*.crt)|*.crt"; defaultExt = "crt";
                        break;
                    case "pfx":
                        var pwd = PromptPassword("PFX password (admin will need this to import the file)", required: false);
                        if (pwd == null) return;
                        bytes = Pkcs12Bundle.Build(loaded.Certificate, loaded.PrivateKey, loaded.ChainExtras, pwd, _item.Name);
                        filter = "PFX (*.pfx)|*.pfx"; defaultExt = "pfx";
                        break;
                    case "pem-key-pkcs8":
                        var pwd2 = PromptPassword("PEM key password (leave empty for unencrypted)", required: false);
                        if (pwd2 == null) return;
                        bytes = System.Text.Encoding.UTF8.GetBytes(PemIo.WritePrivateKeyPemPkcs8(loaded.PrivateKey, pwd2));
                        filter = "PEM (*.pem;*.key)|*.pem;*.key"; defaultExt = "pem";
                        break;
                    case "pem-key-pkcs1":
                        bytes = System.Text.Encoding.UTF8.GetBytes(PemIo.WriteRsaPrivateKeyPemPkcs1(loaded.PrivateKey));
                        filter = "PEM (*.pem;*.key)|*.pem;*.key"; defaultExt = "pem";
                        break;
                    case "pem-bundle":
                        var sb = new System.Text.StringBuilder();
                        sb.Append(PemIo.WriteCertPem(loaded.Certificate));
                        sb.Append(PemIo.WritePrivateKeyPemPkcs8(loaded.PrivateKey, ""));
                        bytes = System.Text.Encoding.UTF8.GetBytes(sb.ToString());
                        filter = "PEM (*.pem)|*.pem"; defaultExt = "pem";
                        break;
                    default: return;
                }

                using (var dlg = new SaveFileDialog
                {
                    FileName = baseName + "." + defaultExt,
                    DefaultExt = defaultExt,
                    Filter = filter,
                    AddExtension = true,
                })
                {
                    if (dlg.ShowDialog() == DialogResult.OK)
                    {
                        File.WriteAllBytes(dlg.FileName, bytes);
                        MessageBox.Show($"Exported to:\n{dlg.FileName}", "Export",
                            MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Export failed:\n\n" + ex.Message,
                    "PKI", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ── helpers ──────────────────────────────────────────────────────

        private string SelectedCountryCode()
        {
            return (_country.SelectedItem as Iso3166Codes.Entry)?.Code ?? "";
        }

        private void SelectCountry(string code)
        {
            for (int i = 0; i < _country.Items.Count; i++)
            {
                if (_country.Items[i] is Iso3166Codes.Entry e
                    && string.Equals(e.Code, code ?? "", StringComparison.OrdinalIgnoreCase))
                {
                    _country.SelectedIndex = i;
                    return;
                }
            }
            _country.SelectedIndex = 0;
        }

        private static string Get(Item item, string key)
            => (item != null && item.Properties.ContainsKey(key)) ? item.Properties[key] : "";

        private static string SafeFileName(string s)
        {
            foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s;
        }

        private static string PromptPassword(string label, bool required)
        {
            using (var dlg = new Form
            {
                Text = "PKI export",
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MinimizeBox = false, MaximizeBox = false,
                Width = 460, Height = 170,
            })
            {
                var lbl = new Label { Text = label, AutoSize = true, Left = 16, Top = 16, Width = 420 };
                var txt = new TextBox { Left = 16, Top = 50, Width = 420, UseSystemPasswordChar = true };
                var ok  = new Button { Text = "OK",     Left = 260, Top = 90, Width = 80, DialogResult = DialogResult.OK };
                var cn  = new Button { Text = "Cancel", Left = 350, Top = 90, Width = 80, DialogResult = DialogResult.Cancel };
                dlg.Controls.AddRange(new Control[] { lbl, txt, ok, cn });
                dlg.AcceptButton = ok; dlg.CancelButton = cn;
                if (dlg.ShowDialog() != DialogResult.OK) return null;
                if (required && string.IsNullOrEmpty(txt.Text))
                {
                    MessageBox.Show("Password is required.", "PKI",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return null;
                }
                return txt.Text;
            }
        }

        private class IssuerEntry
        {
            public string Name;
            public string Thumbprint;
            public override string ToString() => $"{Name}  ({Thumbprint?.Substring(0, Math.Min(8, Thumbprint?.Length ?? 0))}…)";
            public static readonly IssuerEntry None = new NoneEntry();
            private class NoneEntry : IssuerEntry
            {
                public NoneEntry() { Name = "(select an issuing CA)"; Thumbprint = ""; }
                public override string ToString() => Name;
            }
        }
    }
}
