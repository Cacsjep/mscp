using System;

namespace PKI.Storage
{
    // Stores PFX bytes (cert + private key + chain) as base64 in a MIP item
    // Property. Not encrypted at this layer: the Milestone config DB plus
    // HTTPS+IDP auth on the Mgmt Server are the trust boundary, identical
    // to how camera passwords and other VMS secrets are persisted.
    public static class CertVault
    {
        public static string ToBase64(byte[] plain)
            => (plain == null || plain.Length == 0) ? "" : Convert.ToBase64String(plain);

        public static byte[] FromBase64(string b64)
            => string.IsNullOrEmpty(b64) ? null : Convert.FromBase64String(b64);
    }
}
