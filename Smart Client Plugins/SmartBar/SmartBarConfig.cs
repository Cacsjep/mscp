using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Xml.Linq;
using CommunitySDK;

namespace SmartBar
{
    class ProgramEntry : INotifyPropertyChanged
    {
        private string _name;
        private string _path;

        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(); }
        }

        public string Path
        {
            get => _path;
            set { _path = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string prop = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
        }
    }

    static class SmartBarConfig
    {
        private static readonly PluginLog Log = SmartBarDefinition.Log;
        private static readonly object _fileLock = new object();
        private static readonly string _configPath = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Milestone", "SmartBar", "config.xml");

        public static int MaxHistory { get; set; } = 20;
        public static int MaxRecent { get; set; } = 10;
        public static Key InvokeKey { get; set; } = Key.Space;
        public static ModifierKeys InvokeModifiers { get; set; } = ModifierKeys.None;
        public static List<ProgramEntry> Programs { get; set; } = new List<ProgramEntry>();

        public static void Load()
        {
            lock (_fileLock)
            {
                if (!File.Exists(_configPath))
                {
                    MaxHistory = 20;
                    InvokeKey = Key.Space;
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

                    var invokeKeyEl = root?.Element("InvokeKey");
                    InvokeKey = invokeKeyEl != null && Enum.TryParse(invokeKeyEl.Value, out Key ik) ? ik : Key.Space;

                    var invokeModEl = root?.Element("InvokeModifiers");
                    InvokeModifiers = invokeModEl != null && Enum.TryParse(invokeModEl.Value, out ModifierKeys im) ? im : ModifierKeys.None;

                    var maxRecentEl = root?.Element("MaxRecent");
                    MaxRecent = maxRecentEl != null && int.TryParse(maxRecentEl.Value, out var mr) ? mr : 10;

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

                    Log.Info($"Configuration loaded: MaxHistory={MaxHistory}, Programs={Programs.Count}");
                }
                catch (Exception ex)
                {
                    Log.Error("Failed to load configuration, using defaults", ex);
                    MaxHistory = 20;
                    MaxRecent = 10;
                    InvokeKey = Key.Space;
                    InvokeModifiers = ModifierKeys.None;
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
                            new XElement("MaxRecent", MaxRecent),
                            new XElement("InvokeKey", InvokeKey.ToString()),
                            new XElement("InvokeModifiers", InvokeModifiers.ToString()),
                            progsEl));

                    doc.Save(_configPath);
                    Log.Info("Configuration saved");
                }
                catch (Exception ex)
                {
                    Log.Error("Failed to save configuration", ex);
                }
            }
        }
    }
}
