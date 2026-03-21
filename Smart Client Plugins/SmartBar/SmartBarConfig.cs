using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Xml.Linq;
using CommunitySDK;

namespace SmartBar
{
    enum ItemCategory
    {
        Recent,
        Camera,
        View,
        Output,
        Event,
        Command,
        Program,
        Undo
    }

    class CategoryConfig : INotifyPropertyChanged
    {
        private ItemCategory _category;
        private bool _enabled;
        private int _column;
        private int _order;

        public ItemCategory Category
        {
            get => _category;
            set { _category = value; OnPropertyChanged(); }
        }

        public bool Enabled
        {
            get => _enabled;
            set { _enabled = value; OnPropertyChanged(); }
        }

        public int Column
        {
            get => _column;
            set { _column = value; OnPropertyChanged(); }
        }

        public int Order
        {
            get => _order;
            set { _order = value; OnPropertyChanged(); }
        }

        public string DisplayName => Category.ToString();

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string prop = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
        }
    }

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
        public static bool ColumnLayout { get; set; }
        public static int PaletteWidth { get; set; } = 50;
        public static int PaletteHeight { get; set; } = 60;
        public static List<CategoryConfig> Categories { get; set; } = GetDefaultCategories();
        public static List<ProgramEntry> Programs { get; set; } = new List<ProgramEntry>();

        public static bool IsEnabled(ItemCategory cat)
        {
            var cfg = Categories.FirstOrDefault(c => c.Category == cat);
            return cfg?.Enabled ?? true;
        }

        public static int GetOrder(ItemCategory cat)
        {
            var cfg = Categories.FirstOrDefault(c => c.Category == cat);
            return cfg?.Order ?? (int)cat + 1;
        }

        public static int GetColumn(ItemCategory cat)
        {
            var cfg = Categories.FirstOrDefault(c => c.Category == cat);
            return cfg?.Column ?? 1;
        }

        public static List<CategoryConfig> GetDefaultCategories()
        {
            return new List<CategoryConfig>
            {
                new CategoryConfig { Category = ItemCategory.Recent,  Enabled = true, Column = 1, Order = 1 },
                new CategoryConfig { Category = ItemCategory.Camera,  Enabled = true, Column = 1, Order = 2 },
                new CategoryConfig { Category = ItemCategory.View,    Enabled = true, Column = 1, Order = 3 },
                new CategoryConfig { Category = ItemCategory.Command, Enabled = true, Column = 2, Order = 1 },
                new CategoryConfig { Category = ItemCategory.Output,  Enabled = true, Column = 2, Order = 2 },
                new CategoryConfig { Category = ItemCategory.Event,   Enabled = true, Column = 2, Order = 3 },
                new CategoryConfig { Category = ItemCategory.Program, Enabled = true, Column = 3, Order = 1 },
                new CategoryConfig { Category = ItemCategory.Undo,    Enabled = true, Column = 3, Order = 2 },
            };
        }

        public static void Load()
        {
            lock (_fileLock)
            {
                if (!File.Exists(_configPath))
                {
                    MaxHistory = 20;
                    InvokeKey = Key.Space;
                    Categories = GetDefaultCategories();
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

                    var colLayoutEl = root?.Element("ColumnLayout");
                    ColumnLayout = colLayoutEl != null && bool.TryParse(colLayoutEl.Value, out var cl) && cl;

                    var palWidthEl = root?.Element("SmartBarWidth") ?? root?.Element("PaletteWidth");
                    PaletteWidth = palWidthEl != null && int.TryParse(palWidthEl.Value, out var pw) ? pw : 50;

                    var palHeightEl = root?.Element("SmartBarHeight") ?? root?.Element("PaletteHeight");
                    PaletteHeight = palHeightEl != null && int.TryParse(palHeightEl.Value, out var ph) ? ph : 60;

                    // Detect old pixel-based dimensions (>100 means px, not %)
                    if (PaletteWidth > 100 || PaletteHeight > 100)
                    {
                        Log.Info($"Detected old pixel-based dimensions ({PaletteWidth}x{PaletteHeight}), resetting to percentage defaults");
                        PaletteWidth = 50;
                        PaletteHeight = 60;
                    }

                    // Parse categories
                    var catsEl = root?.Element("Categories");
                    if (catsEl != null)
                    {
                        Categories = new List<CategoryConfig>();
                        foreach (var catEl in catsEl.Elements("Category"))
                        {
                            var nameVal = catEl.Attribute("Name")?.Value;
                            if (nameVal == null || !Enum.TryParse(nameVal, out ItemCategory ic)) continue;

                            var enabledVal = catEl.Attribute("Enabled")?.Value;
                            var columnVal = catEl.Attribute("Column")?.Value;
                            var orderVal = catEl.Attribute("Order")?.Value;

                            Categories.Add(new CategoryConfig
                            {
                                Category = ic,
                                Enabled = enabledVal == null || !bool.TryParse(enabledVal, out var en) || en,
                                Column = columnVal != null && int.TryParse(columnVal, out var col) ? col : 0,
                                Order = orderVal != null && int.TryParse(orderVal, out var ord) ? ord : (int)ic,
                            });
                        }
                    }
                    else
                    {
                        Categories = GetDefaultCategories();
                    }

                    // Detect old 0-based configs and reset to defaults
                    if (Categories.Any(c => c.Column == 0))
                    {
                        Log.Info("Detected old 0-based category config, resetting to 1-based defaults");
                        Categories = GetDefaultCategories();
                    }

                    // Ensure all categories present
                    foreach (ItemCategory ic in Enum.GetValues(typeof(ItemCategory)))
                    {
                        if (!Categories.Any(c => c.Category == ic))
                            Categories.Add(new CategoryConfig { Category = ic, Enabled = true, Column = 1, Order = (int)ic + 1 });
                    }

                    // Parse programs
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

                    Log.Info($"Configuration loaded: MaxHistory={MaxHistory}, ColumnLayout={ColumnLayout}, Categories={Categories.Count}, Programs={Programs.Count}");
                }
                catch (Exception ex)
                {
                    Log.Error("Failed to load configuration, using defaults", ex);
                    MaxHistory = 20;
                    MaxRecent = 10;
                    InvokeKey = Key.Space;
                    InvokeModifiers = ModifierKeys.None;
                    ColumnLayout = false;
                    PaletteWidth = 50;
                    PaletteHeight = 60;
                    Categories = GetDefaultCategories();
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

                    var catsEl = new XElement("Categories");
                    var sortedCats = Categories.OrderBy(c => c.Column).ThenBy(c => c.Order).ToList();
                    int currentCol = -1;
                    foreach (var cat in sortedCats)
                    {
                        if (cat.Column != currentCol)
                        {
                            currentCol = cat.Column;
                            catsEl.Add(new XComment($" Column {currentCol} "));
                        }
                        catsEl.Add(new XElement("Category",
                            new XAttribute("Name", cat.Category.ToString()),
                            new XAttribute("Enabled", cat.Enabled),
                            new XAttribute("Column", cat.Column),
                            new XAttribute("Order", cat.Order)));
                    }

                    var root = new XElement("SmartBarConfig",
                        new XElement("MaxHistory", MaxHistory),
                        new XElement("MaxRecent", MaxRecent),
                        new XElement("InvokeKey", InvokeKey.ToString()),
                        new XElement("InvokeModifiers", InvokeModifiers.ToString()),
                        new XElement("ColumnLayout", ColumnLayout),
                        new XElement("SmartBarWidth", PaletteWidth),
                        new XElement("SmartBarHeight", PaletteHeight),
                        new XComment(" Column = display column (1-based), Order = sort position within column (1-based) "),
                        catsEl,
                        progsEl);
                    var doc = new XDocument(root);

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
