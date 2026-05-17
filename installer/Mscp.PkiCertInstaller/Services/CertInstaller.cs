using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;

namespace Mscp.PkiCertInstaller.Services;

// Imports a Milestone-issued PFX into the appropriate Windows certificate
// store, following the rules from "MilestoneXProtect VMS products
// Certificates Guide" (2025 R1):
//
//   Root CA cert      -> LocalMachine\Root (Trusted Root CAs)
//   Intermediate CA   -> LocalMachine\CA   (Intermediate CAs)
//   Leaf w/ key       -> LocalMachine\My   (Personal) + key-file ACL
//
// Per the guide (p.36 / p.104 / p.126) the service identity that runs
// the XProtect services needs Full Control + Read on the private-key
// file. Default identity is NETWORK SERVICE; on domain installs admins
// often swap in a custom service account, so the caller passes the
// list of accounts to ACL.
//
// CA certs (Root / Intermediate) carry their private key in the PFX
// for re-issuing on the source machine, but on a deployment target
// only the public cert is needed - we strip the key before adding to
// the trust store so the ACL flow doesn't waste effort on it.
[SupportedOSPlatform("windows")]
public static class CertInstaller
{
    public sealed record InstallResult(
        string Thumbprint,
        StoreName Store,
        string KeyFilePath,
        string[] GrantedAccounts);

    // Maps the PKI plugin's kind labels onto the right Windows store.
    public static StoreName StoreForKind(string kindLabel) => kindLabel switch
    {
        "Root CA"         => StoreName.Root,
        "Intermediate CA" => StoreName.CertificateAuthority,
        _                 => StoreName.My,
    };

    // Friendly name for the UI, lining up with what certlm.msc shows.
    public static string StoreDisplayName(StoreName s) => s switch
    {
        StoreName.Root                 => @"LocalMachine\Trusted Root Certification Authorities",
        StoreName.CertificateAuthority => @"LocalMachine\Intermediate Certification Authorities",
        StoreName.My                   => @"LocalMachine\Personal",
        _                              => $"LocalMachine\\{s}",
    };

    // CA certs only need the public half on the target machine.
    public static bool NeedsKeyAcl(string kindLabel)
        => kindLabel != "Root CA" && kindLabel != "Intermediate CA";

    public static InstallResult InstallPfx(
        byte[] pfxBytes,
        string pfxPassword,
        StoreName targetStore,
        bool aclPrivateKey,
        string friendlyName,
        params string[] grantAccounts)
    {
        var flags = X509KeyStorageFlags.MachineKeySet
                  | X509KeyStorageFlags.PersistKeySet
                  | X509KeyStorageFlags.Exportable;
        using var fromPfx = X509CertificateLoader.LoadPkcs12(pfxBytes, pfxPassword ?? "", flags);

        // For trust-store imports we deliberately drop the key - the
        // target machine has no business holding a CA's private key.
        var toAdd = aclPrivateKey ? fromPfx : X509CertificateLoader.LoadCertificate(fromPfx.RawData);

        // Set the FriendlyName so the cert appears with a recognisable
        // label in certlm.msc / Server Configurator instead of "<None>".
        // Must be set BEFORE store.Add() because the persisted copy
        // copies its name from the in-memory cert.
        if (!string.IsNullOrWhiteSpace(friendlyName))
        {
            try { toAdd.FriendlyName = friendlyName; }
            catch (Exception ex)
            {
                // FriendlyName is a Windows-only nicety; skipping it
                // doesn't affect cert function. Logged so we know if a
                // platform/runtime ever stops supporting it.
                Log.Warn($"Couldn't set FriendlyName='{friendlyName}': {ex.GetType().Name}: {ex.Message}");
            }
        }

        using var store = new X509Store(targetStore, StoreLocation.LocalMachine);
        store.Open(OpenFlags.ReadWrite);
        var existing = store.Certificates.Find(X509FindType.FindByThumbprint, toAdd.Thumbprint, false);
        if (existing.Count > 0) store.RemoveRange(existing);
        store.Add(toAdd);

        // Belt-and-suspenders: ensure the FriendlyName is persisted on
        // the on-disk copy too. Some Windows builds reset it on Add().
        if (!string.IsNullOrWhiteSpace(friendlyName))
        {
            var persistedForName = store.Certificates
                .Find(X509FindType.FindByThumbprint, toAdd.Thumbprint, false)
                .OfType<X509Certificate2>().FirstOrDefault();
            if (persistedForName != null)
            {
                try { persistedForName.FriendlyName = friendlyName; }
                catch (Exception ex)
                {
                    Log.Warn($"Couldn't re-assert FriendlyName on persisted cert: {ex.GetType().Name}: {ex.Message}");
                }
            }
        }

        string keyPath = "";
        string[] granted = Array.Empty<string>();
        if (aclPrivateKey && fromPfx.HasPrivateKey)
        {
            // Refetch from the store - the freshly persisted cert has
            // the durable key handle that LocateKeyFile relies on.
            var persisted = store.Certificates
                .Find(X509FindType.FindByThumbprint, toAdd.Thumbprint, false)
                .OfType<X509Certificate2>()
                .FirstOrDefault() ?? fromPfx;
            keyPath = LocateKeyFile(persisted) ?? "";
            if (!string.IsNullOrEmpty(keyPath))
                granted = GrantFullControlOnKeyFile(keyPath, grantAccounts);
        }

        return new InstallResult(toAdd.Thumbprint, targetStore, keyPath, granted);
    }

    public static bool IsInstalled(string thumbprint, StoreName store)
        => GetState(thumbprint, store).installed;

    public static (bool installed, bool hasKey) GetState(string thumbprint, StoreName storeName)
    {
        if (string.IsNullOrEmpty(thumbprint)) return (false, false);
        using var store = new X509Store(storeName, StoreLocation.LocalMachine);
        store.Open(OpenFlags.ReadOnly);
        var hits = store.Certificates.Find(X509FindType.FindByThumbprint, thumbprint, false);
        if (hits.Count == 0) return (false, false);
        return (true, hits[0].HasPrivateKey);
    }

    public static bool RemoveByThumbprint(string thumbprint, StoreName storeName)
    {
        if (string.IsNullOrEmpty(thumbprint)) return false;
        using var store = new X509Store(storeName, StoreLocation.LocalMachine);
        store.Open(OpenFlags.ReadWrite);
        var hits = store.Certificates.Find(X509FindType.FindByThumbprint, thumbprint, false);
        if (hits.Count == 0) return false;
        store.RemoveRange(hits);
        return true;
    }

    // Three probes in order: RSA-CNG, ECDSA-CNG, legacy RSA-CSP. Each
    // probe can throw if the cert's key handle isn't of the expected
    // shape - that's expected, NOT an error, since we're trying every
    // possible storage backend. The catches log at INFO level only so
    // an admin with a unfamiliar cert layout can correlate "ACL grant
    // skipped" with the underlying handle-resolution failure.
    private static string? LocateKeyFile(X509Certificate2 cert)
    {
        try
        {
            using var rsa = cert.GetRSAPrivateKey();
            if (rsa is RSACng cng)
            {
                var unique = cng.Key.UniqueName;
                if (!string.IsNullOrEmpty(unique))
                    return FindOnDisk("Microsoft\\Crypto\\Keys", unique);
            }
        }
        catch (Exception ex)
        {
            Log.Info($"LocateKeyFile (RSA-CNG) probe failed: {ex.GetType().Name}: {ex.Message}");
        }

        try
        {
            using var ecdsa = cert.GetECDsaPrivateKey();
            if (ecdsa is ECDsaCng ecng)
            {
                var unique = ecng.Key.UniqueName;
                if (!string.IsNullOrEmpty(unique))
                    return FindOnDisk("Microsoft\\Crypto\\Keys", unique);
            }
        }
        catch (Exception ex)
        {
            Log.Info($"LocateKeyFile (ECDSA-CNG) probe failed: {ex.GetType().Name}: {ex.Message}");
        }

        try
        {
#pragma warning disable SYSLIB0028
            using var rsa = cert.GetRSAPrivateKey();
            if (rsa is RSACryptoServiceProvider csp)
            {
                var unique = csp.CspKeyContainerInfo.UniqueKeyContainerName;
                if (!string.IsNullOrEmpty(unique))
                    return FindOnDisk("Microsoft\\Crypto\\RSA\\MachineKeys", unique);
            }
#pragma warning restore SYSLIB0028
        }
        catch (Exception ex)
        {
            Log.Info($"LocateKeyFile (legacy CSP) probe failed: {ex.GetType().Name}: {ex.Message}");
        }

        Log.Warn($"LocateKeyFile returned null for {cert.Thumbprint} - private-key ACL grant will be skipped, the service may not be able to read the key.");
        return null;
    }

    private static string? FindOnDisk(string subPath, string fileName)
    {
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            @"C:\ProgramData",
        };
        foreach (var r in roots)
        {
            if (string.IsNullOrEmpty(r)) continue;
            var p = Path.Combine(r, subPath, fileName);
            if (File.Exists(p)) return p;
        }
        return null;
    }

    // Milestone's Certificates Guide (p.36, p.104, p.126) explicitly
    // says the service-account user needs "Full Control and Read" on
    // the cert's private key file - so we grant FullControl, which
    // implies Read. Per-account failures are non-fatal: we just skip
    // the bad ones and report what stuck.
    private static string[] GrantFullControlOnKeyFile(string keyPath, string[] grantAccounts)
    {
        var success = new List<string>();
        var info = new FileInfo(keyPath);
        var sec = info.GetAccessControl();
        foreach (var acct in grantAccounts ?? Array.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(acct)) continue;
            try
            {
                var ident = new NTAccount(acct);
                ident.Translate(typeof(SecurityIdentifier));
                sec.AddAccessRule(new FileSystemAccessRule(
                    ident, FileSystemRights.FullControl, AccessControlType.Allow));
                success.Add(acct);
            }
            catch (Exception ex)
            {
                // Per-account failures are non-fatal (typo, deleted user,
                // domain unreachable) - skip and continue. Logged so an
                // admin can see which accounts didn't get granted.
                Log.Warn($"GrantFullControl skipped account '{acct}': {ex.GetType().Name}: {ex.Message}");
            }
        }
        try
        {
            info.SetAccessControl(sec);
        }
        catch (Exception ex)
        {
            Log.Error($"SetAccessControl on '{keyPath}' failed - service account may not be able to read the private key.", ex);
            throw;
        }
        if (success.Count == 0 && grantAccounts != null && grantAccounts.Length > 0)
            Log.Warn("GrantFullControl: 0 of " + grantAccounts.Length + " requested accounts succeeded; review the warnings above.");
        return success.ToArray();
    }
}
