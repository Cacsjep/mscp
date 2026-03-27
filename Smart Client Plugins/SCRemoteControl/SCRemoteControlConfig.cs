using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

namespace SCRemoteControl
{
    static class SCRemoteControlConfig
    {
        private static readonly object _fileLock = new object();
        private static readonly string _configPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Milestone", "SCRemoteControl", "config.xml");

        public static string ListenAddress { get; set; } = "127.0.0.1";
        public static int Port { get; set; } = 9500;
        public static bool UseTls { get; set; }
        public static string PfxPath { get; set; } = string.Empty;
        public static string PfxPassword { get; set; } = string.Empty;
        public static List<ApiToken> ApiTokens { get; set; } = new List<ApiToken>();

        public static void Load()
        {
            lock (_fileLock)
            {
                if (!File.Exists(_configPath))
                {
                    ApiTokens.Add(new ApiToken { Name = "default", Value = GenerateToken() });
                    Save();
                    return;
                }

                try
                {
                    var doc = XDocument.Load(_configPath);
                    var root = doc.Root;
                    if (root == null) return;

                    ListenAddress = root.Element("ListenAddress")?.Value ?? "127.0.0.1";

                    var portEl = root.Element("Port");
                    Port = portEl != null && int.TryParse(portEl.Value, out var p) && p >= 1024 && p <= 65535 ? p : 9500;

                    var tlsEl = root.Element("UseTls");
                    UseTls = tlsEl != null && bool.TryParse(tlsEl.Value, out var tls) && tls;

                    PfxPath = root.Element("PfxPath")?.Value ?? string.Empty;
                    PfxPassword = UnprotectString(root.Element("PfxPasswordProtected")?.Value);

                    ApiTokens.Clear();
                    var tokensEl = root.Element("ApiTokens");
                    if (tokensEl != null)
                    {
                        foreach (var tokenEl in tokensEl.Elements("Token"))
                        {
                            var name = tokenEl.Attribute("Name")?.Value;
                            var protectedValue = tokenEl.Attribute("ValueProtected")?.Value;
                            var plainValue = tokenEl.Attribute("Value")?.Value; // migration from old format
                            var value = !string.IsNullOrEmpty(protectedValue)
                                ? UnprotectString(protectedValue)
                                : plainValue;

                            if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(value))
                                ApiTokens.Add(new ApiToken { Name = name, Value = value });
                        }
                    }

                    if (ApiTokens.Count == 0)
                        ApiTokens.Add(new ApiToken { Name = "default", Value = GenerateToken() });

                    SCRemoteControlDefinition.Log.Info($"Configuration loaded: {(UseTls ? "https" : "http")}://{ListenAddress}:{Port}");
                }
                catch (Exception ex)
                {
                    SCRemoteControlDefinition.Log.Error("Failed to load configuration, using defaults", ex);
                    ListenAddress = "127.0.0.1";
                    Port = 9500;
                    UseTls = false;
                    PfxPath = string.Empty;
                    PfxPassword = string.Empty;
                    if (ApiTokens.Count == 0)
                        ApiTokens.Add(new ApiToken { Name = "default", Value = GenerateToken() });
                }
            }
        }

        public static void Save()
        {
            lock (_fileLock)
            {
                try
                {
                    var dir = Path.GetDirectoryName(_configPath);
                    if (!Directory.Exists(dir))
                        Directory.CreateDirectory(dir);

                    var tokensEl = new XElement("ApiTokens");
                    foreach (var token in ApiTokens)
                    {
                        tokensEl.Add(new XElement("Token",
                            new XAttribute("Name", token.Name ?? ""),
                            new XAttribute("ValueProtected", ProtectString(token.Value))));
                    }

                    var root = new XElement("SCRemoteControlConfig",
                        new XElement("ListenAddress", ListenAddress),
                        new XElement("Port", Port),
                        new XElement("UseTls", UseTls),
                        new XElement("PfxPath", PfxPath),
                        new XElement("PfxPasswordProtected", ProtectString(PfxPassword)),
                        tokensEl);

                    var doc = new XDocument(root);
                    doc.Save(_configPath);
                    SCRemoteControlDefinition.Log.Info("Configuration saved");
                }
                catch (Exception ex)
                {
                    SCRemoteControlDefinition.Log.Error("Failed to save configuration", ex);
                }
            }
        }

        public static bool ValidateToken(string token)
        {
            if (string.IsNullOrEmpty(token)) return false;
            lock (_fileLock)
            {
                foreach (var t in ApiTokens)
                {
                    if (FixedTimeEquals(t.Value, token))
                        return true;
                }
                return false;
            }
        }

        public static string GenerateToken()
        {
            var bytes = new byte[32];
            using (var rng = RandomNumberGenerator.Create())
                rng.GetBytes(bytes);
            return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
        }

        /// <summary>Constant-time string comparison to prevent timing attacks.</summary>
        private static bool FixedTimeEquals(string a, string b)
        {
            if (a == null || b == null) return false;
            var bytesA = Encoding.UTF8.GetBytes(a);
            var bytesB = Encoding.UTF8.GetBytes(b);

            // Always compare full length to avoid leaking length info
            int diff = bytesA.Length ^ bytesB.Length;
            int len = Math.Min(bytesA.Length, bytesB.Length);
            for (int i = 0; i < len; i++)
                diff |= bytesA[i] ^ bytesB[i];
            return diff == 0;
        }

        /// <summary>Encrypt string with DPAPI (machine-scoped).</summary>
        private static string ProtectString(string plainText)
        {
            if (string.IsNullOrEmpty(plainText)) return string.Empty;
            try
            {
                var bytes = Encoding.UTF8.GetBytes(plainText);
                var protectedBytes = ProtectedData.Protect(bytes, null, DataProtectionScope.LocalMachine);
                return Convert.ToBase64String(protectedBytes);
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>Decrypt string with DPAPI (machine-scoped).</summary>
        private static string UnprotectString(string protectedText)
        {
            if (string.IsNullOrEmpty(protectedText)) return string.Empty;
            try
            {
                var protectedBytes = Convert.FromBase64String(protectedText);
                var bytes = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.LocalMachine);
                return Encoding.UTF8.GetString(bytes);
            }
            catch
            {
                return string.Empty;
            }
        }
    }

    class ApiToken
    {
        public string Name { get; set; }
        public string Value { get; set; }
    }
}
