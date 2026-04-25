using System;
using System.Collections.Generic;
using System.Net;
using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Operators;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.X509;

namespace PKI.Crypto
{
    public class CertSubject
    {
        public string CommonName;
        public string Organization;
        public string OrganizationalUnit;
        public string Country;
    }

    public class SanEntry
    {
        public bool IsIp;
        public string Value;
        public static SanEntry Dns(string v) => new SanEntry { IsIp = false, Value = v };
        public static SanEntry Ip(string v)  => new SanEntry { IsIp = true,  Value = v };
        public override string ToString() => (IsIp ? "IP:" : "DNS:") + Value;
    }

    public class CertBuildRequest
    {
        public RolePreset Role;
        public CertSubject Subject;
        public List<SanEntry> SubjectAlternativeNames;
        public DateTime NotBefore;
        public DateTime NotAfter;
        public AsymmetricCipherKeyPair SubjectKeyPair;
        public X509Certificate IssuerCert;          // null => self-signed
        public AsymmetricKeyParameter IssuerPrivateKey;
        public string IssuerSignatureAlgorithm;
    }

    public static class CertBuilder
    {
        private static readonly SecureRandom _random = new SecureRandom();

        public static X509Certificate Build(CertBuildRequest req)
        {
            if (req == null) throw new ArgumentNullException(nameof(req));
            if (req.SubjectKeyPair == null) throw new ArgumentException("SubjectKeyPair is required");
            if (req.NotAfter <= req.NotBefore) throw new ArgumentException("NotAfter must be after NotBefore");
            if (req.Subject == null || string.IsNullOrWhiteSpace(req.Subject.CommonName))
                throw new ArgumentException("Common Name (CN) is required");

            var preset = RolePresets.For(req.Role);
            var subjectDn = BuildDn(req.Subject);

            bool selfSigned = req.IssuerCert == null;
            var issuerDn = selfSigned ? subjectDn : req.IssuerCert.SubjectDN;
            var signingKey = selfSigned ? req.SubjectKeyPair.Private : req.IssuerPrivateKey;
            var sigAlgo = selfSigned
                ? KeyPairFactory.DeriveSigAlgo(req.SubjectKeyPair)
                : (req.IssuerSignatureAlgorithm ?? KeyPairFactory.DeriveSigAlgo(req.SubjectKeyPair));

            var gen = new X509V3CertificateGenerator();
            gen.SetSerialNumber(RandomSerial());
            gen.SetIssuerDN(issuerDn);
            gen.SetSubjectDN(subjectDn);
            gen.SetNotBefore(req.NotBefore.ToUniversalTime());
            gen.SetNotAfter(req.NotAfter.ToUniversalTime());
            gen.SetPublicKey(req.SubjectKeyPair.Public);

            var bc = preset.IsCa
                ? (preset.PathLengthConstraint.HasValue
                    ? new BasicConstraints(preset.PathLengthConstraint.Value)
                    : new BasicConstraints(true))
                : new BasicConstraints(false);
            gen.AddExtension(X509Extensions.BasicConstraints, true, bc);

            if (preset.KeyUsageBits != 0)
                gen.AddExtension(X509Extensions.KeyUsage, preset.IsCa, new KeyUsage(preset.KeyUsageBits));

            if (preset.ExtendedKeyUsages != null && preset.ExtendedKeyUsages.Length > 0)
                gen.AddExtension(X509Extensions.ExtendedKeyUsage, false, new ExtendedKeyUsage(preset.ExtendedKeyUsages));

            if (req.SubjectAlternativeNames != null && req.SubjectAlternativeNames.Count > 0)
            {
                var altNames = new List<GeneralName>();
                foreach (var san in req.SubjectAlternativeNames)
                {
                    if (san == null || string.IsNullOrWhiteSpace(san.Value)) continue;
                    if (san.IsIp)
                    {
                        if (IPAddress.TryParse(san.Value, out var ip))
                            altNames.Add(new GeneralName(GeneralName.IPAddress, ip.ToString()));
                    }
                    else
                    {
                        altNames.Add(new GeneralName(GeneralName.DnsName, san.Value));
                    }
                }
                if (altNames.Count > 0)
                    gen.AddExtension(X509Extensions.SubjectAlternativeName, false, new GeneralNames(altNames.ToArray()));
            }

            gen.AddExtension(X509Extensions.SubjectKeyIdentifier, false,
                new SubjectKeyIdentifier(SubjectPublicKeyInfoFactory.CreateSubjectPublicKeyInfo(req.SubjectKeyPair.Public)));
            var akiKey = selfSigned ? req.SubjectKeyPair.Public : req.IssuerCert.GetPublicKey();
            gen.AddExtension(X509Extensions.AuthorityKeyIdentifier, false,
                new AuthorityKeyIdentifier(SubjectPublicKeyInfoFactory.CreateSubjectPublicKeyInfo(akiKey)));

            var signer = new Asn1SignatureFactory(sigAlgo, signingKey, _random);
            return gen.Generate(signer);
        }

        private static X509Name BuildDn(CertSubject s)
        {
            var oids = new List<DerObjectIdentifier>();
            var values = new List<string>();
            void Add(DerObjectIdentifier oid, string v)
            {
                if (!string.IsNullOrWhiteSpace(v)) { oids.Add(oid); values.Add(v.Trim()); }
            }
            Add(X509Name.CN, s.CommonName);
            Add(X509Name.O,  s.Organization);
            Add(X509Name.OU, s.OrganizationalUnit);
            Add(X509Name.C,  s.Country);
            return new X509Name(oids, values);
        }

        private static BigInteger RandomSerial()
        {
            byte[] buf = new byte[20];
            _random.NextBytes(buf);
            buf[0] &= 0x7F;          // positive
            return new BigInteger(1, buf);
        }
    }
}
