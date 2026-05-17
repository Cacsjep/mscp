using Org.BouncyCastle.Asn1.X509;

namespace PKI.Crypto
{
    public static class RolePresets
    {
        public const int DefaultRootCAValidityDays         = 20 * 365;
        public const int DefaultIntermediateCAValidityDays = 10 * 365;
        public const int DefaultLeafValidityDays           = 397;

        public class Preset
        {
            public bool IsCa;
            public int? PathLengthConstraint;
            public int KeyUsageBits;
            public KeyPurposeID[] ExtendedKeyUsages;
            public int DefaultValidityDays;
        }

        public static Preset For(RolePreset role)
        {
            switch (role)
            {
                case RolePreset.RootCA:
                    return new Preset
                    {
                        IsCa = true,
                        PathLengthConstraint = null,
                        KeyUsageBits = KeyUsage.KeyCertSign | KeyUsage.CrlSign,
                        ExtendedKeyUsages = null,
                        DefaultValidityDays = DefaultRootCAValidityDays,
                    };
                case RolePreset.IntermediateCA:
                    return new Preset
                    {
                        IsCa = true,
                        PathLengthConstraint = 0,
                        KeyUsageBits = KeyUsage.KeyCertSign | KeyUsage.CrlSign,
                        ExtendedKeyUsages = null,
                        DefaultValidityDays = DefaultIntermediateCAValidityDays,
                    };
                case RolePreset.HttpsServer:
                    return new Preset
                    {
                        IsCa = false,
                        PathLengthConstraint = null,
                        KeyUsageBits = KeyUsage.DigitalSignature | KeyUsage.KeyEncipherment,
                        ExtendedKeyUsages = new[] { KeyPurposeID.id_kp_serverAuth },
                        DefaultValidityDays = DefaultLeafValidityDays,
                    };
                case RolePreset.Dot1xClient:
                    return new Preset
                    {
                        IsCa = false,
                        PathLengthConstraint = null,
                        KeyUsageBits = KeyUsage.DigitalSignature,
                        ExtendedKeyUsages = new[] { KeyPurposeID.id_kp_clientAuth },
                        DefaultValidityDays = DefaultLeafValidityDays,
                    };
                case RolePreset.Service:
                    return new Preset
                    {
                        IsCa = false,
                        PathLengthConstraint = null,
                        KeyUsageBits = KeyUsage.DigitalSignature | KeyUsage.KeyEncipherment,
                        ExtendedKeyUsages = new[] { KeyPurposeID.id_kp_serverAuth, KeyPurposeID.id_kp_clientAuth },
                        DefaultValidityDays = DefaultLeafValidityDays,
                    };
                default:
                    return new Preset();
            }
        }

        public static string DisplayName(RolePreset role)
        {
            switch (role)
            {
                case RolePreset.RootCA: return "Root CA";
                case RolePreset.IntermediateCA: return "Intermediate CA";
                case RolePreset.HttpsServer: return "HTTPS server";
                case RolePreset.Dot1xClient: return "802.1X client";
                case RolePreset.Service: return "Service";
                default: return role.ToString();
            }
        }
    }
}
