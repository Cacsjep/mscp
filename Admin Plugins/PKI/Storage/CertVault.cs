using System;
using System.Security.Cryptography;
using System.Text;

namespace PKI.Storage
{
    // Stores the PFX bytes (cert + private key + chain) encrypted via Windows
    // DPAPI machine-scope, base64'd into a MIP item Property. Local to the
    // machine where the Management Client / Management Server runs (DPAPI
    // machine scope = any process on this machine can decrypt; another
    // machine cannot).
    //
    // Pragmatic v1 storage - the original design called for Windows cert
    // store on the Management Server. That move comes in the Storage / Cert
    // store / Background plugin task; for now keys travel with the MIP
    // configuration so the form flow can be exercised end-to-end without
    // needing the Background plugin wired up.
    public static class CertVault
    {
        // Optional entropy pinned to the plugin so other DPAPI consumers on
        // the same machine can't read these blobs by accident.
        private static readonly byte[] _entropy = Encoding.UTF8.GetBytes("MSCP.PKI.v1");

        public static string EncryptToBase64(byte[] plain)
        {
            if (plain == null || plain.Length == 0) return "";
            var ct = ProtectedData.Protect(plain, _entropy, DataProtectionScope.LocalMachine);
            return Convert.ToBase64String(ct);
        }

        public static byte[] DecryptFromBase64(string b64)
        {
            if (string.IsNullOrEmpty(b64)) return null;
            var ct = Convert.FromBase64String(b64);
            return ProtectedData.Unprotect(ct, _entropy, DataProtectionScope.LocalMachine);
        }
    }
}
