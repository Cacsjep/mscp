using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.X509;
using PKI.Crypto;
using PKI.Storage;
using VideoOS.Platform;

namespace PKI.Admin
{
    // Modal Import wizard launched from the Overview pane.
    //
    // Supports:
    //   - PFX / PKCS#12 (cert + key + chain in one file, password-protected)
    //   - PEM cert (single CERTIFICATE block)
    //   - PEM bundle (CERTIFICATE + PRIVATE KEY in one file)
    //   - PEM cert + separate PEM key file
    //   - DER (.der / .crt) - cert only
    //
    // After parsing, the cert is inspected and a destination role is
    // suggested. The dropdown is guarded:
    //   Root CA      -> only Root Certificates
    //   Intermediate -> only Intermediate Certificates
    //   Leaf         -> HTTPS / 802.1X / Service (admin chooses)
    public class PkiImportDialog : Form
    {
        private readonly TextBox _certFile = new TextBox { Width = 360, ReadOnly = true };
        private readonly Button  _certBrowse = new Button { Text = "Browse...", Width = 80 };
        private readonly TextBox _keyFile  = new TextBox { Width = 360, ReadOnly = true };
        private readonly Button  _keyBrowse = new Button { Text = "Browse...", Width = 80 };
        private readonly Button  _keyClear  = new Button { Text = "Clear", Width = 60 };
        private readonly TextBox _password = new TextBox { Width = 280, UseSystemPasswordChar = true };
        private readonly TextBox _displayName = new TextBox { Width = 360 };

        private readonly Label _detected = new Label { AutoSize = true };
        private readonly TableLayoutPanel _details = new TableLayoutPanel
        {
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2,
        };
        private readonly ComboBox _destination = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList, Width = 360,
        };
        private readonly Label _destHint = new Label { AutoSize = true, ForeColor = Color.Gray };
        private readonly Button _import = new Button { Text = "Import", Width = 120, Height = 30 };
        private readonly Button _cancel = new Button { Text = "Cancel", Width = 100, Height = 30 };

        private X509Certificate _parsedCert;
        private AsymmetricKeyParameter _parsedKey;
        private List<X509Certificate> _parsedChain = new List<X509Certificate>();
        private DetectedRole _detectedRole = DetectedRole.Unknown;

        public PkiImportDialog()
        {
            Text = "Import certificate";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = false;
            MaximizeBox = false;
            // Form auto-sizes to its content; fixed minimum so the empty
            // state doesn't render as a tiny strip. ClientSize bumps as the
            // detected-role / details panel populates after parsing a file.
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            MinimumSize = new Size(780, 500);
            AutoScroll = false;

            Build();
            Wire();
            ReInspect();
        }

        // ── Layout ───────────────────────────────────────────────────────

        private void Build()
        {
            var grid = new TableLayoutPanel
            {
                Dock = DockStyle.Top, AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 3, Padding = new Padding(20, 16, 20, 16),
            };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
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

            void Row(string label, Control[] inputs, string hint)
            {
                grid.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(0, 8, 0, 0) }, 0, grid.RowCount);
                if (inputs.Length == 1)
                {
                    grid.Controls.Add(inputs[0], 1, grid.RowCount);
                    grid.SetColumnSpan(inputs[0], 2);
                }
                else
                {
                    var row = new FlowLayoutPanel
                    {
                        AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false,
                        Margin = new Padding(0, 4, 0, 0),
                    };
                    foreach (var c in inputs)
                    {
                        c.Margin = new Padding(0, 0, 6, 0);
                        row.Controls.Add(c);
                    }
                    grid.Controls.Add(row, 1, grid.RowCount);
                    grid.SetColumnSpan(row, 2);
                }
                grid.RowCount++;
                if (!string.IsNullOrEmpty(hint))
                {
                    grid.Controls.Add(new Label
                    {
                        Text = hint, AutoSize = true, ForeColor = Color.Gray,
                        Font = new Font(Font.FontFamily, 8F),
                        MaximumSize = new Size(700, 0),
                        Margin = new Padding(0, 0, 0, 6),
                    }, 1, grid.RowCount);
                    grid.SetColumnSpan(grid.GetControlFromPosition(1, grid.RowCount), 2);
                    grid.RowCount++;
                }
            }

            Section("Source file");
            Row("Certificate file", new Control[] { _certFile, _certBrowse },
                "Accepted: PFX/PKCS#12, PEM (cert only or cert+key bundle), DER, CRT.");
            Row("Separate key file", new Control[] { _keyFile, _keyBrowse, _keyClear },
                "Optional. Only needed when the private key is in a separate PEM file.");
            Row("Password", new Control[] { _password },
                "Required for PFX. For encrypted PEM keys (PKCS#8). Leave empty otherwise.");
            Row("Display name", new Control[] { _displayName },
                "Shown in the tree on the left. Auto-fills from the certificate's CN if you leave it empty.");

            Section("Detected role");
            grid.Controls.Add(_detected, 0, grid.RowCount);
            grid.SetColumnSpan(_detected, 3);
            grid.RowCount++;

            grid.Controls.Add(_details, 0, grid.RowCount);
            grid.SetColumnSpan(_details, 3);
            grid.RowCount++;

            Section("Destination");
            Row("Save under", new Control[] { _destination },
                "Pre-selected based on the detected role. Locked when the cert dictates it (Root CAs only land in Root Certificates).");
            grid.Controls.Add(_destHint, 1, grid.RowCount);
            grid.SetColumnSpan(_destHint, 2);
            grid.RowCount++;

            Controls.Add(grid);

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(20, 12, 20, 16), Height = 60,
            };
            _cancel.Margin = new Padding(0, 0, 0, 0);
            _import.Margin = new Padding(0, 0, 8, 0);
            buttons.Controls.Add(_cancel);
            buttons.Controls.Add(_import);
            Controls.Add(buttons);
            AcceptButton = _import;
            CancelButton = _cancel;
        }

        private void Wire()
        {
            _certBrowse.Click += (s, e) => OnBrowseCert();
            _keyBrowse.Click  += (s, e) => OnBrowseKey();
            _keyClear.Click   += (s, e) => { _keyFile.Text = ""; ReInspect(); };
            _password.TextChanged += (s, e) => ReInspect();
            _import.Click += (s, e) => OnImport();
            _cancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
        }

        // ── File picking ─────────────────────────────────────────────────

        private void OnBrowseCert()
        {
            using (var dlg = new OpenFileDialog
            {
                Title = "Select certificate file",
                Filter = "All supported|*.pfx;*.p12;*.pem;*.cer;*.crt;*.der;*.key|"
                       + "PFX/PKCS#12|*.pfx;*.p12|"
                       + "PEM|*.pem;*.crt;*.cer|"
                       + "DER|*.der;*.crt;*.cer|"
                       + "All files|*.*",
            })
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    _certFile.Text = dlg.FileName;
                    ReInspect();
                }
            }
        }

        private void OnBrowseKey()
        {
            using (var dlg = new OpenFileDialog
            {
                Title = "Select private key file (optional)",
                Filter = "PEM key|*.pem;*.key|All files|*.*",
            })
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    _keyFile.Text = dlg.FileName;
                    ReInspect();
                }
            }
        }

        // ── Parse + classify ─────────────────────────────────────────────

        private void ReInspect()
        {
            _parsedCert = null;
            _parsedKey = null;
            _parsedChain.Clear();
            _detectedRole = DetectedRole.Unknown;
            ResetDetailsPanel();

            var path = (_certFile.Text ?? "").Trim();
            if (path.Length == 0)
            {
                _detected.Text = "Pick a certificate file to begin.";
                _detected.ForeColor = Color.Gray;
                _import.Enabled = false;
                _destination.Items.Clear();
                _destination.Items.Add("(parse the file first)");
                _destination.SelectedIndex = 0;
                _destHint.Text = "";
                return;
            }
            if (!File.Exists(path)) { ShowError("File not found: " + path); return; }

            try
            {
                ParseFile(path);
                if (_parsedCert == null) { ShowError("No certificate found in the selected file."); return; }
            }
            catch (Exception ex)
            {
                ShowError("Could not parse the file:\n" + ex.Message);
                return;
            }

            _detectedRole = Classify(_parsedCert);
            FillDetectedView();
            UpdateDestinationDropdown();
            _import.Enabled = true;
        }

        private void ParseFile(string path)
        {
            var bytes = File.ReadAllBytes(path);
            var ext = Path.GetExtension(path).ToLowerInvariant();

            if (ext == ".pfx" || ext == ".p12")
            {
                var loaded = Pkcs12Bundle.Load(bytes, _password.Text ?? "");
                _parsedCert = loaded.Certificate;
                _parsedKey = loaded.PrivateKey;
                _parsedChain = loaded.ChainExtras ?? new List<X509Certificate>();
                return;
            }

            string asText = null;
            try { asText = Encoding.UTF8.GetString(bytes); } catch { }
            if (asText != null && asText.IndexOf("-----BEGIN", StringComparison.Ordinal) >= 0)
            {
                ParsePem(asText, _password.Text ?? "");
                ApplySeparateKeyIfAny();
                return;
            }

            try
            {
                _parsedCert = new X509CertificateParser().ReadCertificate(bytes);
                ApplySeparateKeyIfAny();
                return;
            }
            catch { }

            try
            {
                var loaded = Pkcs12Bundle.Load(bytes, _password.Text ?? "");
                _parsedCert = loaded.Certificate;
                _parsedKey = loaded.PrivateKey;
                _parsedChain = loaded.ChainExtras ?? new List<X509Certificate>();
            }
            catch
            {
                throw new InvalidDataException("Unrecognized certificate format.");
            }
        }

        private void ParsePem(string pem, string password)
        {
            var certs = PemIo.ReadCertsPem(pem);
            if (certs.Count == 0) throw new InvalidDataException("No CERTIFICATE block found.");
            _parsedCert = certs[0];
            for (int i = 1; i < certs.Count; i++) _parsedChain.Add(certs[i]);
            if (pem.IndexOf("PRIVATE KEY", StringComparison.Ordinal) >= 0)
            {
                try { _parsedKey = PemIo.ReadPrivateKeyPem(pem, password); }
                catch { _parsedKey = null; }
            }
        }

        private void ApplySeparateKeyIfAny()
        {
            var keyPath = (_keyFile.Text ?? "").Trim();
            if (keyPath.Length == 0 || !File.Exists(keyPath)) return;
            var pem = File.ReadAllText(keyPath);
            try { _parsedKey = PemIo.ReadPrivateKeyPem(pem, _password.Text ?? ""); }
            catch (Exception ex) { throw new InvalidDataException("Key file: " + ex.Message); }
        }

        private enum DetectedRole { Unknown, RootCA, IntermediateCA, LeafServerAuth, LeafClientAuth, LeafBoth, LeafOther }

        private static DetectedRole Classify(X509Certificate cert)
        {
            int basicConstraintsPathLen = cert.GetBasicConstraints();
            bool isCa = basicConstraintsPathLen >= 0 || HasCaTrue(cert);
            if (isCa)
            {
                bool selfSigned = cert.IssuerDN.Equivalent(cert.SubjectDN);
                return selfSigned ? DetectedRole.RootCA : DetectedRole.IntermediateCA;
            }
            var eku = TryReadEku(cert);
            bool serverAuth = eku != null && eku.HasKeyPurposeId(KeyPurposeID.id_kp_serverAuth);
            bool clientAuth = eku != null && eku.HasKeyPurposeId(KeyPurposeID.id_kp_clientAuth);
            if (serverAuth && clientAuth) return DetectedRole.LeafBoth;
            if (serverAuth) return DetectedRole.LeafServerAuth;
            if (clientAuth) return DetectedRole.LeafClientAuth;
            return DetectedRole.LeafOther;
        }

        private static bool HasCaTrue(X509Certificate cert)
        {
            try
            {
                var bcExt = cert.GetExtensionValue(X509Extensions.BasicConstraints);
                if (bcExt == null) return false;
                var bc = BasicConstraints.GetInstance(
                    Org.BouncyCastle.X509.Extension.X509ExtensionUtilities.FromExtensionValue(bcExt));
                return bc.IsCA();
            }
            catch { return false; }
        }

        private static ExtendedKeyUsage TryReadEku(X509Certificate cert)
        {
            try
            {
                var ekuExt = cert.GetExtensionValue(X509Extensions.ExtendedKeyUsage);
                if (ekuExt == null) return null;
                return ExtendedKeyUsage.GetInstance(
                    Org.BouncyCastle.X509.Extension.X509ExtensionUtilities.FromExtensionValue(ekuExt));
            }
            catch { return null; }
        }

        private void UpdateDestinationDropdown()
        {
            _destination.Items.Clear();
            switch (_detectedRole)
            {
                case DetectedRole.RootCA:
                    _destination.Items.Add(new DestEntry("Root Certificates", PKIDefinition.PkiRootCertKindId, RolePreset.RootCA));
                    _destHint.Text = "Locked to Root Certificates because the cert is a self-signed CA.";
                    break;
                case DetectedRole.IntermediateCA:
                    _destination.Items.Add(new DestEntry("Intermediate Certificates", PKIDefinition.PkiIntermediateKindId, RolePreset.IntermediateCA));
                    _destHint.Text = "Locked to Intermediate Certificates because the cert is a CA signed by another CA.";
                    break;
                case DetectedRole.LeafServerAuth:
                case DetectedRole.LeafClientAuth:
                case DetectedRole.LeafBoth:
                case DetectedRole.LeafOther:
                    _destination.Items.Add(new DestEntry("HTTPS Certificates",   PKIDefinition.PkiHttpsKindId,   RolePreset.HttpsServer));
                    _destination.Items.Add(new DestEntry("802.1X Certificates",  PKIDefinition.PkiDot1xKindId,   RolePreset.Dot1xClient));
                    _destination.Items.Add(new DestEntry("Service Certificates", PKIDefinition.PkiServiceKindId, RolePreset.Service));
                    _destHint.Text = "Pick a folder. Suggested based on the cert's Extended Key Usage.";
                    break;
                default:
                    _destHint.Text = ""; break;
            }
            _destination.SelectedIndex = SuggestedDestinationIndex(_detectedRole);
        }

        private static int SuggestedDestinationIndex(DetectedRole role)
        {
            switch (role)
            {
                case DetectedRole.LeafServerAuth: return 0;
                case DetectedRole.LeafClientAuth: return 1;
                case DetectedRole.LeafBoth:
                case DetectedRole.LeafOther:      return 2;
                default: return 0;
            }
        }

        private void ShowError(string text)
        {
            _detected.Text = text;
            _detected.ForeColor = Color.FromArgb(160, 30, 30);
            _import.Enabled = false;
            _destination.Items.Clear();
            _destination.Items.Add("(parse the file first)");
            _destination.SelectedIndex = 0;
            _destHint.Text = "";
            ResetDetailsPanel();
        }

        private void ResetDetailsPanel()
        {
            _details.Controls.Clear();
            _details.RowStyles.Clear();
            _details.RowCount = 0;
        }

        private void FillDetectedView()
        {
            _detected.Text = "Detected: " + RoleLabel(_detectedRole)
                + (_parsedKey != null ? "  (private key included)" : "  (cert only, no key)");
            _detected.ForeColor = Color.FromArgb(40, 100, 40);
            ResetDetailsPanel();

            void Row(string k, string v)
            {
                _details.Controls.Add(new Label
                {
                    Text = k, AutoSize = true, ForeColor = Color.FromArgb(110, 110, 110),
                    Margin = new Padding(0, 4, 12, 4),
                }, 0, _details.RowCount);
                _details.Controls.Add(new Label
                {
                    Text = string.IsNullOrEmpty(v) ? "(none)" : v,
                    AutoSize = true, ForeColor = Color.FromArgb(30, 30, 30),
                    Font = new Font("Cascadia Code", 9F, FontStyle.Regular),
                    MaximumSize = new Size(620, 0),
                    Margin = new Padding(0, 4, 0, 4),
                }, 1, _details.RowCount);
                _details.RowCount++;
            }
            Row("Subject", _parsedCert.SubjectDN.ToString());
            Row("Issuer",  _parsedCert.IssuerDN.ToString());
            Row("Serial",  _parsedCert.SerialNumber.ToString(16).ToUpperInvariant());
            Row("Not before", _parsedCert.NotBefore.ToUniversalTime().ToString("o"));
            Row("Not after",  _parsedCert.NotAfter.ToUniversalTime().ToString("o"));
            Row("Key alg", KeyPairFactory.Label(_parsedCert.GetPublicKey()));
        }

        private static string RoleLabel(DetectedRole r)
        {
            switch (r)
            {
                case DetectedRole.RootCA: return "Root CA (self-signed, BasicConstraints CA:TRUE)";
                case DetectedRole.IntermediateCA: return "Intermediate CA (BasicConstraints CA:TRUE, signed by another CA)";
                case DetectedRole.LeafServerAuth: return "Leaf, server authentication (EKU serverAuth)";
                case DetectedRole.LeafClientAuth: return "Leaf, client authentication (EKU clientAuth)";
                case DetectedRole.LeafBoth: return "Leaf, server + client (EKU serverAuth + clientAuth)";
                case DetectedRole.LeafOther: return "Leaf (other / unknown EKU)";
                default: return "(unknown)";
            }
        }

        // ── Import ───────────────────────────────────────────────────────

        private void OnImport()
        {
            if (_parsedCert == null) return;

            var dest = _destination.SelectedItem as DestEntry;
            if (dest == null)
            {
                MessageBox.Show("Pick a destination folder.", "Import",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (!IsDestinationCompatible(_detectedRole, dest.Role))
            {
                MessageBox.Show(
                    "The chosen destination doesn't match the certificate type.",
                    "Import blocked", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                var name = (_displayName.Text ?? "").Trim();
                if (string.IsNullOrEmpty(name))
                {
                    name = ExtractCnFromSubject(_parsedCert.SubjectDN.ToString());
                    if (string.IsNullOrEmpty(name)) name = "Imported certificate";
                }

                byte[] pfxBytes = (_parsedKey != null)
                    ? Pkcs12Bundle.Build(_parsedCert, _parsedKey, _parsedChain, "", name)
                    : null;

                var serverId = EnvironmentManager.Instance.MasterSite.ServerId;
                var newFqid = new FQID(serverId, Guid.Empty, Guid.NewGuid(), FolderType.No, dest.Kind);
                var item = new Item(newFqid, name);
                item.Properties["Category"]      = dest.Role.ToString();
                item.Properties["RolePreset"]    = dest.Role.ToString();
                item.Properties["Subject"]       = _parsedCert.SubjectDN.ToString();
                item.Properties["Issuer"]        = _parsedCert.IssuerDN.ToString();
                item.Properties["IssuerThumbprint"] = "";
                item.Properties["Subject_CN"]    = ExtractCnFromSubject(_parsedCert.SubjectDN.ToString());
                item.Properties["SerialNumber"]  = _parsedCert.SerialNumber.ToString(16).ToUpperInvariant();
                item.Properties["NotBefore"]     = _parsedCert.NotBefore.ToUniversalTime().ToString("o");
                item.Properties["NotAfter"]      = _parsedCert.NotAfter.ToUniversalTime().ToString("o");
                item.Properties["KeyAlgorithm"]  = KeyPairFactory.Label(_parsedCert.GetPublicKey());
                item.Properties["HasPrivateKey"] = (_parsedKey != null) ? "True" : "False";
                item.Properties["Thumbprint"]    = ToHex(Sha1(_parsedCert.GetEncoded()));
                item.Properties["CreatedAt"]     = DateTime.UtcNow.ToString("o");

                if (pfxBytes != null)
                    item.Properties["EncryptedPfx"] = CertVault.EncryptToBase64(pfxBytes);
                else
                    item.Properties["EncryptedDer"] = Convert.ToBase64String(_parsedCert.GetEncoded());

                Configuration.Instance.SaveItemConfiguration(PKIDefinition.PluginId, item);

                MessageBox.Show(
                    "Imported \"" + name + "\" into " + dest.Display + ".",
                    "Import successful", MessageBoxButtons.OK, MessageBoxIcon.Information);
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Import failed:\n\n" + ex.Message, "PKI",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static bool IsDestinationCompatible(DetectedRole role, RolePreset dest)
        {
            switch (role)
            {
                case DetectedRole.RootCA: return dest == RolePreset.RootCA;
                case DetectedRole.IntermediateCA: return dest == RolePreset.IntermediateCA;
                case DetectedRole.LeafServerAuth:
                case DetectedRole.LeafClientAuth:
                case DetectedRole.LeafBoth:
                case DetectedRole.LeafOther:
                    return dest == RolePreset.HttpsServer
                        || dest == RolePreset.Dot1xClient
                        || dest == RolePreset.Service;
                default: return false;
            }
        }

        private static string ExtractCnFromSubject(string dn)
        {
            if (string.IsNullOrEmpty(dn)) return "";
            foreach (var part in dn.Split(','))
            {
                var p = part.Trim();
                if (p.StartsWith("CN=", StringComparison.OrdinalIgnoreCase))
                    return p.Substring(3).Trim();
            }
            return "";
        }

        private static byte[] Sha1(byte[] bytes)
        {
            var d = new Org.BouncyCastle.Crypto.Digests.Sha1Digest();
            d.BlockUpdate(bytes, 0, bytes.Length);
            var h = new byte[d.GetDigestSize()];
            d.DoFinal(h, 0);
            return h;
        }

        private static string ToHex(byte[] bytes)
        {
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (var b in bytes) sb.Append(b.ToString("X2"));
            return sb.ToString();
        }

        private class DestEntry
        {
            public readonly string Display;
            public readonly Guid Kind;
            public readonly RolePreset Role;
            public DestEntry(string d, Guid k, RolePreset r) { Display = d; Kind = k; Role = r; }
            public override string ToString() => Display;
        }
    }
}
