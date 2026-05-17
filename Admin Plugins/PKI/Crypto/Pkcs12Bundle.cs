using System;
using System.Collections.Generic;
using System.IO;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Pkcs;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.X509;

namespace PKI.Crypto
{
    public class Pkcs12LoadResult
    {
        public X509Certificate Certificate;
        public AsymmetricKeyParameter PrivateKey;
        public List<X509Certificate> ChainExtras = new List<X509Certificate>();
    }

    public static class Pkcs12Bundle
    {
        private static readonly SecureRandom _random = new SecureRandom();

        public static byte[] Build(X509Certificate cert,
                                   AsymmetricKeyParameter privateKey,
                                   IEnumerable<X509Certificate> chainExtras,
                                   string password,
                                   string friendlyName)
        {
            if (cert == null) throw new ArgumentNullException(nameof(cert));
            if (privateKey == null) throw new ArgumentNullException(nameof(privateKey));

            var store = new Pkcs12StoreBuilder().Build();
            var entry = new X509CertificateEntry(cert);
            var chain = new List<X509CertificateEntry> { entry };
            if (chainExtras != null)
                foreach (var c in chainExtras) if (c != null) chain.Add(new X509CertificateEntry(c));

            string alias = string.IsNullOrEmpty(friendlyName) ? cert.SubjectDN.ToString() : friendlyName;
            store.SetKeyEntry(alias, new AsymmetricKeyEntry(privateKey), chain.ToArray());

            using (var ms = new MemoryStream())
            {
                store.Save(ms, (password ?? string.Empty).ToCharArray(), _random);
                return ms.ToArray();
            }
        }

        public static Pkcs12LoadResult Load(byte[] pfx, string password)
        {
            if (pfx == null || pfx.Length == 0) throw new ArgumentException("Empty PFX bytes");
            var store = new Pkcs12StoreBuilder().Build();
            using (var ms = new MemoryStream(pfx))
                store.Load(ms, (password ?? string.Empty).ToCharArray());

            var result = new Pkcs12LoadResult();
            foreach (string alias in store.Aliases)
            {
                if (store.IsKeyEntry(alias))
                {
                    result.PrivateKey = store.GetKey(alias).Key;
                    result.Certificate = store.GetCertificate(alias)?.Certificate;
                    var chain = store.GetCertificateChain(alias);
                    if (chain != null && chain.Length > 1)
                        for (int i = 1; i < chain.Length; i++) result.ChainExtras.Add(chain[i].Certificate);
                    break;
                }
            }
            if (result.Certificate == null)
                foreach (string alias in store.Aliases)
                    if (store.IsCertificateEntry(alias))
                    {
                        result.Certificate = store.GetCertificate(alias).Certificate;
                        break;
                    }
            if (result.Certificate == null) throw new InvalidDataException("PFX contains no certificates.");
            return result;
        }
    }
}
