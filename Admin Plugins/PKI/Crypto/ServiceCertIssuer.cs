using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using PKI.Storage;
using VideoOS.Platform;

namespace PKI.Crypto
{
    public class ServiceCertRequest
    {
        public Guid DestinationKind;          // PKIDefinition.PkiServiceKindId etc.
        public RolePreset Role;               // typically RolePreset.Service
        public string ItemName;               // tree label, e.g. "RS recording01"
        public string CommonName;             // cert Subject CN, e.g. hostname
        public string Organization;
        public string OrganizationalUnit;
        public string Country;
        public List<string> DnsNames = new List<string>();
        public List<string> IpAddresses = new List<string>();
        public Item IssuingCA;                // must have "Pfx" property with private key
        public int ValidityDays = 397;
        public KeyAlgorithm KeyAlgorithm = KeyAlgorithm.Rsa2048;
    }

    // Standalone cert issuance path used by the bulk "Generate certs for all
    // services" and the single-service picker. Mirrors the work done inside
    // PkiCertItemManager.GenerateAndStore but takes structured input instead
    // of pulling from a UI item, so it can run from anywhere in the plugin.
    public static class ServiceCertIssuer
    {
        public class IssueResult
        {
            public bool Ok;
            public string Message;
            public Item Item;
        }

        public static IssueResult Issue(ServiceCertRequest req)
        {
            try
            {
                if (req == null) throw new ArgumentNullException(nameof(req));
                if (req.IssuingCA == null) throw new InvalidOperationException("Pick an issuing CA.");
                if (string.IsNullOrWhiteSpace(req.CommonName))
                    throw new InvalidOperationException("CommonName is required.");

                var pfxB64 = GetProp(req.IssuingCA, "Pfx");
                if (string.IsNullOrEmpty(pfxB64))
                    throw new InvalidOperationException("Issuing CA has no private key on this machine.");

                var caPfx = CertVault.FromBase64(pfxB64);
                var caBundle = Pkcs12Bundle.Load(caPfx, "");
                if (caBundle.PrivateKey == null)
                    throw new InvalidOperationException("Issuing CA's PFX has no private key.");

                var keyPair = KeyPairFactory.Generate(req.KeyAlgorithm);

                var sans = new List<SanEntry>();
                foreach (var d in req.DnsNames)
                    if (!string.IsNullOrWhiteSpace(d)) sans.Add(SanEntry.Dns(d.Trim()));
                foreach (var ip in req.IpAddresses)
                    if (!string.IsNullOrWhiteSpace(ip) && IPAddress.TryParse(ip.Trim(), out _))
                        sans.Add(SanEntry.Ip(ip.Trim()));

                var build = new CertBuildRequest
                {
                    Role = req.Role,
                    Subject = new CertSubject
                    {
                        CommonName         = req.CommonName,
                        Organization       = req.Organization,
                        OrganizationalUnit = req.OrganizationalUnit,
                        Country            = req.Country,
                    },
                    SubjectAlternativeNames = sans,
                    NotBefore = DateTime.UtcNow.AddMinutes(-5),
                    NotAfter  = DateTime.UtcNow.AddDays(req.ValidityDays > 0 ? req.ValidityDays : 397),
                    SubjectKeyPair = keyPair,
                    IssuerCert = caBundle.Certificate,
                    IssuerPrivateKey = caBundle.PrivateKey,
                    IssuerSignatureAlgorithm = "SHA256WITHRSA",
                };

                var cert = CertBuilder.Build(build);

                var chainExtras = new List<Org.BouncyCastle.X509.X509Certificate> { caBundle.Certificate };
                var pfxBytes = Pkcs12Bundle.Build(cert, keyPair.Private, chainExtras, "", req.ItemName);

                var serverId = EnvironmentManager.Instance.MasterSite.ServerId;
                var fqid = new FQID(serverId, Guid.Empty, Guid.NewGuid(), FolderType.No, req.DestinationKind);
                var item = new Item(fqid, req.ItemName);

                item.Properties["RolePreset"]       = req.Role.ToString();
                item.Properties["ValidityDays"]     = req.ValidityDays.ToString();
                item.Properties["KeyAlgorithmRequest"] = AlgRequestLabel(req.KeyAlgorithm);
                item.Properties["Subject_CN"]       = req.CommonName;
                item.Properties["Subject_O"]       = req.Organization ?? "";
                item.Properties["Subject_OU"]      = req.OrganizationalUnit ?? "";
                item.Properties["Subject_C"]       = req.Country ?? "";
                item.Properties["SubjectAlternativeNames"] = SerializeSans(sans);
                item.Properties["IssuerThumbprint"] = GetProp(req.IssuingCA, "Thumbprint");
                item.Properties["Pfx"]             = CertVault.ToBase64(pfxBytes);
                item.Properties["Thumbprint"]      = ToHex(Sha1(cert.GetEncoded()));
                item.Properties["Subject"]         = cert.SubjectDN.ToString();
                item.Properties["Issuer"]          = cert.IssuerDN.ToString();
                item.Properties["SerialNumber"]    = cert.SerialNumber.ToString(16).ToUpperInvariant();
                item.Properties["NotBefore"]       = cert.NotBefore.ToUniversalTime().ToString("o");
                item.Properties["NotAfter"]        = cert.NotAfter.ToUniversalTime().ToString("o");
                item.Properties["KeyAlgorithm"]    = KeyPairFactory.Label(keyPair.Public);
                item.Properties["HasPrivateKey"]   = "True";
                item.Properties["CreatedAt"]       = DateTime.UtcNow.ToString("o");

                Configuration.Instance.SaveItemConfiguration(PKIDefinition.PluginId, item);
                PKIDefinition.Log.Info($"Issued service cert: {req.ItemName} (thumbprint {item.Properties["Thumbprint"]})");
                return new IssueResult { Ok = true, Item = item };
            }
            catch (Exception ex)
            {
                PKIDefinition.Log.Error($"Service cert issuance failed for '{req?.ItemName}': {ex.Message}");
                return new IssueResult { Ok = false, Message = ex.Message };
            }
        }

        private static string SerializeSans(List<SanEntry> sans)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < sans.Count; i++)
            {
                if (i > 0) sb.Append('|');
                sb.Append(sans[i].ToString());
            }
            return sb.ToString();
        }

        private static string AlgRequestLabel(KeyAlgorithm alg)
        {
            switch (alg)
            {
                case KeyAlgorithm.Rsa3072: return "RSA-3072";
                case KeyAlgorithm.Rsa4096: return "RSA-4096";
                case KeyAlgorithm.EcdsaP256: return "ECDSA-P256";
                case KeyAlgorithm.EcdsaP384: return "ECDSA-P384";
                default: return "RSA-2048";
            }
        }

        private static string GetProp(Item item, string key)
            => (item != null && item.Properties.ContainsKey(key)) ? item.Properties[key] : "";

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
    }
}
