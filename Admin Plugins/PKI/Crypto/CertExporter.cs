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
    // from the MIP item (DPAPI-encrypted PFX or DER-only fallback for
    // imported cert-only certs), shows a SaveFileDialog, and writes the
    // requested format. Key-bearing formats refuse on items that only
    // hold a public cert.
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

        // Loads cert + (optional) private key + chain from the MIP item.
        // Items issued by this plugin carry an EncryptedPfx (DPAPI). Items
        // imported as cert-only carry an EncryptedDer instead.
        private class LoadResult
        {
            public X509Certificate Certificate;
            public AsymmetricKeyParameter PrivateKey;
            public List<X509Certificate> ChainExtras = new List<X509Certificate>();
        }

        private static LoadResult Load(Item item)
        {
            var pfxB64 = Get(item, "EncryptedPfx");
            if (!string.IsNullOrEmpty(pfxB64))
            {
                var pfx = CertVault.DecryptFromBase64(pfxB64);
                var loaded = Pkcs12Bundle.Load(pfx, "");
                return new LoadResult
                {
                    Certificate = loaded.Certificate,
                    PrivateKey = loaded.PrivateKey,
                    ChainExtras = loaded.ChainExtras ?? new List<X509Certificate>(),
                };
            }

            var derB64 = Get(item, "EncryptedDer");
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
