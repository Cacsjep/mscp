using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Linq;

namespace SmartBar
{
    class ProgramEntry
    {
        public string Name { get; set; }
        public string Path { get; set; }
    }

    static class SmartBarConfig
    {
        private static readonly object _fileLock = new object();
        private static readonly string _configPath = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Milestone", "SmartBar", "config.xml");

        public static int MaxHistory { get; set; } = 20;
        public static List<ProgramEntry> Programs { get; set; } = new List<ProgramEntry>();

        public static void Load()
        {
            lock (_fileLock)
            {
                if (!File.Exists(_configPath))
                {
                    // Set defaults
                    MaxHistory = 20;
                    Programs = new List<ProgramEntry>
                    {
                        new ProgramEntry { Name = "Notepad", Path = "notepad.exe" }
                    };
                    Save();
                    return;
                }

                try
                {
                    var doc = XDocument.Load(_configPath);
                    var root = doc.Root;

                    var maxHistEl = root?.Element("MaxHistory");
                    MaxHistory = maxHistEl != null && int.TryParse(maxHistEl.Value, out var mh) ? mh : 20;

                    Programs = new List<ProgramEntry>();
                    var progsEl = root?.Element("Programs");
                    if (progsEl != null)
                    {
                        foreach (var p in progsEl.Elements("Program"))
                        {
                            var name = p.Element("Name")?.Value;
                            var path = p.Element("Path")?.Value;
                            if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(path))
                                Programs.Add(new ProgramEntry { Name = name, Path = path });
                        }
                    }

                    if (Programs.Count == 0)
                        Programs.Add(new ProgramEntry { Name = "Notepad", Path = "notepad.exe" });
                }
                catch
                {
                    MaxHistory = 20;
                    Programs = new List<ProgramEntry>
                    {
                        new ProgramEntry { Name = "Notepad", Path = "notepad.exe" }
                    };
                }
            }
        }

        public static void Save()
        {
            lock (_fileLock)
            {
                try
                {
                    var dir = System.IO.Path.GetDirectoryName(_configPath);
                    if (!Directory.Exists(dir))
                        Directory.CreateDirectory(dir);

                    var progsEl = new XElement("Programs");
                    foreach (var p in Programs)
                    {
                        progsEl.Add(new XElement("Program",
                            new XElement("Name", p.Name),
                            new XElement("Path", p.Path)));
                    }

                    var doc = new XDocument(
                        new XElement("SmartBarConfig",
                            new XElement("MaxHistory", MaxHistory),
                            progsEl));

                    doc.Save(_configPath);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[SmartBar] Config save failed: {ex.Message}");
                }
            }
        }
    }
}
