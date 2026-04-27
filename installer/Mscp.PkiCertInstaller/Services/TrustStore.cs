using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;

namespace Mscp.PkiCertInstaller.Services;

// Per-user file of TLS thumbprints the admin has explicitly accepted
// for individual Mgmt Server hosts. Keyed by "host:port" (lowercased)
// so the same hostname can have different ports / different certs.
//
// File format is a flat JSON map; we keep it simple so an ops admin
// can read or hand-edit it. Stored under %LOCALAPPDATA% so it never
// requires elevated rights and never travels with roaming profiles.
//
// This is a TRUST-ON-FIRST-USE pattern: a real attacker can still
// trick a first-time admin into clicking "Trust", but every later
// connection to the same host fails loud if the cert thumbprint
// differs (cert reissued OR active MITM).
public sealed class TrustStore
{
    private readonly string _path;
    private readonly ConcurrentDictionary<string, string> _map = new(StringComparer.OrdinalIgnoreCase);

    public TrustStore()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MSCP", "PkiCertInstaller");
        try { Directory.CreateDirectory(dir); } catch { }
        _path = Path.Combine(dir, "trusted_servers.json");
        Load();
    }

    public string FilePath => _path;

    public bool IsTrusted(string host, int port, string thumbprintHex)
    {
        if (string.IsNullOrEmpty(host) || string.IsNullOrEmpty(thumbprintHex)) return false;
        return _map.TryGetValue(Key(host, port), out var stored) &&
               string.Equals(stored, NormalizeThumbprint(thumbprintHex),
                             StringComparison.OrdinalIgnoreCase);
    }

    public void Trust(string host, int port, string thumbprintHex)
    {
        if (string.IsNullOrEmpty(host) || string.IsNullOrEmpty(thumbprintHex)) return;
        _map[Key(host, port)] = NormalizeThumbprint(thumbprintHex);
        Save();
    }

    private static string Key(string host, int port) =>
        $"{host.ToLowerInvariant()}:{port}";

    // SHA-1 thumbprints are commonly rendered with colons or spaces;
    // strip them and uppercase so equality is stable regardless of
    // who wrote the file.
    private static string NormalizeThumbprint(string s) =>
        s.Replace(":", "").Replace(" ", "").Trim().ToUpperInvariant();

    private void Load()
    {
        try
        {
            if (!File.Exists(_path)) return;
            var json = File.ReadAllText(_path);
            var loaded = JsonSerializer.Deserialize<System.Collections.Generic.Dictionary<string, string>>(json);
            if (loaded == null) return;
            foreach (var kvp in loaded)
                _map[kvp.Key] = NormalizeThumbprint(kvp.Value ?? "");
        }
        catch
        {
            // Corrupt store → start fresh. Worst case the admin gets
            // re-prompted on next connect.
        }
    }

    private void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(_map,
                new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_path, json);
        }
        catch
        {
            // Disk-full or perms; the in-memory copy still works for
            // this session.
        }
    }
}
