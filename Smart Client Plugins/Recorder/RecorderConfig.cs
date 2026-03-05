using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Xml.Linq;

namespace Recorder
{
    public class RecorderConfig
    {
        private static readonly string ConfigDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Milestone", "Recorder");

        private static readonly string ConfigPath = Path.Combine(ConfigDir, "config.xml");

        private static readonly object _fileLock = new object();

        public HashSet<string> EnabledMonitors { get; set; } = new HashSet<string>();

        public string RtmpUrl { get; set; } = "";

        public static RecorderConfig Load()
        {
            lock (_fileLock)
            {
                var config = new RecorderConfig();
                if (!File.Exists(ConfigPath))
                    return config;

                try
                {
                    var doc = XDocument.Load(ConfigPath);
                    var monitors = doc.Root?.Element("EnabledMonitors");
                    if (monitors != null)
                    {
                        config.EnabledMonitors = new HashSet<string>(
                            monitors.Elements("Monitor").Select(e => e.Value));
                    }

                    var rtmpEl = doc.Root?.Element("RtmpUrl");
                    if (rtmpEl != null)
                        config.RtmpUrl = rtmpEl.Value;
                }
                catch
                {
                    // Corrupt config — return defaults
                }
                return config;
            }
        }

        public void Save()
        {
            lock (_fileLock)
            {
                Directory.CreateDirectory(ConfigDir);
                var doc = new XDocument(
                    new XElement("RecorderConfig",
                        new XElement("EnabledMonitors",
                            EnabledMonitors.Select(m => new XElement("Monitor", m))),
                        new XElement("RtmpUrl", RtmpUrl ?? "")));
                doc.Save(ConfigPath);
            }
        }

        public bool IsMonitorEnabled(Screen screen)
        {
            if (EnabledMonitors.Count == 0) return true;
            return EnabledMonitors.Contains(screen.DeviceName);
        }
    }
}
