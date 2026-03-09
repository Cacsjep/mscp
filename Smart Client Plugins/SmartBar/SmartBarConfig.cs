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
        private string _args;
        private bool _argsVisible;

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

        public string Args
        {
            get => _args;
            set { _args = value; OnPropertyChanged(); }
        }

        public bool ArgsVisible
        {
            get => _argsVisible;
            set { _argsVisible = value; OnPropertyChanged(); }
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
        public static bool ShowOutputs { get; set; } = true;
        public static bool ShowEvents { get; set; } = true;
        public static bool ShowCommands { get; set; } = true;
        public static bool ShowRecent { get; set; } = true;
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

                    var showOutputsEl = root?.Element("ShowOutputs");
                    ShowOutputs = showOutputsEl == null || !bool.TryParse(showOutputsEl.Value, out var so) || so;

                    var showEventsEl = root?.Element("ShowEvents");
                    ShowEvents = showEventsEl == null || !bool.TryParse(showEventsEl.Value, out var se) || se;

                    var showCommandsEl = root?.Element("ShowCommands");
                    ShowCommands = showCommandsEl == null || !bool.TryParse(showCommandsEl.Value, out var sc) || sc;

                    var showRecentEl = root?.Element("ShowRecent");
                    ShowRecent = showRecentEl == null || !bool.TryParse(showRecentEl.Value, out var sr) || sr;

                    Programs = new List<ProgramEntry>();
                    var progsEl = root?.Element("Programs");
                    if (progsEl != null)
                    {
                        foreach (var p in progsEl.Elements("Program"))
                        {
                            var name = p.Element("Name")?.Value;
                            var path = p.Element("Path")?.Value;
                            var args = p.Element("Args")?.Value;
                            if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(path))
                                Programs.Add(new ProgramEntry { Name = name, Path = path, Args = args ?? string.Empty });
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
                    ShowOutputs = true;
                    ShowEvents = true;
                    ShowCommands = true;
                    ShowRecent = true;
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
                        var progEl = new XElement("Program",
                            new XElement("Name", p.Name),
                            new XElement("Path", p.Path));
                        if (!string.IsNullOrEmpty(p.Args))
                            progEl.Add(new XElement("Args", p.Args));
                        progsEl.Add(progEl);
                    }

                    var doc = new XDocument(
                        new XElement("SmartBarConfig",
                            new XElement("MaxHistory", MaxHistory),
                            new XElement("MaxRecent", MaxRecent),
                            new XElement("InvokeKey", InvokeKey.ToString()),
                            new XElement("InvokeModifiers", InvokeModifiers.ToString()),
                            new XElement("ShowOutputs", ShowOutputs),
                            new XElement("ShowEvents", ShowEvents),
                            new XElement("ShowCommands", ShowCommands),
                            new XElement("ShowRecent", ShowRecent),
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
