using System;
using System.Collections.Generic;
using System.IO;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.OpenSsl;
using Org.BouncyCastle.Pkcs;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.X509;
using PemObject = Org.BouncyCastle.Utilities.IO.Pem.PemObject;
using PemReader = Org.BouncyCastle.OpenSsl.PemReader;
using PemWriter = Org.BouncyCastle.OpenSsl.PemWriter;

namespace PKI.Crypto
{
    public static class PemIo
    {
        public static string WriteCertPem(X509Certificate cert)
        {
            using (var sw = new StringWriter())
            {
                var pw = new PemWriter(sw);
                pw.WriteObject(cert);
                pw.Writer.Flush();
                return sw.ToString();
            }
        }

        public static X509Certificate ReadCertPem(string pem)
        {
            using (var sr = new StringReader(pem))
            {
                var pr = new PemReader(sr);
                while (true)
                {
                    var obj = pr.ReadObject();
                    if (obj == null) break;
                    if (obj is X509Certificate c) return c;
                }
            }
            throw new InvalidDataException("No CERTIFICATE block.");
        }

        public static List<X509Certificate> ReadCertsPem(string pem)
        {
            var result = new List<X509Certificate>();
            using (var sr = new StringReader(pem))
            {
                var pr = new PemReader(sr);
                while (true)
                {
                    var obj = pr.ReadObject();
                    if (obj == null) break;
                    if (obj is X509Certificate c) result.Add(c);
                }
            }
            return result;
        }

        public static string WritePrivateKeyPemPkcs8(AsymmetricKeyParameter privateKey, string password)
        {
            using (var sw = new StringWriter())
            {
                var pw = new PemWriter(sw);
                if (string.IsNullOrEmpty(password))
                {
                    var info = PrivateKeyInfoFactory.CreatePrivateKeyInfo(privateKey);
                    pw.WriteObject(new PemObject("PRIVATE KEY", info.GetEncoded()));
                }
                else
                {
                    pw.WriteObject(new MiscPemGenerator(privateKey, "AES-256-CBC", password.ToCharArray(), new SecureRandom()));
                }
                pw.Writer.Flush();
                return sw.ToString();
            }
        }

        public static string WriteRsaPrivateKeyPemPkcs1(AsymmetricKeyParameter privateKey)
        {
            if (!(privateKey is RsaPrivateCrtKeyParameters))
                throw new InvalidOperationException("PKCS#1 PEM only supports RSA private keys.");
            using (var sw = new StringWriter())
            {
                var pw = new PemWriter(sw);
                pw.WriteObject(privateKey);
                pw.Writer.Flush();
                return sw.ToString();
            }
        }

        public static AsymmetricKeyParameter ReadPrivateKeyPem(string pem, string password)
        {
            using (var sr = new StringReader(pem))
            {
                var pr = string.IsNullOrEmpty(password) ? new PemReader(sr) : new PemReader(sr, new Pwd(password));
                while (true)
                {
                    var obj = pr.ReadObject();
                    if (obj == null) break;
                    if (obj is AsymmetricCipherKeyPair kp) return kp.Private;
                    if (obj is AsymmetricKeyParameter k && k.IsPrivate) return k;
                }
            }
            throw new InvalidDataException("No PRIVATE KEY block (or wrong password).");
        }

        public static Pkcs10CertificationRequest ReadCsrAuto(byte[] bytes)
        {
            string text;
            try { text = System.Text.Encoding.UTF8.GetString(bytes); } catch { text = null; }
            if (text != null && text.Contains("CERTIFICATE REQUEST"))
            {
                using (var sr = new StringReader(text))
                {
                    var pr = new PemReader(sr);
                    while (true)
                    {
                        var obj = pr.ReadObject();
                        if (obj == null) break;
                        if (obj is Pkcs10CertificationRequest csr) return csr;
                    }
                }
            }
            return new Pkcs10CertificationRequest(bytes);
        }

        private class Pwd : IPasswordFinder
        {
            private readonly char[] _p;
            public Pwd(string p) { _p = p.ToCharArray(); }
            public char[] GetPassword() => (char[])_p.Clone();
        }
    }
}
