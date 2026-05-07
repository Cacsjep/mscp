using System;
using System.IO;
using System.Xml.Linq;

namespace TimelineJump
{
    static class TimelineJumpConfig
    {
        private static readonly object _fileLock = new object();
        private static readonly string _configPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Milestone", "TimelineJump", "config.xml");

        public static bool JumpToCurrentOnPlayback { get; set; } = true;

        public static void Load()
        {
            lock (_fileLock)
            {
                if (!File.Exists(_configPath))
                {
                    JumpToCurrentOnPlayback = true;
                    return;
                }

                try
                {
                    var doc = XDocument.Load(_configPath);
                    var root = doc.Root;
                    if (root == null) return;

                    var el = root.Element("JumpToCurrentOnPlayback");
                    JumpToCurrentOnPlayback = el == null
                        || !bool.TryParse(el.Value, out var v) || v;
                }
                catch (Exception ex)
                {
                    TimelineJumpDefinition.Log.Error("Failed to load configuration, using defaults", ex);
                    JumpToCurrentOnPlayback = true;
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

                    var root = new XElement("TimelineJumpConfig",
                        new XElement("JumpToCurrentOnPlayback", JumpToCurrentOnPlayback));
                    new XDocument(root).Save(_configPath);
                }
                catch (Exception ex)
                {
                    TimelineJumpDefinition.Log.Error("Failed to save configuration", ex);
                }
            }
        }
    }
}
