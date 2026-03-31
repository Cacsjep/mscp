using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using WebView.Models;
using VideoOS.Platform.Client;

namespace WebView.Client
{
    public class WebViewViewItemManager : ViewItemManager
    {
        private const string AutoAcceptCertsKey = "AutoAcceptCerts";
        private const string UserEntriesKey = "UserEntries";

        public WebViewViewItemManager() : base("WebViewViewItemManager") { }

        public bool AutoAcceptCerts
        {
            get
            {
                var val = GetProperty(AutoAcceptCertsKey);
                return string.IsNullOrEmpty(val) || val == "True";
            }
            set => SetProperty(AutoAcceptCertsKey, value.ToString());
        }

        /// <summary>
        /// Format: "name|url|username|encryptedBase64Password;..."
        /// Fields 3 and 4 are optional (empty string if not set).
        /// Password is encrypted with DPAPI (CurrentUser scope).
        /// </summary>
        public List<HardwareDeviceInfo> GetUserEntries()
        {
            var raw = GetProperty(UserEntriesKey);
            if (string.IsNullOrEmpty(raw)) return new List<HardwareDeviceInfo>();

            return raw.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(entry =>
                {
                    var parts = entry.Split(new[] { '|' }, 4);
                    if (parts.Length < 2) return null;

                    var info = new HardwareDeviceInfo
                    {
                        Name = parts[0],
                        Address = parts[1],
                        IsUserDefined = true,
                        HardwareId = GenerateStableId(parts[1]),
                        RecordingServerName = "User Defined",
                        Username = parts.Length > 2 && !string.IsNullOrEmpty(parts[2]) ? parts[2] : null,
                    };

                    // Decrypt password with DPAPI
                    if (parts.Length > 3 && !string.IsNullOrEmpty(parts[3]))
                    {
                        info.Password = DecryptPassword(parts[3]);
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
                    return $"{e.Name}|{e.Address}|{user}|{encPwd}";
                }));
            SetProperty(UserEntriesKey, raw);
        }

        internal static Guid GenerateStableId(string url)
        {
            using (var md5 = System.Security.Cryptography.MD5.Create())
            {
                var bytes = Encoding.UTF8.GetBytes(url ?? "");
                return new Guid(md5.ComputeHash(bytes));
            }
        }

        private static string EncryptPassword(string plainText)
        {
            try
            {
                var bytes = Encoding.UTF8.GetBytes(plainText);
                var encrypted = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
                return Convert.ToBase64String(encrypted);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WebView] DPAPI encrypt failed: {ex.Message}");
                return "";
            }
        }

        private static string DecryptPassword(string encryptedBase64)
        {
            try
            {
                var encrypted = Convert.FromBase64String(encryptedBase64);
                var bytes = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(bytes);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WebView] DPAPI decrypt failed: {ex.Message}");
                return null;
            }
        }

        public void Save() => SaveProperties();
        public override void PropertiesLoaded() { }

        public override ViewItemWpfUserControl GenerateViewItemWpfUserControl()
            => new WebViewViewItemWpfUserControl(this);

        public override PropertiesWpfUserControl GeneratePropertiesWpfUserControl()
            => new WebViewPropertiesWpfUserControl(this);
    }
}
