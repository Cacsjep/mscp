using System;
using Org.BouncyCastle.Asn1.X9;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Security;

namespace PKI.Crypto
{
    public enum KeyAlgorithm
    {
        Rsa2048,
        Rsa3072,
        Rsa4096,
        EcdsaP256,
        EcdsaP384,
    }

    public static class KeyPairFactory
    {
        private static readonly SecureRandom _random = new SecureRandom();

        public static KeyAlgorithm Parse(string s)
        {
            switch ((s ?? "").Trim().ToUpperInvariant())
            {
                case "RSA-2048": return KeyAlgorithm.Rsa2048;
                case "RSA-3072": return KeyAlgorithm.Rsa3072;
                case "RSA-4096": return KeyAlgorithm.Rsa4096;
                case "ECDSA-P256": return KeyAlgorithm.EcdsaP256;
                case "ECDSA-P384": return KeyAlgorithm.EcdsaP384;
                default: return KeyAlgorithm.Rsa2048;
            }
        }

        public static AsymmetricCipherKeyPair Generate(KeyAlgorithm alg)
        {
            switch (alg)
            {
                case KeyAlgorithm.Rsa2048: return Rsa(2048);
                case KeyAlgorithm.Rsa3072: return Rsa(3072);
                case KeyAlgorithm.Rsa4096: return Rsa(4096);
                case KeyAlgorithm.EcdsaP256: return Ec("P-256");
                case KeyAlgorithm.EcdsaP384: return Ec("P-384");
                default: throw new ArgumentOutOfRangeException(nameof(alg));
            }
        }

        public static string SignatureAlgorithmFor(KeyAlgorithm alg)
        {
            switch (alg)
            {
                case KeyAlgorithm.EcdsaP256: return "SHA256WITHECDSA";
                case KeyAlgorithm.EcdsaP384: return "SHA384WITHECDSA";
                default: return "SHA256WITHRSA";
            }
        }

        public static string Label(AsymmetricKeyParameter publicKey)
        {
            if (publicKey is RsaKeyParameters rsa) return $"RSA-{rsa.Modulus.BitLength}";
            if (publicKey is ECPublicKeyParameters ec) return $"ECDSA-P{ec.Parameters.Curve.FieldSize}";
            return "Unknown";
        }

        public static string DeriveSigAlgo(AsymmetricCipherKeyPair kp)
        {
            if (kp.Public is ECPublicKeyParameters ec)
                return ec.Parameters.Curve.FieldSize >= 384 ? "SHA384WITHECDSA" : "SHA256WITHECDSA";
            return "SHA256WITHRSA";
        }

        // Same heuristic as the keypair overload, but works when only the
        // private key is in hand (CSR signing path - we have the CA's
        // private key and the CSR's public key, never the CA keypair).
        public static string DeriveSigAlgo(AsymmetricKeyParameter privateKey)
        {
            if (privateKey is ECPrivateKeyParameters ec)
                return ec.Parameters.Curve.FieldSize >= 384 ? "SHA384WITHECDSA" : "SHA256WITHECDSA";
            return "SHA256WITHRSA";
        }

        private static AsymmetricCipherKeyPair Rsa(int bits)
        {
            var gen = new RsaKeyPairGenerator();
            gen.Init(new RsaKeyGenerationParameters(
                Org.BouncyCastle.Math.BigInteger.ValueOf(65537), _random, bits, 100));
            return gen.GenerateKeyPair();
        }

        private static AsymmetricCipherKeyPair Ec(string curveName)
        {
            var curve = ECNamedCurveTable.GetByName(curveName);
            var dp = new ECDomainParameters(curve.Curve, curve.G, curve.N, curve.H);
            var gen = new ECKeyPairGenerator();
            gen.Init(new ECKeyGenerationParameters(dp, _random));
            return gen.GenerateKeyPair();
        }
    }
}
