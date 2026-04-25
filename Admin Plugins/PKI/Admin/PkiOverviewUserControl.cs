using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using PKI.Crypto;
using VideoOS.Platform;

namespace PKI.Admin
{
    // The Overview pane: lists every cert across the five leaf folders in a
    // single sortable grid. Action bar at the top exposes Import and
    // Refresh. Status column flags expired, expiring soon, and ok states
    // so admins can see issues at a glance.
    public class PkiOverviewUserControl : UserControl
    {
        private readonly Label _heading = new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI", 14F, FontStyle.Bold),
            ForeColor = Color.FromArgb(50, 80, 120),
            Margin = new Padding(0, 0, 0, 4),
        };
        private readonly Label _subHeading = new Label
        {
            AutoSize = true,
            ForeColor = Color.FromArgb(110, 110, 110),
            Margin = new Padding(0, 0, 0, 12),
        };
        private readonly Button _import   = new Button { Text = "Import certificate...",            Width = 200, Height = 32 };
        private readonly Button _genServices = new Button { Text = "Generate service certificates...", Width = 240, Height = 32 };
        private readonly Button _export   = new Button { Text = "Export...",                        Width = 100, Height = 32, Enabled = false };
        private readonly Button _delete   = new Button { Text = "Delete",                           Width = 90,  Height = 32, Enabled = false };
        private readonly Button _refresh  = new Button { Text = "Refresh",                          Width = 100, Height = 32 };
        private ContextMenuStrip _exportMenu;
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
            ReadOnly = true,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
        };

        public PkiOverviewUserControl()
        {
            Dock = DockStyle.Fill;
            AutoScroll = false;
            BuildLayout();
            BuildGridColumns();
            BuildExportMenu();
            _import.Click       += (s, e) => OnImportClick();
            _genServices.Click  += (s, e) => OnGenerateServiceClick();
            _export.Click       += (s, e) => OnExportClick();
            _delete.Click       += (s, e) => OnDeleteClick();
            _refresh.Click      += (s, e) => Refresh();
            _grid.SelectionChanged += (s, e) => UpdateRowActions();
        }

        private void BuildLayout()
        {
            var top = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1, RowCount = 3,
                Padding = new Padding(20, 16, 20, 16),
            };
            top.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            top.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            top.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            _heading.Text = "Certificate vault";
            top.Controls.Add(_heading, 0, 0);

            // Header strip: subheading on the left, Import + Refresh on the right.
            var headerStrip = new TableLayoutPanel
            {
                ColumnCount = 2, RowCount = 1, Dock = DockStyle.Top, AutoSize = true,
            };
            headerStrip.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            headerStrip.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            headerStrip.Controls.Add(_subHeading, 0, 0);

            var actions = new FlowLayoutPanel
            {
                AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false,
            };
            _import.Margin       = new Padding(0, 0, 8, 0);
            _genServices.Margin  = new Padding(0, 0, 8, 0);
            _export.Margin       = new Padding(0, 0, 8, 0);
            _delete.Margin       = new Padding(0, 0, 8, 0);
            _refresh.Margin      = new Padding(0, 0, 0, 0);
            actions.Controls.Add(_import);
            actions.Controls.Add(_genServices);
            actions.Controls.Add(_export);
            actions.Controls.Add(_delete);
            actions.Controls.Add(_refresh);
            headerStrip.Controls.Add(actions, 1, 0);

            top.Controls.Add(headerStrip, 0, 1);
            top.Controls.Add(_grid, 0, 2);

            Controls.Add(top);
        }

        private void BuildGridColumns()
        {
            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "Status", Name = "Status", FillWeight = 60, MinimumWidth = 80,
            });
            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "Display name", Name = "Display", FillWeight = 200, MinimumWidth = 160,
            });
            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "Folder", Name = "Folder", FillWeight = 130, MinimumWidth = 130,
            });
            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "Issuer", Name = "Issuer", FillWeight = 200, MinimumWidth = 160,
            });
            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "Valid until", Name = "NotAfter", FillWeight = 110, MinimumWidth = 110,
            });
            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "Remaining", Name = "Remaining", FillWeight = 100, MinimumWidth = 100,
            });
            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "Has key", Name = "HasKey", FillWeight = 60, MinimumWidth = 60,
            });
        }

        // ── Data load ────────────────────────────────────────────────────

        public new void Refresh()
        {
            try
            {
                _grid.Rows.Clear();
                int total = 0, expired = 0, soon = 0, noKey = 0;

                foreach (var (kind, label) in EnumerateLeafKinds())
                {
                    var items = Configuration.Instance.GetItemConfigurations(PKIDefinition.PluginId, null, kind);
                    if (items == null) continue;
                    foreach (var item in items)
                    {
                        if (string.IsNullOrEmpty(GetProp(item, "Thumbprint"))) continue; // not yet issued
                        total++;

                        var status = ComputeStatus(GetProp(item, "NotAfter"));
                        if (status == "Expired") expired++;
                        else if (status == "Expires soon") soon++;
                        var hasKey = string.Equals(GetProp(item, "HasPrivateKey"), "True", StringComparison.OrdinalIgnoreCase);
                        if (!hasKey) noKey++;

                        var rowIdx = _grid.Rows.Add(
                            status,
                            item.Name,
                            label,
                            FriendlyIssuer(item),
                            FormatDate(GetProp(item, "NotAfter")),
                            FormatRemaining(GetProp(item, "NotAfter")),
                            hasKey ? "yes" : "no");
                        var row = _grid.Rows[rowIdx];
                        row.Tag = new RowItem { Item = item, Kind = kind };

                        if (status == "Expired")
                            row.DefaultCellStyle.BackColor = Color.FromArgb(255, 235, 235);
                        else if (status == "Expires soon")
                            row.DefaultCellStyle.BackColor = Color.FromArgb(255, 248, 220);
                    }
                }

                _subHeading.Text = $"{total} certificate{(total == 1 ? "" : "s")} stored. "
                                 + $"{expired} expired, {soon} expiring within 30 days, {noKey} without private key.";
            }
            catch (Exception ex)
            {
                _subHeading.Text = "Failed to load: " + ex.Message;
                _subHeading.ForeColor = Color.FromArgb(160, 30, 30);
            }
        }

        private static string ComputeStatus(string notAfterIso)
        {
            if (string.IsNullOrEmpty(notAfterIso)) return "?";
            if (!DateTime.TryParse(notAfterIso, CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out var na)) return "?";
            var now = DateTime.UtcNow;
            if (na < now) return "Expired";
            if (na < now.AddDays(30)) return "Expires soon";
            return "OK";
        }

        private static string FormatDate(string iso)
        {
            if (string.IsNullOrEmpty(iso)) return "";
            if (!DateTime.TryParse(iso, CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out var dt)) return iso;
            return dt.ToUniversalTime().ToString("yyyy-MM-dd");
        }

        // Remaining days. Negative values mean already-expired.
        private static string FormatRemaining(string iso)
        {
            if (string.IsNullOrEmpty(iso)) return "";
            if (!DateTime.TryParse(iso, CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out var dt)) return "";
            var days = (int)Math.Round((dt.ToUniversalTime() - DateTime.UtcNow).TotalDays);
            if (days < 0) return $"expired {(-days)}d ago";
            if (days == 0) return "expires today";
            if (days == 1) return "1 day";
            if (days < 365) return days + " days";
            int years = days / 365;
            int rem = days % 365;
            return rem == 0 ? years + "y" : $"{years}y {rem}d";
        }

        private static IEnumerable<(Guid kind, string label)> EnumerateLeafKinds()
        {
            yield return (PKIDefinition.PkiRootCertKindId,     "Root CA");
            yield return (PKIDefinition.PkiIntermediateKindId, "Intermediate CA");
            yield return (PKIDefinition.PkiHttpsKindId,        "HTTPS");
            yield return (PKIDefinition.PkiDot1xKindId,        "802.1X");
            yield return (PKIDefinition.PkiServiceKindId,      "Service");
        }

        private static string GetProp(Item item, string key)
            => (item != null && item.Properties.ContainsKey(key)) ? item.Properties[key] : "";

        // Short, human-readable issuer for the grid. Prefers the Display
        // Name of the issuing item (when the issuer is one of our own CAs);
        // otherwise falls back to the CN value pulled from the Issuer DN.
        // Self-signed certs render as "self-signed" so admins can spot Roots
        // at a glance.
        private static string FriendlyIssuer(Item item)
        {
            var issuerDn = GetProp(item, "Issuer");
            var subjectDn = GetProp(item, "Subject");
            if (!string.IsNullOrEmpty(issuerDn) && issuerDn == subjectDn) return "self-signed";

            var thumb = GetProp(item, "IssuerThumbprint");
            if (!string.IsNullOrEmpty(thumb))
            {
                foreach (var (kind, _) in EnumerateLeafKinds())
                {
                    var candidates = Configuration.Instance.GetItemConfigurations(PKIDefinition.PluginId, null, kind);
                    if (candidates == null) continue;
                    foreach (var cand in candidates)
                    {
                        if (string.Equals(GetProp(cand, "Thumbprint"), thumb, StringComparison.OrdinalIgnoreCase))
                            return cand.Name;
                    }
                }
            }
            return ExtractCn(issuerDn);
        }

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

        // ── Actions ──────────────────────────────────────────────────────

        private void OnImportClick()
        {
            using (var dlg = new PkiImportDialog())
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                    Refresh();
            }
        }

        private void OnGenerateServiceClick()
        {
            using (var dlg = new PkiServiceCertDialog())
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                    Refresh();
            }
        }

        private void UpdateRowActions()
        {
            var hasRow = _grid.SelectedRows.Count == 1
                && _grid.SelectedRows[0].Tag is RowItem;
            _delete.Enabled = hasRow;
            _export.Enabled = hasRow;
        }

        private void BuildExportMenu()
        {
            // One drop-down lists every export format. Key-bearing items
            // are surfaced regardless of HasPrivateKey; CertExporter
            // refuses with a friendly message for cert-only items.
            _exportMenu = new ContextMenuStrip();
            void Add(string text, string format)
            {
                var mi = new ToolStripMenuItem(text);
                mi.Click += (s, e) =>
                {
                    var ri = SelectedRowItem();
                    if (ri != null) CertExporter.ExportInteractive(ri.Item, format, this);
                };
                _exportMenu.Items.Add(mi);
            }
            Add("PEM (.pem) - cert only",         CertExporter.Format.PemCert);
            Add("DER (.der) - cert only",         CertExporter.Format.DerCert);
            Add(".crt - cert only",               CertExporter.Format.Crt);
            _exportMenu.Items.Add(new ToolStripSeparator());
            Add("PFX (cert + key)...",            CertExporter.Format.Pfx);
            Add("PEM PKCS#8 key...",              CertExporter.Format.PemKeyPkcs8);
            Add("PEM PKCS#1 RSA key (RSA only)",  CertExporter.Format.PemKeyPkcs1);
            Add("PEM bundle (cert + key)",        CertExporter.Format.PemBundle);
        }

        private void OnExportClick()
        {
            if (SelectedRowItem() == null) return;
            _exportMenu.Show(_export, new Point(0, _export.Height));
        }

        private RowItem SelectedRowItem()
        {
            if (_grid.SelectedRows.Count != 1) return null;
            return _grid.SelectedRows[0].Tag as RowItem;
        }

        private void OnDeleteClick()
        {
            if (_grid.SelectedRows.Count != 1) return;
            if (!(_grid.SelectedRows[0].Tag is RowItem ri)) return;

            var item = ri.Item;
            var thumb = GetProp(item, "Thumbprint");

            // Block delete if other certs reference this one as their issuer.
            // Same rule the per-folder ItemManager enforces; reproduced here
            // so admins get the same protection from the Overview pane.
            if (!string.IsNullOrEmpty(thumb))
            {
                var dependents = PkiCertItemManager.FindCertsIssuedBy(thumb);
                if (dependents.Count > 0)
                {
                    var names = string.Join("\n  - ", dependents);
                    MessageBox.Show(
                        "This certificate has issued other certificates and cannot be deleted.\n\n"
                        + "Dependent certificates:\n  - " + names
                        + "\n\nDelete or re-issue those first.",
                        "Delete blocked",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }

            var confirm = MessageBox.Show(
                $"Delete certificate \"{item.Name}\"?\n\nThis removes the cert and its private key from this server.",
                "Delete certificate",
                MessageBoxButtons.OKCancel, MessageBoxIcon.Warning);
            if (confirm != DialogResult.OK) return;

            try
            {
                Configuration.Instance.DeleteItemConfiguration(PKIDefinition.PluginId, item);
                PKIDefinition.Log.Info($"Deleted cert from Overview: {item.Name} (thumbprint {thumb})");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Delete failed:\n\n" + ex.Message, "PKI",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            Refresh();
        }

        private class RowItem
        {
            public Item Item;
            public Guid Kind;
        }
    }
}
