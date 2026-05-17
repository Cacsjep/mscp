using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Pkcs;
using Org.BouncyCastle.X509;
using PKI.Crypto;
using PKI.Storage;
using VideoOS.Platform;

namespace PKI.Admin
{
    // Modal "Sign CSR" wizard launched from the view-mode card of a Root
    // CA or Intermediate CA cert. Loads a PEM/DER CSR from disk, lets the
    // admin pick a target role, validity, and SAN list, then issues the
    // certificate using the CA's private key and writes it to disk in the
    // chosen format. Public-key only - the CSR's submitter keeps their
    // private key, exactly the standard CSR workflow.
    public class PkiSignCsrDialog : Form
    {
        private readonly Item _issuerItem;
        private readonly X509Certificate _issuerCert;
        private readonly AsymmetricKeyParameter _issuerKey;

        private readonly TextBox _csrFile = new TextBox { Width = 360, ReadOnly = true };
        private readonly Button _csrBrowse = new Button { Text = "Browse...", Width = 80 };

        private readonly Label _csrSummary = new Label
        {
            AutoSize = true, ForeColor = Color.Gray,
            Font = new Font("Cascadia Code", 9F, FontStyle.Regular),
            MaximumSize = new Size(700, 0),
        };

        private readonly ComboBox _role = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList, Width = 280,
        };
        private readonly NumericUpDown _validityDays = new NumericUpDown
        {
            Minimum = 1, Maximum = 30 * 365, Value = RolePresets.DefaultLeafValidityDays, Width = 100,
        };

        private readonly ListBox _sanList = new ListBox { Width = 460, Height = 110 };
        private readonly ComboBox _sanType = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList, Width = 70,
        };
        private readonly TextBox _sanInput = new TextBox { Width = 280 };
        private readonly Button _sanAdd = new Button { Text = "Add", Width = 60 };
        private readonly Button _sanRm = new Button { Text = "Remove", Width = 80 };
        private readonly Button _sanFill = new Button { Text = "From CSR", Width = 90 };

        private readonly ComboBox _outFormat = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList, Width = 200,
        };

        private readonly Button _sign = new Button { Text = "Sign and save...", Width = 160, Height = 30 };
        private readonly Button _cancel = new Button { Text = "Cancel", Width = 100, Height = 30 };

        private Pkcs10CertificationRequest _parsedCsr;

        public PkiSignCsrDialog(Item issuerItem, X509Certificate issuerCert, AsymmetricKeyParameter issuerKey)
        {
            _issuerItem = issuerItem;
            _issuerCert = issuerCert;
            _issuerKey = issuerKey;

            Text = "Sign CSR with " + (issuerItem?.Name ?? "CA");
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = false;
            MaximizeBox = false;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            MinimumSize = new Size(820, 620);

            Build();
            Wire();
            UpdateSignEnabled();
        }

        private void Build()
        {
            var grid = new TableLayoutPanel
            {
                Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 3, Padding = new Padding(20, 16, 20, 16),
            };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            void Section(string text)
            {
                var lbl = new Label
                {
                    Text = text, Font = new Font(Font.FontFamily, 11F, FontStyle.Bold),
                    ForeColor = Color.FromArgb(50, 80, 120), AutoSize = true,
                    Margin = new Padding(0, 14, 0, 6),
                };
                grid.Controls.Add(lbl, 0, grid.RowCount);
                grid.SetColumnSpan(lbl, 3);
                grid.RowCount++;
            }

            void Row(string label, params Control[] inputs)
            {
                grid.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(0, 8, 0, 0) }, 0, grid.RowCount);
                var row = new FlowLayoutPanel
                {
                    AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false,
                    Margin = new Padding(0, 4, 0, 0), Padding = Padding.Empty,
                };
                for (int i = 0; i < inputs.Length; i++)
                {
                    inputs[i].Margin = new Padding(0, 0, i == inputs.Length - 1 ? 0 : 6, 0);
                    row.Controls.Add(inputs[i]);
                }
                grid.Controls.Add(row, 1, grid.RowCount);
                grid.SetColumnSpan(row, 2);
                grid.RowCount++;
            }

            Section("CSR file");
            Row("CSR (PEM/DER)", _csrFile, _csrBrowse);
            grid.Controls.Add(_csrSummary, 1, grid.RowCount);
            grid.SetColumnSpan(_csrSummary, 2);
            grid.RowCount++;

            Section("Issued certificate");
            // Target role: leaves + intermediate. We deliberately exclude
            // RootCA - signing a self-issued root via someone else's CA
            // makes no sense.
            _role.Items.AddRange(new object[]
            {
                new RoleEntry(RolePreset.HttpsServer,    "HTTPS server"),
                new RoleEntry(RolePreset.Dot1xClient,    "802.1X client"),
                new RoleEntry(RolePreset.Service,        "Service"),
                new RoleEntry(RolePreset.IntermediateCA, "Intermediate CA"),
            });
            _role.DisplayMember = "Display";
            _role.SelectedIndex = 0;
            Row("Role", _role);
            Row("Validity (days)", _validityDays);

            Section("Subject Alternative Names");
            grid.Controls.Add(new Label
            {
                Text = "Pre-filled from the CSR's extensionRequest. Edit before signing if needed.",
                AutoSize = true, ForeColor = Color.Gray, Font = new Font(Font.FontFamily, 8F),
                Margin = new Padding(0, 0, 0, 4),
            }, 1, grid.RowCount);
            grid.SetColumnSpan(grid.GetControlFromPosition(1, grid.RowCount), 2);
            grid.RowCount++;

            _sanType.Items.AddRange(new object[] { "DNS", "IP" });
            _sanType.SelectedIndex = 0;
            Row("Add entry", _sanType, _sanInput, _sanAdd);

            grid.Controls.Add(new Label { Text = "Entries", AutoSize = true, Margin = new Padding(0, 8, 0, 0) }, 0, grid.RowCount);
            var listRow = new FlowLayoutPanel
            {
                AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false,
                Margin = new Padding(0, 4, 0, 0),
            };
            _sanList.Margin = new Padding(0, 0, 6, 0);
            listRow.Controls.Add(_sanList);
            var sideButtons = new FlowLayoutPanel
            {
                AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false,
            };
            _sanRm.Margin = new Padding(0, 0, 0, 6);
            _sanFill.Margin = new Padding(0, 0, 0, 0);
            sideButtons.Controls.Add(_sanRm);
            sideButtons.Controls.Add(_sanFill);
            listRow.Controls.Add(sideButtons);
            grid.Controls.Add(listRow, 1, grid.RowCount);
            grid.SetColumnSpan(listRow, 2);
            grid.RowCount++;

            Section("Output format");
            _outFormat.Items.AddRange(new object[]
            {
                "PEM (.pem) - signed cert only",
                "PEM bundle (.pem) - signed cert + issuer chain",
                "DER (.der)",
                ".crt",
            });
            _outFormat.SelectedIndex = 0;
            Row("Format", _outFormat);

            Controls.Add(grid);

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(20, 12, 20, 16), Height = 60,
            };
            _cancel.Margin = new Padding(0, 0, 0, 0);
            _sign.Margin = new Padding(0, 0, 8, 0);
            buttons.Controls.Add(_cancel);
            buttons.Controls.Add(_sign);
            Controls.Add(buttons);
            AcceptButton = _sign;
            CancelButton = _cancel;
        }

        private void Wire()
        {
            _csrBrowse.Click += (s, e) => OnBrowseCsr();
            _sanAdd.Click += (s, e) => OnSanAdd();
            _sanRm.Click += (s, e) => OnSanRemove();
            _sanFill.Click += (s, e) => OnSanFillFromCsr();
            _role.SelectedIndexChanged += (s, e) => OnRoleChanged();
            _sign.Click += (s, e) => OnSign();
            _cancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
        }

        // ── CSR loading ─────────────────────────────────────────────────────

        private void OnBrowseCsr()
        {
            using (var dlg = new OpenFileDialog
            {
                Title = "Select CSR file",
                Filter = "CSR (PEM/DER)|*.csr;*.req;*.pem;*.p10|All files|*.*",
            })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                _csrFile.Text = dlg.FileName;
                LoadCsr(dlg.FileName);
            }
        }

        private void LoadCsr(string path)
        {
            _parsedCsr = null;
            _csrSummary.ForeColor = Color.Gray;
            _csrSummary.Text = "";
            try
            {
                var bytes = File.ReadAllBytes(path);
                var csr = PemIo.ReadCsrAuto(bytes);
                if (!csr.Verify())
                    throw new InvalidOperationException("CSR signature is invalid - the file may be corrupt.");
                _parsedCsr = csr;

                var info = csr.GetCertificationRequestInfo();
                var subject = info.Subject?.ToString() ?? "(empty)";
                var keyLabel = KeyPairFactory.Label(csr.GetPublicKey());
                var sb = new StringBuilder();
                sb.AppendLine("Subject: " + subject);
                sb.AppendLine("Public key: " + keyLabel);
                var sans = CertBuilder.ExtractSansFromCsr(csr);
                if (sans.Count > 0)
                {
                    sb.Append("CSR SANs: ");
                    sb.AppendLine(string.Join(", ", sans.ConvertAll(s => s.ToString())));
                }
                _csrSummary.ForeColor = Color.FromArgb(20, 100, 30);
                _csrSummary.Text = sb.ToString().TrimEnd();

                // Pre-populate the SAN list from the CSR so the admin sees
                // exactly what would land in the issued cert if they sign now.
                _sanList.Items.Clear();
                foreach (var s in sans) _sanList.Items.Add(s.ToString());
            }
            catch (Exception ex)
            {
                _csrSummary.ForeColor = Color.FromArgb(150, 30, 30);
                _csrSummary.Text = "Could not parse CSR: " + ex.Message;
            }
            UpdateSignEnabled();
        }

        // ── SAN editing ─────────────────────────────────────────────────────

        private void OnSanAdd()
        {
            var v = (_sanInput.Text ?? "").Trim();
            if (v.Length == 0) return;
            var prefix = (string)_sanType.SelectedItem + ":";
            string error = null;
            if (prefix == "DNS:" && !IsValidHostname(v)) error = "Not a valid DNS name: " + v;
            else if (prefix == "IP:" && !IsValidIp(v)) error = "Not a valid IP address: " + v;
            if (error != null)
            {
                MessageBox.Show(error, "Sign CSR", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            var entry = prefix + v;
            if (!_sanList.Items.Contains(entry)) _sanList.Items.Add(entry);
            _sanInput.Clear();
        }

        private void OnSanRemove()
        {
            if (_sanList.SelectedIndex >= 0)
                _sanList.Items.RemoveAt(_sanList.SelectedIndex);
        }

        private void OnSanFillFromCsr()
        {
            if (_parsedCsr == null) return;
            var sans = CertBuilder.ExtractSansFromCsr(_parsedCsr);
            foreach (var s in sans)
            {
                var entry = s.ToString();
                if (!_sanList.Items.Contains(entry)) _sanList.Items.Add(entry);
            }
        }

        private void OnRoleChanged()
        {
            var entry = _role.SelectedItem as RoleEntry;
            if (entry == null) return;
            _validityDays.Value = Math.Min(_validityDays.Maximum,
                Math.Max(_validityDays.Minimum, RolePresets.For(entry.Role).DefaultValidityDays));
        }

        private void UpdateSignEnabled()
        {
            _sign.Enabled = _parsedCsr != null && _issuerCert != null && _issuerKey != null;
        }

        // ── Sign ────────────────────────────────────────────────────────────

        private void OnSign()
        {
            if (_parsedCsr == null) { MessageBox.Show(this, "Pick a CSR file first.", "Sign CSR"); return; }
            if (_issuerKey == null)
            {
                MessageBox.Show(this, "This CA has no private key on this server, so it can't sign certificates.",
                    "Sign CSR", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            var roleEntry = (RoleEntry)_role.SelectedItem;
            var sans = new List<SanEntry>();
            foreach (var raw in _sanList.Items)
            {
                var s = (raw ?? "").ToString().Trim();
                if (s.StartsWith("DNS:", StringComparison.OrdinalIgnoreCase)) sans.Add(SanEntry.Dns(s.Substring(4)));
                else if (s.StartsWith("IP:", StringComparison.OrdinalIgnoreCase)) sans.Add(SanEntry.Ip(s.Substring(3)));
            }

            X509Certificate signed;
            try
            {
                var notBefore = DateTime.UtcNow.AddMinutes(-5);    // small skew tolerance
                var notAfter = notBefore.AddDays((int)_validityDays.Value);
                signed = CertBuilder.SignCsr(_parsedCsr, _issuerCert, _issuerKey,
                    roleEntry.Role, notBefore, notAfter, sans);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Signing failed:\n\n" + ex.Message, "Sign CSR",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            var subjectCn = TryReadCn(_parsedCsr) ?? "signed-cert";
            var defaultName = SafeFileName(subjectCn);
            string filter, defaultExt;
            switch (_outFormat.SelectedIndex)
            {
                case 1:  filter = "PEM (*.pem)|*.pem"; defaultExt = "pem"; break;
                case 2:  filter = "DER (*.der)|*.der"; defaultExt = "der"; break;
                case 3:  filter = "Certificate (*.crt)|*.crt"; defaultExt = "crt"; break;
                default: filter = "PEM (*.pem)|*.pem"; defaultExt = "pem"; break;
            }

            byte[] bytes;
            switch (_outFormat.SelectedIndex)
            {
                case 0:
                    bytes = Encoding.UTF8.GetBytes(PemIo.WriteCertPem(signed));
                    break;
                case 1:
                {
                    var sb = new StringBuilder();
                    sb.Append(PemIo.WriteCertPem(signed));
                    sb.Append(PemIo.WriteCertPem(_issuerCert));
                    bytes = Encoding.UTF8.GetBytes(sb.ToString());
                    break;
                }
                case 2:
                case 3:
                    bytes = signed.GetEncoded();
                    break;
                default:
                    bytes = Encoding.UTF8.GetBytes(PemIo.WriteCertPem(signed));
                    break;
            }

            using (var save = new SaveFileDialog
            {
                Title = "Save signed certificate",
                FileName = defaultName + "." + defaultExt,
                DefaultExt = defaultExt, Filter = filter, AddExtension = true,
            })
            {
                if (save.ShowDialog(this) != DialogResult.OK) return;
                File.WriteAllBytes(save.FileName, bytes);
                MessageBox.Show(this,
                    "Signed certificate written to:\n" + save.FileName +
                    "\n\nThe CSR submitter combines this file with their original private key.",
                    "Sign CSR", MessageBoxButtons.OK, MessageBoxIcon.Information);
                DialogResult = DialogResult.OK;
                Close();
            }
        }

        // ── Helpers ─────────────────────────────────────────────────────────

        private static string TryReadCn(Pkcs10CertificationRequest csr)
        {
            try
            {
                var dn = csr.GetCertificationRequestInfo().Subject?.ToString();
                if (string.IsNullOrEmpty(dn)) return null;
                foreach (var part in dn.Split(','))
                {
                    var p = part.Trim();
                    if (p.StartsWith("CN=", StringComparison.OrdinalIgnoreCase))
                        return p.Substring(3).Trim();
                }
            }
            catch { }
            return null;
        }

        private static string SafeFileName(string s)
        {
            foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return string.IsNullOrEmpty(s) ? "signed-cert" : s;
        }

        private static bool IsValidHostname(string v)
        {
            if (string.IsNullOrWhiteSpace(v)) return false;
            if (v.Length > 253) return false;
            return Regex.IsMatch(v, @"^(\*\.)?([a-zA-Z0-9]([a-zA-Z0-9\-]{0,61}[a-zA-Z0-9])?)(\.[a-zA-Z0-9]([a-zA-Z0-9\-]{0,61}[a-zA-Z0-9])?)*$");
        }

        private static bool IsValidIp(string v)
            => IPAddress.TryParse((v ?? "").Trim(), out var addr)
            && (addr.AddressFamily == AddressFamily.InterNetwork
             || addr.AddressFamily == AddressFamily.InterNetworkV6);

        private sealed class RoleEntry
        {
            public RolePreset Role { get; }
            public string Display { get; }
            public RoleEntry(RolePreset r, string d) { Role = r; Display = d; }
            public override string ToString() => Display;
        }
    }
}
