using System;
using System.IO;
using System.Xml.Linq;
using CommunitySDK;

namespace RemoteManager
{
    /// <summary>
    /// Machine-wide config persisted to %ProgramData%\Milestone\RemoteManager\config.xml.
    /// Shared by every Smart Client user on the host. Plugin is intended for admin installs only.
    /// </summary>
    static class RemoteManagerConfig
    {
        private static readonly PluginLog _log = new PluginLog("RemoteManager");
        private static readonly object _fileLock = new object();

        private static readonly string _configPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Milestone", "RemoteManager", "config.xml");

        /// <summary>
        /// When true, the credentials toolbar (username/password/copy/show) is never shown.
        /// Default: false (toolbar visible — plugin is admin-only, see deployment docs).
        /// </summary>
        public static bool HideCredentialBar { get; set; } = false;

        public static void Load()
        {
            lock (_fileLock)
            {
                if (!File.Exists(_configPath))
                {
                    Save();
                    return;
                }

                try
                {
                    var doc = XDocument.Load(_configPath);
                    var root = doc.Root;
                    if (root == null) return;

                    var hideEl = root.Element("HideCredentialBar");
                    if (hideEl != null && bool.TryParse(hideEl.Value, out var hide))
                        HideCredentialBar = hide;
                }
                catch (Exception ex)
                {
                    _log.Error("Failed to load RemoteManager config, using defaults", ex);
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
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                        Directory.CreateDirectory(dir);

                    var doc = new XDocument(
                        new XElement("RemoteManagerConfig",
                            new XElement("HideCredentialBar", HideCredentialBar)));
                    doc.Save(_configPath);
                }
                catch (Exception ex)
                {
                    _log.Error("Failed to save RemoteManager config", ex);
                }
            }
        }
    }
}
