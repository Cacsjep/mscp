using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Xml.Linq;

namespace MonitorRTMPStreamer
{
    public class StreamerConfig
    {
        private static readonly string ConfigDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Milestone", "MonitorRTMPStreamer");

        private static readonly string ConfigPath = Path.Combine(ConfigDir, "config.xml");

        private static readonly object _fileLock = new object();

        public HashSet<string> EnabledMonitors { get; set; } = new HashSet<string>();

        public string RtmpUrl { get; set; } = "";

        public int Fps { get; set; } = 1;

        public static StreamerConfig Load()
        {
            lock (_fileLock)
            {
                var config = new StreamerConfig();
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

                    var fpsEl = doc.Root?.Element("Fps");
                    if (fpsEl != null && int.TryParse(fpsEl.Value, out var fps) && fps >= 1 && fps <= 10)
                        config.Fps = fps;
                }
                catch
                {
                    // Corrupt config - return defaults
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
                    new XElement("StreamerConfig",
                        new XElement("EnabledMonitors",
                            EnabledMonitors.Select(m => new XElement("Monitor", m))),
                        new XElement("RtmpUrl", RtmpUrl ?? ""),
                        new XElement("Fps", Fps)));
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
