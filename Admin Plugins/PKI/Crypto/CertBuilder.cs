using System;
using System.Collections.Generic;
using System.Net;
using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Asn1.Pkcs;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Operators;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Pkcs;
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

        // Issues a certificate from an externally-supplied CSR. Subject DN
        // and public key are taken verbatim from the CSR; the signing key,
        // serial, validity, and extension policy come from the caller.
        // SAN entries default to whatever the CSR's extensionRequest
        // attribute carries (so a server admin's openssl-generated CSR
        // keeps its names) but the caller can override.
        public static X509Certificate SignCsr(
            Pkcs10CertificationRequest csr,
            X509Certificate issuerCert,
            AsymmetricKeyParameter issuerPrivateKey,
            RolePreset role,
            DateTime notBefore,
            DateTime notAfter,
            List<SanEntry> sanOverride = null,
            string issuerSignatureAlgorithm = null)
        {
            if (csr == null) throw new ArgumentNullException(nameof(csr));
            if (issuerCert == null) throw new ArgumentNullException(nameof(issuerCert));
            if (issuerPrivateKey == null) throw new ArgumentNullException(nameof(issuerPrivateKey));
            if (notAfter <= notBefore) throw new ArgumentException("NotAfter must be after NotBefore");
            if (!csr.Verify())
                throw new InvalidOperationException("CSR signature is invalid - the CSR is corrupt or not self-signed.");

            var info = csr.GetCertificationRequestInfo();
            var subjectDn = info.Subject;
            var subjectPublicKey = csr.GetPublicKey();
            var preset = RolePresets.For(role);

            var gen = new X509V3CertificateGenerator();
            gen.SetSerialNumber(RandomSerial());
            gen.SetIssuerDN(issuerCert.SubjectDN);
            gen.SetSubjectDN(subjectDn);
            gen.SetNotBefore(notBefore.ToUniversalTime());
            gen.SetNotAfter(notAfter.ToUniversalTime());
            gen.SetPublicKey(subjectPublicKey);

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

            // SAN: caller wins; otherwise inherit from the CSR's
            // extensionRequest attribute (the typical openssl CSR carries
            // its SAN list there).
            var sans = sanOverride ?? ExtractSansFromCsr(csr);
            if (sans != null && sans.Count > 0)
            {
                var altNames = new List<GeneralName>();
                foreach (var san in sans)
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
                new SubjectKeyIdentifier(SubjectPublicKeyInfoFactory.CreateSubjectPublicKeyInfo(subjectPublicKey)));
            gen.AddExtension(X509Extensions.AuthorityKeyIdentifier, false,
                new AuthorityKeyIdentifier(SubjectPublicKeyInfoFactory.CreateSubjectPublicKeyInfo(issuerCert.GetPublicKey())));

            // Signature algorithm: caller override (e.g. SHA-384 for ECDSA-P384
            // CAs) or inferred from the issuer's key. Subject's algorithm is
            // irrelevant - we sign with the CA's key.
            var sigAlgo = issuerSignatureAlgorithm ?? KeyPairFactory.DeriveSigAlgo(issuerPrivateKey);
            var signer = new Asn1SignatureFactory(sigAlgo, issuerPrivateKey, _random);
            return gen.Generate(signer);
        }

        // Pulls DNS / IP SAN entries out of a CSR's extensionRequest
        // attribute. Returns an empty list when the CSR has no SAN
        // (which is fine - a leaf cert with no SAN is just CN-only).
        public static List<SanEntry> ExtractSansFromCsr(Pkcs10CertificationRequest csr)
        {
            var result = new List<SanEntry>();
            if (csr == null) return result;
            try
            {
                var info = csr.GetCertificationRequestInfo();
                var attrs = info.Attributes;
                if (attrs == null) return result;
                foreach (var entry in attrs)
                {
                    var attr = AttributeX509.GetInstance(entry);
                    if (!attr.AttrType.Equals(PkcsObjectIdentifiers.Pkcs9AtExtensionRequest)) continue;
                    foreach (var setEntry in attr.AttrValues)
                    {
                        var exts = X509Extensions.GetInstance(setEntry);
                        var sanExt = exts.GetExtension(X509Extensions.SubjectAlternativeName);
                        if (sanExt == null) continue;
                        var names = GeneralNames.GetInstance(sanExt.GetParsedValue());
                        foreach (var n in names.GetNames())
                        {
                            if (n.TagNo == GeneralName.DnsName)
                                result.Add(SanEntry.Dns(((IAsn1String)n.Name).GetString()));
                            else if (n.TagNo == GeneralName.IPAddress)
                            {
                                var bytes = Asn1OctetString.GetInstance(n.Name).GetOctets();
                                if (bytes.Length == 4 || bytes.Length == 16)
                                    result.Add(SanEntry.Ip(new IPAddress(bytes).ToString()));
                            }
                        }
                    }
                }
            }
            catch { /* best-effort: a malformed extensionRequest doesn't block signing - the caller can still set SAN manually */ }
            return result;
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
