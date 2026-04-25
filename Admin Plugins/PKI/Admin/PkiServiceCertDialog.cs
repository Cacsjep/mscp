using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using PKI.Crypto;
using VideoOS.Platform;

namespace PKI.Admin
{
    // Modal launched from the Overview pane. Discovery runs once on open.
    // Admin ticks one or more rows (Select all / none toolbar above the
    // grid), picks an issuing CA, validity, key alg, then Generate iterates
    // the checked rows and emits one cert per service into PkiServiceKindId.
    //
    // Each cert's SAN includes:
    //   - the configured hostname (DNS)
    //   - the FQDN if reverse-resolution found one (DNS)
    //   - the short name when the configured hostname was dotted (DNS)
    //   - every non-loopback IPv4 / IPv6 we resolved (IP)
    public class PkiServiceCertDialog : Form
    {
        private List<DiscoveredService> _services = new List<DiscoveredService>();
        private List<Item> _cas = new List<Item>();

        private readonly DataGridView _grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            BackgroundColor = SystemColors.Control,
            BorderStyle = BorderStyle.None,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
        };

        private readonly Button _selectAll  = new Button { Text = "Select all",  Width = 90, Height = 26 };
        private readonly Button _selectNone = new Button { Text = "Select none", Width = 100, Height = 26 };

        private readonly ComboBox _issuer   = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 300 };
        private readonly NumericUpDown _validity = new NumericUpDown { Minimum = 1, Maximum = 3650, Value = 397, Width = 80 };
        private readonly ComboBox _keyAlg   = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 130 };
        private readonly Label    _summary  = new Label { AutoSize = true, ForeColor = Color.FromArgb(110, 110, 110) };
        private readonly Button   _generate = new Button { Text = "Generate", Width = 130, Height = 32 };
        private readonly Button   _cancel   = new Button { Text = "Cancel",   Width = 100, Height = 32 };

        public PkiServiceCertDialog()
        {
            Text = "Generate service certificates";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimizeBox = false;
            MaximizeBox = true;
            MinimumSize = new Size(900, 580);
            ClientSize = new Size(960, 620);

            BuildLayout();
            BuildGridColumns();

            _selectAll.Click  += (s, e) => SetAllChecked(true);
            _selectNone.Click += (s, e) => SetAllChecked(false);
            _generate.Click   += (s, e) => OnGenerate();
            _cancel.Click     += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            AcceptButton = _generate;
            CancelButton = _cancel;

            Load += (s, e) => RunDiscovery();
        }

        // ── Layout ───────────────────────────────────────────────────────

        private void BuildLayout()
        {
            // One root TableLayoutPanel: heading / toolbar / grid / form /
            // summary / buttons. Each row claims a single explicit height
            // so the bottom button strip is always reserved at 56px and
            // never gets clipped.
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6,
                Padding = new Padding(20, 16, 20, 16),
            };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));        // heading
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));        // toolbar (select all/none)
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));     // grid
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));        // form strip
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));        // summary
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));     // buttons

            var heading = new Label
            {
                Text = "Pick the Milestone services to issue TLS certificates for. Hostname + IPs go into the SAN automatically.",
                AutoSize = true, ForeColor = Color.FromArgb(110, 110, 110),
                Margin = new Padding(0, 0, 0, 10),
            };
            root.Controls.Add(heading, 0, 0);

            var toolbar = new FlowLayoutPanel
            {
                AutoSize = true, FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false, Margin = new Padding(0, 0, 0, 6),
            };
            _selectAll.Margin  = new Padding(0, 0, 6, 0);
            _selectNone.Margin = new Padding(0, 0, 0, 0);
            toolbar.Controls.Add(_selectAll);
            toolbar.Controls.Add(_selectNone);
            root.Controls.Add(toolbar, 0, 1);

            root.Controls.Add(_grid, 0, 2);

            // Form strip: Issuing CA / Validity / Key alg.
            var form = new TableLayoutPanel
            {
                Dock = DockStyle.Top, AutoSize = true, ColumnCount = 6,
                Padding = new Padding(0, 12, 0, 0),
            };
            for (int i = 0; i < 5; i++)
                form.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            void Cell(int c, Control ctl, int padRight)
            {
                ctl.Margin = new Padding(0, 0, padRight, 0);
                form.Controls.Add(ctl, c, 0);
            }
            Cell(0, new Label { Text = "Issuing CA",     AutoSize = true, Margin = new Padding(0, 6, 8, 0) }, 8);
            Cell(1, _issuer, 16);
            Cell(2, new Label { Text = "Validity (days)", AutoSize = true, Margin = new Padding(0, 6, 8, 0) }, 8);
            Cell(3, _validity, 16);
            Cell(4, new Label { Text = "Key",            AutoSize = true, Margin = new Padding(0, 6, 8, 0) }, 8);
            Cell(5, _keyAlg, 0);

            _keyAlg.Items.AddRange(new object[] { "RSA-2048", "RSA-3072", "RSA-4096", "ECDSA-P256", "ECDSA-P384" });
            _keyAlg.SelectedIndex = 0;

            root.Controls.Add(form, 0, 3);

            _summary.Margin = new Padding(0, 10, 0, 6);
            root.Controls.Add(_summary, 0, 4);

            // Bottom button strip - fixed height, both buttons absolutely
            // anchored so they always render flush against the right edge.
            var buttonStrip = new Panel { Dock = DockStyle.Fill };
            _generate.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _cancel.Anchor   = AnchorStyles.Top | AnchorStyles.Right;
            buttonStrip.Controls.Add(_generate);
            buttonStrip.Controls.Add(_cancel);
            buttonStrip.Resize += (s, e) =>
            {
                int top = 12;
                _cancel.Location   = new Point(buttonStrip.ClientSize.Width - _cancel.Width, top);
                _generate.Location = new Point(_cancel.Location.X - 8 - _generate.Width, top);
            };
            // Initial layout in case Resize doesn't fire before paint.
            _cancel.Location   = new Point(360, 12);
            _generate.Location = new Point(220, 12);
            root.Controls.Add(buttonStrip, 0, 5);

            Controls.Add(root);
        }

        private void BuildGridColumns()
        {
            _grid.Columns.Add(new DataGridViewCheckBoxColumn
            {
                HeaderText = "", Name = "Pick", FillWeight = 28, MinimumWidth = 30,
            });
            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "Type", Name = "Type", FillWeight = 110, MinimumWidth = 110, ReadOnly = true,
            });
            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "Display name", Name = "Display", FillWeight = 180, MinimumWidth = 140, ReadOnly = true,
            });
            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "Hostname", Name = "Host", FillWeight = 180, MinimumWidth = 140, ReadOnly = true,
            });
            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "IPs (v4 + v6)", Name = "Ips", FillWeight = 280, MinimumWidth = 200, ReadOnly = true,
            });
            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "Cert name", Name = "CertName", FillWeight = 180, MinimumWidth = 140, ReadOnly = true,
            });
        }

        // ── Discovery + grid population ─────────────────────────────────

        private void RunDiscovery()
        {
            _summary.Text = "Discovering services...";
            _services = ServiceDiscovery.Discover();
            _cas = PkiCertItemManager.FindAllCAs()
                .Where(c => !string.IsNullOrEmpty(GetProp(c, "Thumbprint")))
                .ToList();

            _grid.Rows.Clear();
            foreach (var svc in _services)
            {
                var hostDisplay = string.IsNullOrEmpty(svc.Fqdn) || string.Equals(svc.Fqdn, svc.Hostname, StringComparison.OrdinalIgnoreCase)
                    ? svc.Hostname
                    : $"{svc.Hostname} ({svc.Fqdn})";

                var v4 = svc.IpAddresses.Where(ip => !ip.Contains(":")).ToList();
                var v6 = svc.IpAddresses.Where(ip =>  ip.Contains(":")).ToList();
                var ipDisplay = string.Join(", ", v4.Concat(v6));

                var rowIdx = _grid.Rows.Add(
                    false,
                    svc.CategoryLabel,
                    svc.DisplayName,
                    hostDisplay,
                    ipDisplay,
                    svc.SuggestedCertName);
                _grid.Rows[rowIdx].Tag = svc;
            }

            _issuer.Items.Clear();
            foreach (var ca in _cas)
            {
                var label = ca.Name + " (" + ExtractCn(GetProp(ca, "Subject")) + ")";
                _issuer.Items.Add(new IssuerEntry { Item = ca, Display = label });
            }
            if (_issuer.Items.Count > 0) _issuer.SelectedIndex = 0;

            _summary.Text = $"Found {_services.Count} service(s). Available CAs: {_cas.Count}.";
            _generate.Enabled = _services.Count > 0 && _cas.Count > 0;
            if (_cas.Count == 0)
                _summary.Text += "  Issue a Root or Intermediate CA before generating service certs.";
        }

        private void SetAllChecked(bool value)
        {
            foreach (DataGridViewRow row in _grid.Rows)
                row.Cells["Pick"].Value = value;
            // Commit immediately so the bool state is visible to OnGenerate.
            _grid.EndEdit();
        }

        // ── Generate ────────────────────────────────────────────────────

        private void OnGenerate()
        {
            var issuer = _issuer.SelectedItem as IssuerEntry;
            if (issuer == null)
            {
                MessageBox.Show("Pick an issuing CA.", "Generate",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _grid.EndEdit();

            var checkedSvcs = new List<DiscoveredService>();
            foreach (DataGridViewRow row in _grid.Rows)
            {
                var pick = row.Cells["Pick"].Value;
                if (pick is bool b && b) checkedSvcs.Add((DiscoveredService)row.Tag);
            }
            if (checkedSvcs.Count == 0)
            {
                MessageBox.Show("Tick at least one service.", "Generate",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            int validityDays = (int)_validity.Value;
            var keyAlg = KeyPairFactory.Parse(_keyAlg.SelectedItem?.ToString() ?? "RSA-2048");

            int ok = 0, fail = 0;
            var errors = new List<string>();

            UseWaitCursor = true;
            _generate.Enabled = false;
            _cancel.Enabled = false;

            try
            {
                foreach (var svc in checkedSvcs)
                {
                    var dnsNames = new List<string> { svc.Hostname };
                    if (!string.IsNullOrEmpty(svc.Fqdn)
                        && !dnsNames.Contains(svc.Fqdn, StringComparer.OrdinalIgnoreCase))
                    {
                        dnsNames.Add(svc.Fqdn);
                    }
                    if (svc.Hostname.Contains("."))
                    {
                        var shortName = svc.Hostname.Split('.')[0];
                        if (!dnsNames.Contains(shortName, StringComparer.OrdinalIgnoreCase))
                            dnsNames.Add(shortName);
                    }

                    var req = new ServiceCertRequest
                    {
                        DestinationKind = PKIDefinition.PkiServiceKindId,
                        Role            = RolePreset.Service,
                        ItemName        = svc.SuggestedCertName,
                        CommonName      = svc.Hostname,
                        DnsNames        = dnsNames,
                        IpAddresses     = svc.IpAddresses,
                        IssuingCA       = issuer.Item,
                        ValidityDays    = validityDays,
                        KeyAlgorithm    = keyAlg,
                    };

                    var res = ServiceCertIssuer.Issue(req);
                    if (res.Ok) ok++;
                    else { fail++; errors.Add($"{svc.SuggestedCertName}: {res.Message}"); }
                }
            }
            finally
            {
                UseWaitCursor = false;
                _generate.Enabled = true;
                _cancel.Enabled = true;
            }

            var msg = $"Generated {ok} certificate(s).";
            if (fail > 0) msg += $"\nFailed: {fail}\n\n" + string.Join("\n", errors);
            MessageBox.Show(msg, "Service certificates",
                MessageBoxButtons.OK,
                fail == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);

            if (ok > 0)
            {
                DialogResult = DialogResult.OK;
                Close();
            }
        }

        // ── Helpers ─────────────────────────────────────────────────────

        private static string GetProp(Item item, string key)
            => (item != null && item.Properties.ContainsKey(key)) ? item.Properties[key] : "";

        private static string ExtractCn(string dn)
        {
            if (string.IsNullOrEmpty(dn)) return "";
            foreach (var part in dn.Split(','))
            {
                var p = part.Trim();
                if (p.StartsWith("CN=", StringComparison.OrdinalIgnoreCase))
                    return p.Substring(3).Trim();
            }
            return dn;
        }

        private class IssuerEntry
        {
            public Item Item;
            public string Display;
            public override string ToString() => Display;
        }
    }
}
