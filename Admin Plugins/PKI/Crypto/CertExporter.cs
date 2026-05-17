using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows.Forms;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.X509;
using PKI.Storage;
using VideoOS.Platform;

namespace PKI.Crypto
{
    // Shared cert-export entry point. Used by both the per-cert form
    // (PkiCertificateUserControl) and the Overview pane. Loads cert+key
    // from the MIP item (PFX bytes for issued/imported-with-key items,
    // DER fallback for imported cert-only items), shows a SaveFileDialog,
    // and writes the requested format. Key-bearing formats refuse on
    // items that only hold a public cert.
    public static class CertExporter
    {
        public static class Format
        {
            public const string PemCert     = "pem-cert";
            public const string DerCert     = "der-cert";
            public const string Crt         = "crt";
            public const string Pfx         = "pfx";
            public const string PemKeyPkcs8 = "pem-key-pkcs8";
            public const string PemKeyPkcs1 = "pem-key-pkcs1";
            public const string PemBundle   = "pem-bundle";
        }

        public static bool RequiresKey(string format)
        {
            switch (format)
            {
                case Format.Pfx:
                case Format.PemKeyPkcs8:
                case Format.PemKeyPkcs1:
                case Format.PemBundle:
                    return true;
                default:
                    return false;
            }
        }

        public static void ExportInteractive(Item item, string format, IWin32Window owner)
        {
            if (item == null) return;
            try
            {
                LoadResult loaded = Load(item);
                if (loaded.Certificate == null)
                {
                    MessageBox.Show(owner,
                        "This certificate has no exportable material on this server.",
                        "Export", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                if (RequiresKey(format) && loaded.PrivateKey == null)
                {
                    MessageBox.Show(owner,
                        "This certificate has no private key on this server. Only public-cert formats are available.",
                        "Export", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                string baseName = SafeFileName(item.Name ?? "certificate");
                byte[] bytes;
                string filter, defaultExt;

                switch (format)
                {
                    case Format.PemCert:
                        bytes = Encoding.UTF8.GetBytes(PemIo.WriteCertPem(loaded.Certificate));
                        filter = "PEM (*.pem)|*.pem"; defaultExt = "pem"; break;
                    case Format.DerCert:
                        bytes = loaded.Certificate.GetEncoded();
                        filter = "DER (*.der)|*.der"; defaultExt = "der"; break;
                    case Format.Crt:
                        bytes = loaded.Certificate.GetEncoded();
                        filter = "Certificate (*.crt)|*.crt"; defaultExt = "crt"; break;
                    case Format.Pfx:
                    {
                        var pwd = PromptPassword(owner, "PFX password (leave empty for unencrypted)");
                        if (pwd == null) return;
                        bytes = Pkcs12Bundle.Build(loaded.Certificate, loaded.PrivateKey, loaded.ChainExtras, pwd, item.Name);
                        filter = "PFX (*.pfx)|*.pfx"; defaultExt = "pfx"; break;
                    }
                    case Format.PemKeyPkcs8:
                    {
                        var pwd = PromptPassword(owner, "PEM key password (leave empty for unencrypted)");
                        if (pwd == null) return;
                        bytes = Encoding.UTF8.GetBytes(PemIo.WritePrivateKeyPemPkcs8(loaded.PrivateKey, pwd));
                        filter = "PEM (*.pem;*.key)|*.pem;*.key"; defaultExt = "pem"; break;
                    }
                    case Format.PemKeyPkcs1:
                        bytes = Encoding.UTF8.GetBytes(PemIo.WriteRsaPrivateKeyPemPkcs1(loaded.PrivateKey));
                        filter = "PEM (*.pem;*.key)|*.pem;*.key"; defaultExt = "pem"; break;
                    case Format.PemBundle:
                    {
                        var sb = new StringBuilder();
                        sb.Append(PemIo.WriteCertPem(loaded.Certificate));
                        sb.Append(PemIo.WritePrivateKeyPemPkcs8(loaded.PrivateKey, ""));
                        bytes = Encoding.UTF8.GetBytes(sb.ToString());
                        filter = "PEM (*.pem)|*.pem"; defaultExt = "pem"; break;
                    }
                    default:
                        return;
                }

                using (var dlg = new SaveFileDialog
                {
                    FileName = baseName + "." + defaultExt,
                    DefaultExt = defaultExt, Filter = filter, AddExtension = true,
                })
                {
                    if (dlg.ShowDialog(owner) == DialogResult.OK)
                    {
                        File.WriteAllBytes(dlg.FileName, bytes);
                        MessageBox.Show(owner, "Exported to:\n" + dlg.FileName, "Export",
                            MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(owner, "Export failed:\n\n" + ex.Message, "PKI",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // Multi-export: one folder + one shared password (for key
        // formats) applied to every selected cert. Skips items missing
        // material the format requires (no-key items for PFX / PEM key /
        // bundle) and reports them in the summary instead of aborting.
        public static void ExportInteractiveBatch(IList<Item> items, string format, IWin32Window owner)
        {
            if (items == null || items.Count == 0) return;
            if (items.Count == 1) { ExportInteractive(items[0], format, owner); return; }

            string defaultExt;
            switch (format)
            {
                case Format.PemCert:     defaultExt = "pem"; break;
                case Format.DerCert:     defaultExt = "der"; break;
                case Format.Crt:         defaultExt = "crt"; break;
                case Format.Pfx:         defaultExt = "pfx"; break;
                case Format.PemKeyPkcs8: defaultExt = "pem"; break;
                case Format.PemKeyPkcs1: defaultExt = "pem"; break;
                case Format.PemBundle:   defaultExt = "pem"; break;
                default: return;
            }

            string folder;
            using (var fbd = new FolderBrowserDialog
            {
                Description = $"Pick a folder. Each certificate is written as <name>.{defaultExt}.",
                ShowNewFolderButton = true,
            })
            {
                if (fbd.ShowDialog(owner) != DialogResult.OK) return;
                folder = fbd.SelectedPath;
            }

            // Only PFX and PEM-PKCS#8 prompt for a password; PKCS#1 RSA
            // and the PEM bundle write keys unencrypted by design.
            string sharedPwd = null;
            if (format == Format.Pfx || format == Format.PemKeyPkcs8)
            {
                sharedPwd = PromptPassword(owner,
                    format == Format.Pfx
                        ? "PFX password (used for every exported cert; leave empty for unencrypted)"
                        : "PEM key password (used for every exported cert; leave empty for unencrypted)");
                if (sharedPwd == null) return;
            }

            int ok = 0, skipped = 0, failed = 0;
            var errors = new List<string>();

            foreach (var item in items)
            {
                try
                {
                    var loaded = Load(item);
                    if (loaded.Certificate == null)
                    {
                        skipped++;
                        errors.Add($"{item.Name}: no exportable material");
                        continue;
                    }
                    if (RequiresKey(format) && loaded.PrivateKey == null)
                    {
                        skipped++;
                        errors.Add($"{item.Name}: no private key on this server");
                        continue;
                    }
                    byte[] bytes;
                    switch (format)
                    {
                        case Format.PemCert:
                            bytes = Encoding.UTF8.GetBytes(PemIo.WriteCertPem(loaded.Certificate));
                            break;
                        case Format.DerCert:
                        case Format.Crt:
                            bytes = loaded.Certificate.GetEncoded();
                            break;
                        case Format.Pfx:
                            bytes = Pkcs12Bundle.Build(loaded.Certificate, loaded.PrivateKey,
                                loaded.ChainExtras, sharedPwd, item.Name);
                            break;
                        case Format.PemKeyPkcs8:
                            bytes = Encoding.UTF8.GetBytes(
                                PemIo.WritePrivateKeyPemPkcs8(loaded.PrivateKey, sharedPwd));
                            break;
                        case Format.PemKeyPkcs1:
                            bytes = Encoding.UTF8.GetBytes(
                                PemIo.WriteRsaPrivateKeyPemPkcs1(loaded.PrivateKey));
                            break;
                        case Format.PemBundle:
                        {
                            var sb = new StringBuilder();
                            sb.Append(PemIo.WriteCertPem(loaded.Certificate));
                            sb.Append(PemIo.WritePrivateKeyPemPkcs8(loaded.PrivateKey, ""));
                            bytes = Encoding.UTF8.GetBytes(sb.ToString());
                            break;
                        }
                        default: continue;
                    }
                    var path = Path.Combine(folder, SafeFileName(item.Name) + "." + defaultExt);
                    File.WriteAllBytes(path, bytes);
                    ok++;
                }
                catch (Exception ex)
                {
                    failed++;
                    errors.Add($"{item.Name}: {ex.Message}");
                }
            }

            var summary = $"Exported {ok} of {items.Count} certificate(s) to:\n{folder}";
            if (skipped > 0 || failed > 0)
                summary += $"\n\nSkipped: {skipped}, failed: {failed}\n\n" + string.Join("\n", errors);
            MessageBox.Show(owner, summary, "Export",
                MessageBoxButtons.OK,
                (failed == 0 && skipped == 0) ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }

        // Loads cert + (optional) private key + chain from the MIP item.
        // Items with a key carry "Pfx" (base64 PKCS#12 bytes). Items
        // imported cert-only carry "Der" instead.
        private class LoadResult
        {
            public X509Certificate Certificate;
            public AsymmetricKeyParameter PrivateKey;
            public List<X509Certificate> ChainExtras = new List<X509Certificate>();
        }

        private static LoadResult Load(Item item)
        {
            var pfxB64 = Get(item, "Pfx");
            if (!string.IsNullOrEmpty(pfxB64))
            {
                var pfx = CertVault.FromBase64(pfxB64);
                var loaded = Pkcs12Bundle.Load(pfx, "");
                return new LoadResult
                {
                    Certificate = loaded.Certificate,
                    PrivateKey = loaded.PrivateKey,
                    ChainExtras = loaded.ChainExtras ?? new List<X509Certificate>(),
                };
            }

            var derB64 = Get(item, "Der");
            if (!string.IsNullOrEmpty(derB64))
            {
                var der = Convert.FromBase64String(derB64);
                var cert = new X509CertificateParser().ReadCertificate(der);
                return new LoadResult { Certificate = cert };
            }

            return new LoadResult();
        }

        private static string Get(Item item, string key)
            => (item != null && item.Properties.ContainsKey(key)) ? item.Properties[key] : "";

        private static string SafeFileName(string s)
        {
            foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s;
        }

        public static string PromptPassword(IWin32Window owner, string label)
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
                if (dlg.ShowDialog(owner) != DialogResult.OK) return null;
                return txt.Text ?? "";
            }
        }
    }
}
