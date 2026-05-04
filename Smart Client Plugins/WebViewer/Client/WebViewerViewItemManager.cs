using System;
using System.Security.Cryptography;
using System.Text;
using CommunitySDK;
using VideoOS.Platform.Client;

namespace WebViewer.Client
{
    public class WebViewerViewItemManager : ViewItemManager
    {
        private static readonly PluginLog _log = new PluginLog("WebViewer");

        // Property keys stored on the MIP item.
        private const string TitleKey = "Title";
        private const string ShowTitleKey = "ShowTitle";
        private const string UrlKey = "Url";
        private const string UsernameKey = "Username";
        private const string PasswordKey = "Password"; // DPAPI-encrypted (CurrentUser)
        private const string AutoAcceptCertsKey = "AutoAcceptCerts";
        private const string AutoLoginKey = "AutoLogin";

        public WebViewerViewItemManager() : base("WebViewerViewItemManager") { }

        public string Title
        {
            get => GetProperty(TitleKey) ?? "";
            set => SetProperty(TitleKey, value ?? "");
        }

        // Default true: title row shows when a title is set.
        public bool ShowTitle
        {
            get
            {
                var v = GetProperty(ShowTitleKey);
                return string.IsNullOrEmpty(v) || string.Equals(v, "true", StringComparison.OrdinalIgnoreCase);
            }
            set => SetProperty(ShowTitleKey, value ? "true" : "false");
        }

        public string Url
        {
            get => GetProperty(UrlKey) ?? "";
            set => SetProperty(UrlKey, value ?? "");
        }

        public string Username
        {
            get => GetProperty(UsernameKey) ?? "";
            set => SetProperty(UsernameKey, value ?? "");
        }

        // Returns the decrypted password or empty. Setting takes the plain password
        // and DPAPI-encrypts it under the current Windows user.
        public string Password
        {
            get
            {
                var enc = GetProperty(PasswordKey);
                return string.IsNullOrEmpty(enc) ? "" : DecryptPassword(enc);
            }
            set => SetProperty(PasswordKey, string.IsNullOrEmpty(value) ? "" : EncryptPassword(value));
        }

        // Default true: tolerate self-signed / hostname-mismatch certs (the typical
        // case for in-house dashboards). Users who care can flip this off.
        public bool AutoAcceptCerts
        {
            get
            {
                var v = GetProperty(AutoAcceptCertsKey);
                return string.IsNullOrEmpty(v) || string.Equals(v, "true", StringComparison.OrdinalIgnoreCase);
            }
            set => SetProperty(AutoAcceptCertsKey, value ? "true" : "false");
        }

        // Default true: if creds are configured, auto-fill the basic-auth prompt.
        public bool AutoLogin
        {
            get
            {
                var v = GetProperty(AutoLoginKey);
                return string.IsNullOrEmpty(v) || string.Equals(v, "true", StringComparison.OrdinalIgnoreCase);
            }
            set => SetProperty(AutoLoginKey, value ? "true" : "false");
        }

        public void Save() => SaveProperties();
        public override void PropertiesLoaded() { }

        public override ViewItemWpfUserControl GenerateViewItemWpfUserControl()
            => new WebViewerViewItemWpfUserControl(this);

        // DPAPI helpers — same shape as RemoteManager so credentials never sit
        // in clear text in the saved view config.
        private static string EncryptPassword(string plain)
        {
            try
            {
                var enc = ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), null, DataProtectionScope.CurrentUser);
                return Convert.ToBase64String(enc);
            }
            catch (Exception ex) { _log.Error("DPAPI encrypt failed", ex); return ""; }
        }

        private static string DecryptPassword(string encB64)
        {
            try
            {
                var enc = Convert.FromBase64String(encB64);
                var bytes = ProtectedData.Unprotect(enc, null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(bytes);
            }
            catch (Exception ex) { _log.Error("DPAPI decrypt failed", ex); return ""; }
        }
    }
}
