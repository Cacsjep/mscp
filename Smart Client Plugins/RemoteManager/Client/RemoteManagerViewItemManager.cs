using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RemoteManager.Models;
using VideoOS.Platform.Client;

namespace RemoteManager.Client
{
    public class RemoteManagerViewItemManager : ViewItemManager
    {
        private const string AutoAcceptCertsKey = "AutoAcceptCerts";
        private const string UserEntriesKey = "UserEntries";
        private const string RdpEntriesKey = "RdpEntries";
        private const string TreeStructureKey = "TreeStructure";

        public RemoteManagerViewItemManager() : base("RemoteManagerViewItemManager") { }

        public bool AutoAcceptCerts
        {
            get
            {
                var val = GetProperty(AutoAcceptCertsKey);
                return string.IsNullOrEmpty(val) || val == "True";
            }
            set => SetProperty(AutoAcceptCertsKey, value.ToString());
        }

        #region Web View User Entries

        /// <summary>
        /// Format: "id|name|url|username|encryptedBase64Password;..."
        /// </summary>
        public List<HardwareDeviceInfo> GetUserEntries()
        {
            var raw = GetProperty(UserEntriesKey);
            if (string.IsNullOrEmpty(raw)) return new List<HardwareDeviceInfo>();

            return raw.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(entry =>
                {
                    var parts = entry.Split(new[] { '|' }, 5);
                    if (parts.Length < 2) return null;

                    // Support old format (no id field) by checking if first field is a GUID
                    int offset = 0;
                    Guid id;
                    if (Guid.TryParse(parts[0], out id))
                    {
                        offset = 1;
                    }
                    else
                    {
                        id = Guid.NewGuid();
                    }

                    var info = new HardwareDeviceInfo
                    {
                        HardwareId = id,
                        Name = parts[offset],
                        Address = parts.Length > offset + 1 ? parts[offset + 1] : "",
                        IsUserDefined = true,
                        RecordingServerName = "User Defined",
                        Username = parts.Length > offset + 2 && !string.IsNullOrEmpty(parts[offset + 2]) ? parts[offset + 2] : null,
                    };

                    if (parts.Length > offset + 3 && !string.IsNullOrEmpty(parts[offset + 3]))
                    {
                        info.Password = DecryptPassword(parts[offset + 3]);
                    }

                    return info;
                })
                .Where(e => e != null)
                .ToList();
        }

        public void SetUserEntries(List<HardwareDeviceInfo> entries)
        {
            var raw = string.Join(";", entries
                .Where(e => e.IsUserDefined)
                .Select(e =>
                {
                    var user = e.Username ?? "";
                    var encPwd = !string.IsNullOrEmpty(e.Password) ? EncryptPassword(e.Password) : "";
                    return $"{e.HardwareId}|{e.Name}|{e.Address}|{user}|{encPwd}";
                }));
            SetProperty(UserEntriesKey, raw);
        }

        #endregion

        #region RDP Entries

        /// <summary>
        /// Format: "id|name|host|port|username|encryptedBase64Password|enableNLA|enableClipboard;..."
        /// </summary>
        public List<RdpConnectionInfo> GetRdpEntries()
        {
            var raw = GetProperty(RdpEntriesKey);
            if (string.IsNullOrEmpty(raw)) return new List<RdpConnectionInfo>();

            return raw.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(entry =>
                {
                    var parts = entry.Split(new[] { '|' }, 8);
                    if (parts.Length < 3) return null;

                    // Support old format (no id field) by checking if first field is a GUID
                    int offset = 0;
                    Guid id;
                    if (Guid.TryParse(parts[0], out id))
                    {
                        offset = 1; // new format with ID
                    }
                    else
                    {
                        id = Guid.NewGuid(); // old format, generate new ID
                    }

                    var info = new RdpConnectionInfo
                    {
                        Id = id,
                        Name = parts[offset],
                        Host = parts[offset + 1],
                    };

                    if (parts.Length > offset + 2 && int.TryParse(parts[offset + 2], out int port))
                        info.Port = port;

                    if (parts.Length > offset + 3 && !string.IsNullOrEmpty(parts[offset + 3]))
                        info.Username = parts[offset + 3];

                    if (parts.Length > offset + 4 && !string.IsNullOrEmpty(parts[offset + 4]))
                        info.Password = DecryptPassword(parts[offset + 4]);

                    if (parts.Length > offset + 5)
                        info.EnableNLA = parts[offset + 5] == "True";

                    if (parts.Length > offset + 6)
                        info.EnableClipboard = parts[offset + 6] == "True";
                    else
                        info.EnableClipboard = true;

                    return info;
                })
                .Where(e => e != null)
                .ToList();
        }

        public void SetRdpEntries(List<RdpConnectionInfo> entries)
        {
            var raw = string.Join(";", entries.Select(e =>
            {
                var user = e.Username ?? "";
                var encPwd = !string.IsNullOrEmpty(e.Password) ? EncryptPassword(e.Password) : "";
                return $"{e.Id}|{e.Name}|{e.Host}|{e.Port}|{user}|{encPwd}|{e.EnableNLA}|{e.EnableClipboard}";
            }));
            SetProperty(RdpEntriesKey, raw);
        }

        #endregion

        #region Tree Structure

        public TreeStructure GetTreeStructure()
        {
            var raw = GetProperty(TreeStructureKey);
            if (string.IsNullOrEmpty(raw)) return new TreeStructure();

            try
            {
                return JsonSerializer.Deserialize<TreeStructure>(raw) ?? new TreeStructure();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[RemoteManager] Failed to deserialize tree structure: {ex.Message}");
                return new TreeStructure();
            }
        }

        public void SetTreeStructure(TreeStructure tree)
        {
            try
            {
                var json = JsonSerializer.Serialize(tree);
                SetProperty(TreeStructureKey, json);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[RemoteManager] Failed to serialize tree structure: {ex.Message}");
            }
        }

        #endregion

        #region Helpers

        internal static Guid GenerateStableId(string input)
        {
            using (var md5 = System.Security.Cryptography.MD5.Create())
            {
                var bytes = Encoding.UTF8.GetBytes(input ?? "");
                return new Guid(md5.ComputeHash(bytes));
            }
        }

        internal static string EncryptPassword(string plainText)
        {
            try
            {
                var bytes = Encoding.UTF8.GetBytes(plainText);
                var encrypted = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
                return Convert.ToBase64String(encrypted);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[RemoteManager] DPAPI encrypt failed: {ex.Message}");
                return "";
            }
        }

        internal static string DecryptPassword(string encryptedBase64)
        {
            try
            {
                var encrypted = Convert.FromBase64String(encryptedBase64);
                var bytes = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(bytes);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[RemoteManager] DPAPI decrypt failed: {ex.Message}");
                return null;
            }
        }

        #endregion

        public void Save() => SaveProperties();
        public override void PropertiesLoaded() { }

        public override ViewItemWpfUserControl GenerateViewItemWpfUserControl()
            => new RemoteManagerViewItemWpfUserControl(this);

        public override PropertiesWpfUserControl GeneratePropertiesWpfUserControl()
            => new RemoteManagerPropertiesWpfUserControl(this);
    }
}
