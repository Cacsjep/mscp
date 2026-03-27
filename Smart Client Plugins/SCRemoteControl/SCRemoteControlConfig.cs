using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Xml.Linq;

namespace SCRemoteControl
{
    static class SCRemoteControlConfig
    {
        private static readonly object _fileLock = new object();
        private static readonly string _configPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Milestone", "SCRemoteControl", "config.xml");

        public static string ListenAddress { get; set; } = "0.0.0.0";
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

                    var addrEl = root.Element("ListenAddress");
                    ListenAddress = addrEl?.Value ?? "0.0.0.0";

                    var portEl = root.Element("Port");
                    Port = portEl != null && int.TryParse(portEl.Value, out var p) && p >= 1024 && p <= 65535 ? p : 9500;

                    var tlsEl = root.Element("UseTls");
                    UseTls = tlsEl != null && bool.TryParse(tlsEl.Value, out var tls) && tls;

                    PfxPath = root.Element("PfxPath")?.Value ?? string.Empty;
                    PfxPassword = root.Element("PfxPassword")?.Value ?? string.Empty;

                    ApiTokens.Clear();
                    var tokensEl = root.Element("ApiTokens");
                    if (tokensEl != null)
                    {
                        foreach (var tokenEl in tokensEl.Elements("Token"))
                        {
                            var name = tokenEl.Attribute("Name")?.Value;
                            var value = tokenEl.Attribute("Value")?.Value;
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
                    ListenAddress = "0.0.0.0";
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
                            new XAttribute("Value", token.Value ?? "")));
                    }

                    var root = new XElement("SCRemoteControlConfig",
                        new XElement("ListenAddress", ListenAddress),
                        new XElement("Port", Port),
                        new XElement("UseTls", UseTls),
                        new XElement("PfxPath", PfxPath),
                        new XElement("PfxPassword", PfxPassword),
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
            return ApiTokens.Any(t => string.Equals(t.Value, token, StringComparison.Ordinal));
        }

        public static string GenerateToken()
        {
            var bytes = new byte[16];
            using (var rng = RandomNumberGenerator.Create())
                rng.GetBytes(bytes);
            return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
        }
    }

    class ApiToken
    {
        public string Name { get; set; }
        public string Value { get; set; }
    }
}
