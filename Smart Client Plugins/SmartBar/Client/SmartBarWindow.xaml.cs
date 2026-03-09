using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CommunitySDK;
using VideoOS.Platform;
using VideoOS.Platform.Client;
using VideoOS.Platform.ConfigurationItems;
using VideoOS.Platform.Messaging;

namespace SmartBar.Client
{
    public partial class SmartBarWindow : Window
    {
        private static readonly PluginLog Log = SmartBarDefinition.Log;
        private static readonly SolidColorBrush SelectedBg = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x1E, 0x1E, 0x1E));
        private static readonly SolidColorBrush TransparentBg = System.Windows.Media.Brushes.Transparent;
        private static readonly SolidColorBrush TextPrimary = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xE8, 0xE8, 0xE8));
        private static readonly SolidColorBrush TextSelected = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x58, 0xA6, 0xFF));
        private static readonly SolidColorBrush TextGroup = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x50, 0x50, 0x50));
        private static readonly SolidColorBrush TextCategory = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0xFF, 0xFF));

        private List<CommandItem> _allItems;
        private List<CommandItem> _filteredItems;
        private readonly HashSet<CommandItem> _selectedCameras = new HashSet<CommandItem>();
        private int _selectedIndex;

        private bool _closing;
        private bool _suppressMouseSelect;

        private List<Item> _windows;
        private int _targetWindowIndex;

        public SmartBarWindow()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Deactivated += OnDeactivated;
        }

        private void OnDeactivated(object sender, EventArgs e)
        {
            SafeClose();
        }

        private void SafeClose()
        {
            if (_closing) return;
            _closing = true;
            try { Close(); } catch { }
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            PositionOnActiveMonitor();
            searchBox.Focus();
            LoadWindows();
            LoadItems();
            ApplyFilter();
        }

        private void PositionOnActiveMonitor()
        {
            // Cover the same monitor as the Smart Client main window
            var source = Application.Current.MainWindow;
            if (source == null) return;

            var screen = System.Windows.Forms.Screen.FromHandle(
                new System.Windows.Interop.WindowInteropHelper(source).Handle);

            var area = screen.WorkingArea;
            Left = area.Left;
            Top = area.Top;
            Width = area.Width;
            Height = area.Height;
        }

        private void LoadItems()
        {
            _allItems = new List<CommandItem>();

            try
            {
                var serverId = EnvironmentManager.Instance.MasterSite.ServerId;
                var mgmt = new ManagementServer(EnvironmentManager.Instance.MasterSite);

                foreach (var group in mgmt.CameraGroupFolder.CameraGroups)
                    CollectCameras(group, serverId);

                var viewGroups = ClientControl.Instance.GetViewGroupItems();
                if (viewGroups != null)
                {
                    foreach (var vg in viewGroups)
                        CollectViews(vg);
                }

                LoadRecentItems();
                LoadCommands();
                LoadPrograms();
                LoadOutputs();
                LoadEvents();
                LoadUndoHistory();
            }
            catch (Exception ex) { Log.Error("LoadItems failed", ex); }
        }

        private void CollectCameras(CameraGroup group, ServerId serverId, string parentPath = null)
        {
            var path = parentPath == null ? group.Name : parentPath + " › " + group.Name;

            foreach (var cam in group.CameraFolder.Cameras)
            {
                if (!cam.Enabled) continue;
                var cameraId = new Guid(cam.Id);
                var item = Configuration.Instance.GetItem(serverId, cameraId, Kind.Camera);
                if (item == null) continue;

                _allItems.Add(new CommandItem
                {
                    Name = cam.Name,
                    Group = path,
                    Category = ItemCategory.Camera,
                    PlatformItem = item
                });
            }

            foreach (var sub in group.CameraGroupFolder.CameraGroups)
                CollectCameras(sub, serverId, path);
        }

        private void CollectViews(Item viewGroup, string parentPath = null)
        {
            var path = parentPath == null ? viewGroup.Name : parentPath + " › " + viewGroup.Name;

            foreach (var child in viewGroup.GetChildren())
            {
                if (child.Name == SmartBarGroupName) continue;

                if (child.FQID.FolderType == FolderType.No)
                {
                    _allItems.Add(new CommandItem
                    {
                        Name = child.Name,
                        Group = path,
                        Category = ItemCategory.View,
                        PlatformItem = child
                    });
                }
                else
                {
                    CollectViews(child, path);
                }
            }
        }

        private void LoadCommands()
        {
            if (!SmartBarConfig.ShowCommands) return;

            void AddCmd(string group, string name, Action action)
            {
                _allItems.Add(new CommandItem
                {
                    Name = "Command: " + name,
                    Group = group,
                    Category = ItemCategory.Command,
                    Execute = action
                });
            }

            // Application Control
            AddCmd("Application", "Toggle Fullscreen", () =>
                SendAppControl(ApplicationControlCommandData.ToggleFullScreenMode));
            AddCmd("Application", "Enter Fullscreen", () =>
                SendAppControl(ApplicationControlCommandData.EnterFullScreenMode));
            AddCmd("Application", "Exit Fullscreen", () =>
                SendAppControl(ApplicationControlCommandData.ExitFullScreenMode));
            AddCmd("Application", "Show Side Panel", () =>
                SendAppControl(ApplicationControlCommandData.ShowSidePanel));
            AddCmd("Application", "Hide Side Panel", () =>
                SendAppControl(ApplicationControlCommandData.HideSidePanel));
            AddCmd("Application", "Maximize Window", () =>
                SendAppControl(ApplicationControlCommandData.Maximize));
            AddCmd("Application", "Minimize Window", () =>
                SendAppControl(ApplicationControlCommandData.Minimize));
            AddCmd("Application", "Restore Window", () =>
                SendAppControl(ApplicationControlCommandData.Restore));
            AddCmd("Application", "Reload Configuration", () =>
                EnvironmentManager.Instance.SendMessage(
                    new Message(MessageId.SmartClient.ReloadConfigurationCommand)));

            // Mode Switching
            AddCmd("Mode", "Switch to Live", () =>
                EnvironmentManager.Instance.SendMessage(
                    new Message(MessageId.SmartClient.ChangeModeCommand, "ClientLive")));
            AddCmd("Mode", "Switch to Playback", () =>
                EnvironmentManager.Instance.SendMessage(
                    new Message(MessageId.SmartClient.ChangeModeCommand, "ClientPlayback")));
            AddCmd("Mode", "Switch to Setup", () =>
                EnvironmentManager.Instance.SendMessage(
                    new Message(MessageId.SmartClient.ChangeModeCommand, "ClientSetup")));

            // Window
            AddCmd("Window", "Close Target Window", () =>
            {
                var dest = GetTargetWindowFQID();
                if (dest != null)
                    EnvironmentManager.Instance.SendMessage(
                        new Message(MessageId.SmartClient.MultiWindowCommand,
                            new MultiWindowCommandData
                            {
                                MultiWindowCommand = MultiWindowCommand.CloseSelectedWindow,
                                Window = dest
                            }), dest);
            });
            AddCmd("Window", "Close All Floating Windows", () =>
                EnvironmentManager.Instance.SendMessage(
                    new Message(MessageId.SmartClient.MultiWindowCommand,
                        new MultiWindowCommandData
                        {
                            MultiWindowCommand = MultiWindowCommand.CloseAllWindows
                        })));

            // Navigation
            AddCmd("Navigation", "Undo / Go Back", () => SmartBarHistory.GoBack());
        }

        private void LoadPrograms()
        {
            foreach (var prog in SmartBarConfig.Programs)
            {
                var path = prog.Path;
                var args = prog.Args;
                _allItems.Add(new CommandItem
                {
                    Name = "Program: " + prog.Name,
                    Group = "Programs",
                    Category = ItemCategory.Program,
                    Execute = () =>
                    {
                        try
                        {
                            if (string.IsNullOrEmpty(args))
                                System.Diagnostics.Process.Start(path);
                            else
                                System.Diagnostics.Process.Start(path, args);
                        }
                        catch (Exception ex) { Log.Error($"Failed to start program: {path}", ex); }
                    }
                });
            }
        }

        private void LoadOutputs()
        {
            if (!SmartBarConfig.ShowOutputs) return;

            try
            {
                var outputs = Configuration.Instance.GetItemsByKind(Kind.Output);
                foreach (var output in outputs)
                    CollectOutputItems(output);
            }
            catch (Exception ex) { Log.Error("LoadOutputs failed", ex); }
        }

        private void CollectOutputItems(Item item)
        {
            if (item.FQID.FolderType == FolderType.No)
            {
                var fqid = item.FQID;
                _allItems.Add(new CommandItem
                {
                    Name = "Output: " + item.Name + " Activate",
                    Group = "Outputs",
                    Category = ItemCategory.Output,
                    Execute = () =>
                    {
                        try
                        {
                            EnvironmentManager.Instance.SendMessage(
                                new Message(MessageId.Control.OutputActivate) { RelatedFQID = fqid }, fqid);
                            Log.Info($"Output activated: {item.Name}");
                        }
                        catch (Exception ex) { Log.Error($"OutputActivate failed: {item.Name}", ex); }
                    }
                });
                _allItems.Add(new CommandItem
                {
                    Name = "Output: " + item.Name + " Deactivate",
                    Group = "Outputs",
                    Category = ItemCategory.Output,
                    Execute = () =>
                    {
                        try
                        {
                            EnvironmentManager.Instance.SendMessage(
                                new Message(MessageId.Control.OutputDeactivate) { RelatedFQID = fqid }, fqid);
                            Log.Info($"Output deactivated: {item.Name}");
                        }
                        catch (Exception ex) { Log.Error($"OutputDeactivate failed: {item.Name}", ex); }
                    }
                });
            }
            else
            {
                foreach (var child in item.GetChildren())
                    CollectOutputItems(child);
            }
        }

        private void LoadEvents()
        {
            if (!SmartBarConfig.ShowEvents) return;

            try
            {
                var events = Configuration.Instance.GetItemsByKind(Kind.TriggerEvent);
                foreach (var ev in events)
                    CollectEventItems(ev);
            }
            catch (Exception ex) { Log.Error("LoadEvents failed", ex); }
        }

        private void CollectEventItems(Item item)
        {
            if (item.FQID.FolderType == FolderType.No)
            {
                var fqid = item.FQID;
                _allItems.Add(new CommandItem
                {
                    Name = "Event: " + item.Name,
                    Group = "Events",
                    Category = ItemCategory.Event,
                    Execute = () =>
                    {
                        try
                        {
                            EnvironmentManager.Instance.SendMessage(
                                new Message(MessageId.Control.TriggerCommand) { RelatedFQID = fqid }, fqid);
                            Log.Info($"Event triggered: {item.Name}");
                        }
                        catch (Exception ex) { Log.Error($"TriggerEvent failed: {item.Name}", ex); }
                    }
                });
            }
            else
            {
                foreach (var child in item.GetChildren())
                    CollectEventItems(child);
            }
        }

        private void LoadRecentItems()
        {
            if (!SmartBarConfig.ShowRecent) return;

            var recents = SmartBarHistory.GetRecentItems();
            if (recents.Count == 0) return;

            var serverId = EnvironmentManager.Instance.MasterSite.ServerId;
            foreach (var r in recents)
            {
                if (r.Type == RecentType.Camera)
                {
                    var item = Configuration.Instance.GetItem(serverId, r.ObjectId, Kind.Camera);
                    if (item == null) continue;
                    _allItems.Add(new CommandItem
                    {
                        Name = r.Name,
                        Group = "Recent",
                        Category = ItemCategory.Recent,
                        PlatformItem = item
                    });
                }
                else
                {
                    // Find the view item by ObjectId
                    var viewItem = FindViewByObjectId(r.ObjectId);
                    if (viewItem == null) continue;
                    _allItems.Add(new CommandItem
                    {
                        Name = r.Name,
                        Group = "Recent",
                        Category = ItemCategory.Recent,
                        PlatformItem = viewItem
                    });
                }
            }
        }

        private Item FindViewByObjectId(Guid objectId)
        {
            var viewGroups = ClientControl.Instance.GetViewGroupItems();
            if (viewGroups == null) return null;
            foreach (var vg in viewGroups)
            {
                var found = FindViewRecursive(vg, objectId);
                if (found != null) return found;
            }
            return null;
        }

        private Item FindViewRecursive(Item parent, Guid objectId)
        {
            foreach (var child in parent.GetChildren())
            {
                if (child.FQID.ObjectId == objectId) return child;
                if (child.FQID.FolderType != FolderType.No)
                {
                    var found = FindViewRecursive(child, objectId);
                    if (found != null) return found;
                }
            }
            return null;
        }

        private void LoadUndoHistory()
        {
            var descriptions = SmartBarHistory.GetHistoryDescriptions();
            for (int i = 0; i < descriptions.Count; i++)
            {
                int undoCount = i + 1;
                _allItems.Add(new CommandItem
                {
                    Name = $"Undo {undoCount}: {descriptions[i]}",
                    Group = "Undo History",
                    Category = ItemCategory.Undo,
                    Execute = () => SmartBarHistory.GoBackN(undoCount)
                });
            }
        }

        private void SendAppControl(string command)
        {
            EnvironmentManager.Instance.SendMessage(
                new Message(MessageId.SmartClient.ApplicationControlCommand, command));
        }

        private void ApplyFilter()
        {
            var query = searchBox.Text?.Trim() ?? string.Empty;

            var matched = string.IsNullOrEmpty(query)
                ? _allItems
                : _allItems.Where(i => i.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0
                                    || i.Group.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0);

            _filteredItems = matched
                .OrderBy(i => i.Category)
                .ThenBy(i => i.Group, StringComparer.OrdinalIgnoreCase)
                .ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            _selectedIndex = _filteredItems.Count > 0 ? 0 : -1;
            RebuildList();
        }

        private const int MaxVisibleItems = 50;

        private void RebuildList()
        {
            resultPanel.Children.Clear();

            ItemCategory? lastCategory = null;
            string lastGroup = null;
            bool isFirst = true;
            int rendered = 0;

            for (int i = 0; i < _filteredItems.Count; i++)
            {
                if (rendered >= MaxVisibleItems) break;

                var item = _filteredItems[i];

                if (lastCategory != item.Category)
                {
                    lastCategory = item.Category;
                    lastGroup = null;
                    var categoryLabel = item.Category == ItemCategory.Recent ? "Recent"
                        : item.Category == ItemCategory.Undo ? "Undo History"
                        : item.Category == ItemCategory.Camera ? "Cameras"
                        : item.Category == ItemCategory.View ? "Views"
                        : item.Category == ItemCategory.Output ? "Outputs"
                        : item.Category == ItemCategory.Event ? "Events"
                        : item.Category == ItemCategory.Program ? "Programs" : "Commands";
                    resultPanel.Children.Add(new TextBlock
                    {
                        Text = categoryLabel,
                        Foreground = TextCategory,
                        FontSize = 10.5,
                        FontWeight = FontWeights.SemiBold,
                        Margin = new Thickness(10, isFirst ? 6 : 14, 0, 2)
                    });
                    resultPanel.Children.Add(new Border
                    {
                        BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x28, 0x28, 0x28)),
                        BorderThickness = new Thickness(0, 1, 0, 0),
                        Margin = new Thickness(10, 0, 10, 4)
                    });
                    isFirst = false;
                }

                if (item.Group != lastGroup)
                {
                    lastGroup = item.Group;
                    var groupBlock = new TextBlock
                    {
                        FontSize = 11,
                        Margin = new Thickness(10, 8, 0, 3)
                    };
                    var parts = item.Group.Split(new[] { " › " }, StringSplitOptions.None);
                    for (int p = 0; p < parts.Length; p++)
                    {
                        if (p > 0)
                            groupBlock.Inlines.Add(new System.Windows.Documents.Run(" › ") { Foreground = TextGroup });
                        groupBlock.Inlines.Add(new System.Windows.Documents.Run(parts[p])
                        {
                            Foreground = p == parts.Length - 1 ? TextSelected : TextGroup
                        });
                    }
                    resultPanel.Children.Add(groupBlock);
                }

                resultPanel.Children.Add(CreateRow(item, i));
                rendered++;
            }

            if (_filteredItems.Count > MaxVisibleItems)
            {
                int remaining = _filteredItems.Count - MaxVisibleItems;
                resultPanel.Children.Add(new TextBlock
                {
                    Text = $"{remaining} more results, keep typing to filter...",
                    Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0xB3, 0x00)),
                    FontSize = 11,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 10, 0, 6)
                });
            }

            if (_filteredItems.Count == 0)
            {
                resultPanel.Children.Add(new TextBlock
                {
                    Text = "No results found",
                    Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0xB3, 0x00)),
                    FontSize = 12,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 24, 0, 24)
                });
            }
        }

        private Border CreateRow(CommandItem item, int index)
        {
            bool isMultiSelected = _selectedCameras.Contains(item);

            var nameBlock = new TextBlock
            {
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };

            if (isMultiSelected)
            {
                nameBlock.Inlines.Add(new System.Windows.Documents.Run("\u2713  " + item.Name) { Foreground = TextSelected });
            }
            else if (item.Category == ItemCategory.Undo && item.Name.StartsWith("Undo "))
            {
                var colonIdx = item.Name.IndexOf(": ", 5);
                if (colonIdx > 0)
                {
                    nameBlock.Inlines.Add(new System.Windows.Documents.Run(item.Name.Substring(0, colonIdx + 2)) { Foreground = TextGroup });
                    nameBlock.Inlines.Add(new System.Windows.Documents.Run(item.Name.Substring(colonIdx + 2)) { Foreground = TextPrimary });
                }
                else
                {
                    nameBlock.Inlines.Add(new System.Windows.Documents.Run(item.Name) { Foreground = TextPrimary });
                }
            }
            else if (item.Category == ItemCategory.Command && item.Name.StartsWith("Command: "))
            {
                nameBlock.Inlines.Add(new System.Windows.Documents.Run("Command: ") { Foreground = TextGroup });
                nameBlock.Inlines.Add(new System.Windows.Documents.Run(item.Name.Substring(9)) { Foreground = TextPrimary });
            }
            else if (item.Category == ItemCategory.Program && item.Name.StartsWith("Program: "))
            {
                nameBlock.Inlines.Add(new System.Windows.Documents.Run("Program: ") { Foreground = TextGroup });
                nameBlock.Inlines.Add(new System.Windows.Documents.Run(item.Name.Substring(9)) { Foreground = TextPrimary });
            }
            else if ((item.Category == ItemCategory.Output || item.Category == ItemCategory.Event)
                && item.Name.IndexOf(": ") is int ci && ci > 0)
            {
                nameBlock.Inlines.Add(new System.Windows.Documents.Run(item.Name.Substring(0, ci + 2)) { Foreground = TextGroup });
                nameBlock.Inlines.Add(new System.Windows.Documents.Run(item.Name.Substring(ci + 2)) { Foreground = TextPrimary });
            }
            else
            {
                nameBlock.Inlines.Add(new System.Windows.Documents.Run(item.Name) { Foreground = TextPrimary });
            }

            var row = new Border
            {
                Background = index == _selectedIndex ? SelectedBg : TransparentBg,
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(22, 5, 10, 5),
                Cursor = Cursors.Hand,
                Child = nameBlock,
                Tag = index
            };

            row.MouseEnter += (s, _) =>
            {
                if (_suppressMouseSelect) return;
                _selectedIndex = (int)((Border)s).Tag;
                UpdateSelection();
            };
            row.MouseMove += (s, _) => _suppressMouseSelect = false;
            row.MouseLeftButtonUp += (s, _) => ExecuteSelected();

            return row;
        }

        private void UpdateSelection()
        {
            foreach (var child in resultPanel.Children)
            {
                if (child is Border border && border.Tag is int idx)
                    border.Background = idx == _selectedIndex ? SelectedBg : TransparentBg;
            }
        }

        private void UpdateSelectionBar()
        {
            if (_selectedCameras.Count > 0)
            {
                selectionBar.Visibility = Visibility.Visible;
                selectionText.Text = $"{_selectedCameras.Count} camera(s) selected";
            }
            else
            {
                selectionBar.Visibility = Visibility.Collapsed;
            }
        }

        private void ToggleMultiSelect()
        {
            if (_selectedIndex < 0 || _selectedIndex >= _filteredItems.Count)
                return;

            var item = _filteredItems[_selectedIndex];
            if (item.Category != ItemCategory.Camera)
                return;

            if (!_selectedCameras.Remove(item))
                _selectedCameras.Add(item);

            _suppressMouseSelect = true;
            UpdateSelectionBar();
            RebuildList();
            ScrollToSelected();
            searchBox.Focus();
        }

        private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
        {
            searchPlaceholder.Visibility = string.IsNullOrEmpty(searchBox.Text)
                ? Visibility.Visible
                : Visibility.Collapsed;
            ApplyFilter();
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Escape:
                    e.Handled = true;
                    if (_selectedCameras.Count > 0)
                    {
                        _selectedCameras.Clear();
                        UpdateSelectionBar();
                        RebuildList();
                    }
                    else
                    {
                        SafeClose();
                    }
                    break;

                case Key.Down:
                    e.Handled = true;
                    if (_filteredItems.Count > 0)
                    {
                        _suppressMouseSelect = true;
                        int maxIdx = Math.Min(_filteredItems.Count, MaxVisibleItems) - 1;
                        _selectedIndex = _selectedIndex >= maxIdx ? 0 : _selectedIndex + 1;
                        UpdateSelection();
                        ScrollToSelected();
                    }
                    break;

                case Key.Up:
                    e.Handled = true;
                    if (_filteredItems.Count > 0)
                    {
                        _suppressMouseSelect = true;
                        int maxIdx = Math.Min(_filteredItems.Count, MaxVisibleItems) - 1;
                        _selectedIndex = _selectedIndex <= 0 ? maxIdx : _selectedIndex - 1;
                        UpdateSelection();
                        ScrollToSelected();
                    }
                    break;

                case Key.Tab:
                    e.Handled = true;
                    ToggleMultiSelect();
                    break;

                case Key.Enter:
                    e.Handled = true;
                    ExecuteSelected();
                    break;

                case Key.D1: case Key.D2: case Key.D3: case Key.D4:
                case Key.D5: case Key.D6: case Key.D7: case Key.D8: case Key.D9:
                    if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
                    {
                        e.Handled = true;
                        int winIdx = e.Key - Key.D1;
                        if (_windows != null && winIdx < _windows.Count)
                        {
                            _targetWindowIndex = winIdx;
                            RebuildWindowChips();
                        }
                    }
                    break;
            }
        }

        private void OnBackdropMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is System.Windows.Controls.Grid)
                SafeClose();
        }

        private void ScrollToSelected()
        {
            foreach (var child in resultPanel.Children)
            {
                if (child is Border border && border.Tag is int idx && idx == _selectedIndex)
                {
                    border.BringIntoView();
                    break;
                }
            }
        }

        private void LoadWindows()
        {
            _windows = Configuration.Instance.GetItemsByKind(Kind.Window);
            _targetWindowIndex = 0;
            RebuildWindowChips();
        }

        private void RebuildWindowChips()
        {
            windowChipsPanel.Children.Clear();
            if (_windows == null || _windows.Count <= 1) return;

            for (int i = 0; i < _windows.Count; i++)
            {
                bool isActive = i == _targetWindowIndex;
                var activeFg = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x00, 0x00, 0x00));
                var inactiveFg = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x90, 0x90, 0x90));

                var chip = new Border
                {
                    Background = isActive
                        ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x58, 0xA6, 0xFF))
                        : new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x1E, 0x1E, 0x1E)),
                    CornerRadius = new CornerRadius(3),
                    Padding = new Thickness(6, 3, 6, 3),
                    Margin = new Thickness(0, 0, 4, 0),
                    Cursor = Cursors.Hand,
                    Tag = i
                };

                var content = new StackPanel { Orientation = Orientation.Horizontal };
                content.Children.Add(new FontAwesome5.ImageAwesome
                {
                    Icon = FontAwesome5.EFontAwesomeIcon.Regular_WindowRestore,
                    Foreground = isActive ? activeFg : inactiveFg,
                    Width = 10,
                    Height = 10,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 4, 0)
                });
                content.Children.Add(new TextBlock
                {
                    Text = (i + 1).ToString(),
                    Foreground = isActive ? activeFg : inactiveFg,
                    FontSize = 12,
                    FontWeight = isActive ? FontWeights.SemiBold : FontWeights.Normal,
                    VerticalAlignment = VerticalAlignment.Center
                });

                chip.Child = content;
                chip.MouseLeftButtonUp += (s, _) =>
                {
                    _targetWindowIndex = (int)((Border)s).Tag;
                    RebuildWindowChips();
                };

                windowChipsPanel.Children.Add(chip);
            }
        }

        private FQID GetTargetWindowFQID()
        {
            if (_windows != null && _targetWindowIndex < _windows.Count)
                return _windows[_targetWindowIndex].FQID;
            return null;
        }

        private void ExecuteSelected()
        {
            if (_selectedIndex < 0 || _selectedIndex >= _filteredItems.Count)
                return;

            var item = _filteredItems[_selectedIndex];

            if (item.Category == ItemCategory.Camera)
            {
                var cameras = new List<CommandItem>(_selectedCameras);
                if (cameras.Count == 0)
                    cameras.Add(item);

                ShowCamerasInView(cameras);
            }
            else if (item.Category == ItemCategory.View)
            {
                NavigateToView(item);
            }
            else if (item.Category == ItemCategory.Recent)
            {
                // Recent items can be cameras or views — check the Kind
                if (item.PlatformItem?.FQID?.Kind == Kind.Camera)
                    ShowCamerasInView(new List<CommandItem> { item });
                else
                    NavigateToView(item);
            }
            else if (item.Category == ItemCategory.Command || item.Category == ItemCategory.Program
                || item.Category == ItemCategory.Undo || item.Category == ItemCategory.Output
                || item.Category == ItemCategory.Event)
            {
                item.Execute?.Invoke();
            }

            SafeClose();
        }

        private void SetCameraInSlot(int index, FQID cameraFQID)
        {
            var dest = GetTargetWindowFQID();
            EnvironmentManager.Instance.SendMessage(
                new Message(MessageId.SmartClient.SetCameraInViewCommand,
                    new SetCameraInViewCommandData
                    {
                        Index = index,
                        CameraFQID = cameraFQID
                    }), dest);
        }

        // (rows, cols) - sorted by total slots ascending for best-fit selection
        private static readonly (int rows, int cols)[] GridLayouts =
        {
            (1, 1), (1, 2), (1, 3), (2, 2), (2, 3),
            (2, 4), (3, 3), (3, 4), (4, 4), (4, 5)
        };
        private static readonly string SmartBarGroupName = "SmartBar";

        private static Item FindPrivateViewGroup()
        {
            var groups = ClientControl.Instance.GetViewGroupItems();
            if (groups == null || groups.Count == 0) return null;

            // GetViewGroupItems returns top-level groups: typically Private, Shared
            // Use the first one (Private views)
            return groups[0];
        }

        public static void EnsureSmartBarViews()
        {
            var topGroup = FindPrivateViewGroup() as ConfigItem;
            if (topGroup == null) return;

            var sbGroup = topGroup.GetChildren()
                .FirstOrDefault(c => c.Name == SmartBarGroupName) as ConfigItem;

            if (sbGroup == null)
                sbGroup = topGroup.AddChild(SmartBarGroupName, Kind.View, FolderType.UserDefined);

            if (sbGroup == null) return;

            var existing = sbGroup.GetChildren().Select(c => c.Name).ToHashSet();
            bool changed = false;

            foreach (var (rows, cols) in GridLayouts)
            {
                int total = rows * cols;
                string name = $"{rows}x{cols}";
                if (existing.Contains(name)) continue;

                var rects = new Rectangle[total];
                int cellW = 1000 / cols;
                int cellH = 1000 / rows;
                for (int i = 0; i < total; i++)
                {
                    int col = i % cols;
                    int row = i / cols;
                    rects[i] = new Rectangle(col * cellW, row * cellH, cellW, cellH);
                }

                var view = sbGroup.AddChild(name, Kind.View, FolderType.No) as ViewAndLayoutItem;
                if (view == null) continue;

                view.Layout = rects;
                for (int i = 0; i < total; i++)
                {
                    view.InsertBuiltinViewItem(i, ViewAndLayoutItem.CameraBuiltinId,
                        new Dictionary<string, string> { { "CameraId", Guid.Empty.ToString() } });
                }
                view.Save();
                changed = true;
            }

            if (changed)
                topGroup.PropertiesModified();
        }

        private void ShowCamerasInView(List<CommandItem> cameras)
        {
            var topGroup = FindPrivateViewGroup();
            if (topGroup == null) return;

            var sbGroup = topGroup.GetChildren()
                .FirstOrDefault(c => c.Name == SmartBarGroupName);
            if (sbGroup == null) return;

            // Pick smallest layout that fits all cameras
            int needed = cameras.Count;
            var layout = GridLayouts.FirstOrDefault(g => g.rows * g.cols >= needed);
            if (layout.rows == 0) layout = GridLayouts[GridLayouts.Length - 1];

            int gridSize = layout.rows * layout.cols;
            string viewName = $"{layout.rows}x{layout.cols}";

            var viewItem = sbGroup.GetChildren().FirstOrDefault(c => c.Name == viewName);
            if (viewItem == null) return;

            // Navigate to the grid view first
            var dest = GetTargetWindowFQID();
            EnvironmentManager.Instance.SendMessage(
                new Message(MessageId.SmartClient.MultiWindowCommand,
                    new MultiWindowCommandData
                    {
                        MultiWindowCommand = MultiWindowCommand.SetViewInWindow,
                        View = viewItem.FQID,
                        Window = dest
                    }), dest);

            // Insert cameras into the view slots
            for (int i = 0; i < cameras.Count && i < gridSize; i++)
            {
                SetCameraInSlot(i, cameras[i].PlatformItem.FQID);
            }
        }

        private void NavigateToView(CommandItem item)
        {
            var dest = GetTargetWindowFQID();
            EnvironmentManager.Instance.SendMessage(
                new Message(MessageId.SmartClient.MultiWindowCommand,
                    new MultiWindowCommandData
                    {
                        MultiWindowCommand = MultiWindowCommand.SetViewInWindow,
                        View = item.PlatformItem.FQID,
                        Window = dest
                    }), dest);
        }
    }

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

    class CommandItem
    {
        public string Name { get; set; }
        public string Group { get; set; }
        public ItemCategory Category { get; set; }
        public Item PlatformItem { get; set; }
        public Action Execute { get; set; }
    }
}
