using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using RemoteManager.Models;
using RemoteManager.Services;
using AxMSTSCLib;
using MSTSCLib;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using VideoOS.Platform.Client;

namespace RemoteManager.Client
{
    #region Converters

    public class NodeTypeToIconConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            switch ((RemoteNodeType)value)
            {
                case RemoteNodeType.Root:
                    return FontAwesome5.EFontAwesomeIcon.Solid_Server;
                case RemoteNodeType.Folder:
                    return FontAwesome5.EFontAwesomeIcon.Solid_Folder;
                case RemoteNodeType.HardwareWebsite:
                    return FontAwesome5.EFontAwesomeIcon.Solid_Microchip;
                case RemoteNodeType.UserWebsite:
                    return FontAwesome5.EFontAwesomeIcon.Solid_Globe;
                case RemoteNodeType.RdpConnection:
                    return FontAwesome5.EFontAwesomeIcon.Solid_Desktop;
                default:
                    return FontAwesome5.EFontAwesomeIcon.Solid_File;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    public class NodeTypeToColorConverter : IValueConverter
    {
        private static readonly Brush RootBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x60, 0xCD, 0xFF)));
        private static readonly Brush FolderBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0xB9, 0x00)));
        private static readonly Brush HardwareBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x8B, 0x94, 0x9E)));
        private static readonly Brush WebsiteBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50)));
        private static readonly Brush RdpBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x42, 0xA5, 0xF5)));

        private static SolidColorBrush Freeze(SolidColorBrush b) { b.Freeze(); return b; }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            switch ((RemoteNodeType)value)
            {
                case RemoteNodeType.Root: return RootBrush;
                case RemoteNodeType.Folder: return FolderBrush;
                case RemoteNodeType.HardwareWebsite: return HardwareBrush;
                case RemoteNodeType.UserWebsite: return WebsiteBrush;
                case RemoteNodeType.RdpConnection: return RdpBrush;
                default: return HardwareBrush;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    public class BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => (bool)value ? Visibility.Visible : Visibility.Collapsed;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    #endregion

    public partial class RemoteManagerViewItemWpfUserControl : ViewItemWpfUserControl
    {
        private readonly RemoteManagerViewItemManager _viewItemManager;
        private List<HardwareDeviceInfo> _allDevices = new List<HardwareDeviceInfo>();
        private List<HardwareDeviceInfo> _userEntries = new List<HardwareDeviceInfo>();
        private List<RdpConnectionInfo> _rdpEntries = new List<RdpConnectionInfo>();
        private readonly Dictionary<Guid, TabEntry> _openTabs = new Dictionary<Guid, TabEntry>();
        private Guid? _activeTabId;
        private bool _passwordVisible;
        private string _selectedUsername;
        private string _selectedPassword;
        private string _selectedDeviceName;
        private readonly string _webView2UserDataFolder;
        private CoreWebView2Environment _webView2Environment;
        private int _passwordReadGeneration;

        // Tree state
        private RemoteTreeNode _rootNode;
        private readonly ObservableCollection<RemoteTreeNode> _treeRoots = new ObservableCollection<RemoteTreeNode>();
        private bool _initialized;

        // Drag-drop state
        private Point _dragStartPoint;
        private bool _isDragging;
        private bool _dragOccurred;
        private TreeViewItem _highlightedItem;

        private static readonly SolidColorBrush DropValidBorderBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x60, 0xCD, 0xFF)));
        private static readonly SolidColorBrush DropValidBgBrush = Freeze(new SolidColorBrush(Color.FromArgb(15, 0x60, 0xCD, 0xFF)));
        private static readonly SolidColorBrush DropInvalidBorderBrush = Freeze(new SolidColorBrush(Color.FromArgb(80, 0xFF, 0x6B, 0x6B)));
        private static readonly SolidColorBrush DropInvalidBgBrush = Freeze(new SolidColorBrush(Color.FromArgb(10, 0xFF, 0x6B, 0x6B)));

        private static SolidColorBrush Freeze(SolidColorBrush b) { b.Freeze(); return b; }

        public RemoteManagerViewItemWpfUserControl(RemoteManagerViewItemManager viewItemManager)
        {
            _viewItemManager = viewItemManager;
            InitializeComponent();

            _webView2UserDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MSCPlugins", "RemoteManager", "WebView2Data");

            deviceTree.ItemsSource = _treeRoots;
        }

        public override void Init()
        {
            _initialized = true;
            autoAcceptCertsCheckBox.IsChecked = _viewItemManager.AutoAcceptCerts;
            autoLoginCheckBox.IsChecked = _viewItemManager.AutoLogin;
            _userEntries = _viewItemManager.GetUserEntries();
            _rdpEntries = _viewItemManager.GetRdpEntries();
            LoadDevicesAsync();
        }

        public override void Close()
        {
            foreach (var tab in _openTabs.Values)
            {
                tab.TabButton.MouseLeftButtonDown -= OnTabClicked;
                DisposeTabContent(tab);
            }
            _openTabs.Clear();
        }

        public override bool Selectable => false;
        public override bool ShowToolbar => false;

        #region Device Loading

        private async void LoadDevicesAsync()
        {
            loadingOverlay.Visibility = Visibility.Visible;

            try
            {
                _allDevices = await Task.Run(() => DeviceDiscoveryService.DiscoverDevices());
                BuildTree();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[RemoteManager] Failed to load devices: {ex.Message}");
            }
            finally
            {
                loadingOverlay.Visibility = Visibility.Collapsed;
            }
        }

        #endregion

        #region Tree Building

        private void BuildTree()
        {
            var savedTree = _viewItemManager.GetTreeStructure();

            // Create root node
            _rootNode = new RemoteTreeNode("Remote Manager", RemoteNodeType.Root,
                Guid.Empty, isSystemDefined: true, isExpanded: true);

            // Build lookup tables for items
            var hwLookup = _allDevices.ToDictionary(d => d.HardwareId);
            var webLookup = _userEntries.ToDictionary(e => e.HardwareId);
            var rdpLookup = _rdpEntries.ToDictionary(r => r.Id);

            // Track which items have been placed in the tree
            var placedHw = new HashSet<Guid>();
            var placedWeb = new HashSet<Guid>();
            var placedRdp = new HashSet<Guid>();

            // Rebuild tree from saved structure
            if (savedTree.Children != null && savedTree.Children.Count > 0)
            {
                RestoreChildren(_rootNode, savedTree.Children,
                    hwLookup, webLookup, rdpLookup,
                    placedHw, placedWeb, placedRdp);
            }

            // Add any new hardware devices not yet in the tree (discovered after last save)
            foreach (var dev in _allDevices.OrderBy(d => d.Name, NaturalSort))
            {
                if (!placedHw.Contains(dev.HardwareId))
                {
                    _rootNode.Children.Add(CreateHardwareNode(dev));
                }
            }

            // Add any new user entries not yet in the tree
            foreach (var entry in _userEntries)
            {
                if (!placedWeb.Contains(entry.HardwareId))
                {
                    _rootNode.Children.Add(CreateWebsiteNode(entry));
                }
            }

            // Add any new RDP entries not yet in the tree
            foreach (var rdp in _rdpEntries)
            {
                if (!placedRdp.Contains(rdp.Id))
                {
                    _rootNode.Children.Add(CreateRdpNode(rdp));
                }
            }

            _treeRoots.Clear();
            _treeRoots.Add(_rootNode);

            // Attach context menus after tree is built
            deviceTree.Dispatcher.BeginInvoke(new Action(AttachContextMenus),
                System.Windows.Threading.DispatcherPriority.Loaded);

            ApplySearchFilter();
        }

        private void RestoreChildren(RemoteTreeNode parent, List<TreeNodeData> childrenData,
            Dictionary<Guid, HardwareDeviceInfo> hwLookup,
            Dictionary<Guid, HardwareDeviceInfo> webLookup,
            Dictionary<Guid, RdpConnectionInfo> rdpLookup,
            HashSet<Guid> placedHw, HashSet<Guid> placedWeb, HashSet<Guid> placedRdp)
        {
            foreach (var data in childrenData)
            {
                if (!Guid.TryParse(data.Id, out var id)) continue;

                RemoteTreeNode node = null;

                switch (data.Type)
                {
                    case "folder":
                        node = new RemoteTreeNode(data.Name ?? "Folder", RemoteNodeType.Folder,
                            id, isExpanded: data.Expanded);
                        if (data.Children != null)
                        {
                            RestoreChildren(node, data.Children,
                                hwLookup, webLookup, rdpLookup,
                                placedHw, placedWeb, placedRdp);
                        }
                        break;

                    case "hardware":
                        if (hwLookup.TryGetValue(id, out var hw))
                        {
                            node = CreateHardwareNode(hw);
                            placedHw.Add(id);
                        }
                        break;

                    case "website":
                        if (webLookup.TryGetValue(id, out var web))
                        {
                            node = CreateWebsiteNode(web);
                            placedWeb.Add(id);
                        }
                        break;

                    case "rdp":
                        if (rdpLookup.TryGetValue(id, out var rdp))
                        {
                            node = CreateRdpNode(rdp);
                            placedRdp.Add(id);
                        }
                        break;
                }

                if (node != null)
                    parent.Children.Add(node);
            }
        }

        private RemoteTreeNode CreateHardwareNode(HardwareDeviceInfo hw)
        {
            return new RemoteTreeNode(hw.Name ?? hw.IpAddress, RemoteNodeType.HardwareWebsite,
                hw.HardwareId, isSystemDefined: true);
        }

        private RemoteTreeNode CreateWebsiteNode(HardwareDeviceInfo web)
        {
            return new RemoteTreeNode(web.Name ?? web.Address, RemoteNodeType.UserWebsite,
                web.HardwareId);
        }

        private RemoteTreeNode CreateRdpNode(RdpConnectionInfo rdp)
        {
            return new RemoteTreeNode(rdp.Name ?? rdp.Host, RemoteNodeType.RdpConnection,
                rdp.Id);
        }

        #endregion

        #region Tree Persistence

        private void SaveTreeStructure()
        {
            if (_rootNode == null) return;

            var tree = new TreeStructure
            {
                Children = SerializeChildren(_rootNode)
            };
            _viewItemManager.SetTreeStructure(tree);
            _viewItemManager.Save();
        }

        private List<TreeNodeData> SerializeChildren(RemoteTreeNode parent)
        {
            var list = new List<TreeNodeData>();
            foreach (var child in parent.Children)
            {
                var data = new TreeNodeData { Id = child.Id.ToString() };

                switch (child.NodeType)
                {
                    case RemoteNodeType.Folder:
                        data.Type = "folder";
                        data.Name = child.Name;
                        data.Expanded = child.IsExpanded;
                        data.Children = SerializeChildren(child);
                        break;
                    case RemoteNodeType.HardwareWebsite:
                        data.Type = "hardware";
                        break;
                    case RemoteNodeType.UserWebsite:
                        data.Type = "website";
                        break;
                    case RemoteNodeType.RdpConnection:
                        data.Type = "rdp";
                        break;
                }

                list.Add(data);
            }
            return list;
        }

        #endregion

        #region Search

        private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_initialized) return;
            searchPlaceholder.Visibility = string.IsNullOrEmpty(searchBox.Text)
                ? Visibility.Visible : Visibility.Collapsed;
            ApplySearchFilter();
        }

        private void ApplySearchFilter()
        {
            if (_rootNode == null) return;

            var query = searchBox.Text?.Trim();
            var isSearching = !string.IsNullOrWhiteSpace(query);

            if (!isSearching)
            {
                SetAllVisible(_rootNode);
                return;
            }

            FilterNode(_rootNode, query);
        }

        /// <summary>
        /// Returns true if this node or any descendant matches the search query.
        /// </summary>
        private bool FilterNode(RemoteTreeNode node, string query)
        {
            // Root is always visible
            if (node.NodeType == RemoteNodeType.Root)
            {
                bool anyChildVisible = false;
                foreach (var child in node.Children)
                {
                    if (FilterNode(child, query))
                        anyChildVisible = true;
                }
                node.IsVisible = true;
                if (anyChildVisible) node.IsExpanded = true;
                return true;
            }

            // For folders, check children first
            if (node.IsContainer)
            {
                bool anyChildVisible = false;
                foreach (var child in node.Children)
                {
                    if (FilterNode(child, query))
                        anyChildVisible = true;
                }

                bool selfMatch = MatchesSearch(node, query);
                node.IsVisible = selfMatch || anyChildVisible;
                if (anyChildVisible) node.IsExpanded = true;
                return node.IsVisible;
            }

            // Leaf nodes
            bool matches = MatchesSearch(node, query);
            node.IsVisible = matches;
            return matches;
        }

        private bool MatchesSearch(RemoteTreeNode node, string query)
        {
            if (node.Name != null && node.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            // Also search by address/host
            var address = GetNodeAddress(node);
            if (address != null && address.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            return false;
        }

        private string GetNodeAddress(RemoteTreeNode node)
        {
            switch (node.NodeType)
            {
                case RemoteNodeType.HardwareWebsite:
                    var hw = _allDevices.FirstOrDefault(d => d.HardwareId == node.Id);
                    return hw?.IpAddress;
                case RemoteNodeType.UserWebsite:
                    var web = _userEntries.FirstOrDefault(e => e.HardwareId == node.Id);
                    return web?.Address;
                case RemoteNodeType.RdpConnection:
                    var rdp = _rdpEntries.FirstOrDefault(r => r.Id == node.Id);
                    return rdp?.Host;
                default:
                    return null;
            }
        }

        private void SetAllVisible(RemoteTreeNode node)
        {
            node.IsVisible = true;
            foreach (var child in node.Children)
                SetAllVisible(child);
        }

        #endregion

        #region Tree Selection

        private void OpenNodeItem(RemoteTreeNode node)
        {
            if (node == null) return;

            switch (node.NodeType)
            {
                case RemoteNodeType.HardwareWebsite:
                    var hw = _allDevices.FirstOrDefault(d => d.HardwareId == node.Id);
                    if (hw != null)
                    {
                        OpenOrFocusWebViewTab(hw);
                        ReadPasswordOnDemand(hw);
                    }
                    break;

                case RemoteNodeType.UserWebsite:
                    var web = _userEntries.FirstOrDefault(e2 => e2.HardwareId == node.Id);
                    if (web != null)
                    {
                        OpenOrFocusWebViewTab(web);
                        UpdateCredentialBar(web.Name, web.Username, web.Password, false);
                    }
                    break;

                case RemoteNodeType.RdpConnection:
                    var rdp = _rdpEntries.FirstOrDefault(r => r.Id == node.Id);
                    if (rdp != null)
                    {
                        OpenOrFocusRdpTab(rdp);
                        UpdateCredentialBar(rdp.Name, rdp.Username, rdp.Password, false);
                    }
                    break;
            }
        }

        private async void ReadPasswordOnDemand(HardwareDeviceInfo device)
        {
            if (!device.IsUserDefined && device.Password == null && !string.IsNullOrEmpty(device.HardwarePath))
            {
                var generation = ++_passwordReadGeneration;
                try
                {
                    var pwd = await Task.Run(() => DeviceDiscoveryService.ReadPassword(device.HardwarePath));
                    device.Password = pwd ?? "";
                    if (_passwordReadGeneration == generation && _selectedDeviceName == device.Name)
                    {
                        _selectedPassword = device.Password;
                        _passwordVisible = false;
                        passwordText.Text = MaskPassword(device.Password);
                        togglePasswordButton.Content = "Show";
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[RemoteManager] Password read failed: {ex.Message}");
                    device.Password = "";
                }
            }

            _selectedDeviceName = device.Name;
            _selectedUsername = device.Username;
            _selectedPassword = device.Password;
            UpdateCredentialBar(device.Name, device.Username, device.Password, !device.IsUserDefined);
        }

        #endregion

        #region Context Menus

        private void AttachContextMenus()
        {
            AttachContextMenusRecursive(_rootNode);
        }

        private void AttachContextMenusRecursive(RemoteTreeNode node)
        {
            var tvi = GetTreeViewItem(node);
            if (tvi != null)
            {
                var rowBorder = FindChild<Border>(tvi, "RowBorder");
                if (rowBorder != null)
                    rowBorder.ContextMenu = CreateContextMenuForNode(node);
            }

            foreach (var child in node.Children)
                AttachContextMenusRecursive(child);
        }

        private ContextMenu CreateContextMenuForNode(RemoteTreeNode node)
        {
            var items = new List<(string text, RoutedEventHandler handler, bool enabled)>();

            switch (node.NodeType)
            {
                case RemoteNodeType.Root:
                    items.Add(("New Folder", (s, e) => AddFolder(node), true));
                    items.Add(("Add Website", (s, e) => OnAddWebViewEntry(node), true));
                    items.Add(("Add RDP Connection", (s, e) => OnAddRdpEntry(node), true));
                    break;

                case RemoteNodeType.Folder:
                    items.Add(("New Folder", (s, e) => AddFolder(node), true));
                    items.Add(("Add Website", (s, e) => OnAddWebViewEntry(node), true));
                    items.Add(("Add RDP Connection", (s, e) => OnAddRdpEntry(node), true));
                    if (!node.IsSystemDefined)
                    {
                        items.Add(("Rename", (s, e) => RenameFolder(node), true));
                        items.Add(("Delete", (s, e) => DeleteNode(node), true));
                    }
                    break;

                case RemoteNodeType.UserWebsite:
                    items.Add(("Edit", (s, e) => EditWebViewEntry(node), true));
                    items.Add(("Delete", (s, e) => DeleteNode(node), true));
                    break;

                case RemoteNodeType.RdpConnection:
                    items.Add(("Edit", (s, e) => EditRdpEntry(node), true));
                    items.Add(("Delete", (s, e) => DeleteNode(node), true));
                    break;

                case RemoteNodeType.HardwareWebsite:
                    // Hardware items have no context menu actions
                    return null;
            }

            return CreateDarkContextMenu(items.ToArray());
        }

        private ContextMenu CreateDarkContextMenu(params (string text, RoutedEventHandler handler, bool enabled)[] items)
        {
            var menu = new ContextMenu
            {
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF2A2A2A")),
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF444444")),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(0),
                HasDropShadow = false,
                Template = CreateDarkContextMenuTemplate(),
            };

            foreach (var (text, handler, enabled) in items)
            {
                var mi = new MenuItem
                {
                    Header = text,
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFD0D0D0")),
                    Template = CreateDarkMenuItemTemplate(),
                    IsEnabled = enabled,
                };
                mi.Click += handler;
                menu.Items.Add(mi);
            }

            return menu;
        }

        #endregion

        #region Tree Actions (Add / Rename / Delete)

        private void AddFolder(RemoteTreeNode parentNode)
        {
            var name = PromptInput("New Folder", "Folder name:", "New Folder");
            if (name == null) return;

            var folder = new RemoteTreeNode(name, RemoteNodeType.Folder, Guid.NewGuid());
            parentNode.Children.Add(folder);
            parentNode.IsExpanded = true;

            SaveTreeStructure();
            RefreshContextMenu(folder);
        }

        private void RenameFolder(RemoteTreeNode node)
        {
            var name = PromptInput("Rename Folder", "New name:", node.Name);
            if (name == null || name == node.Name) return;

            node.Name = name;
            SaveTreeStructure();
        }

        private void DeleteNode(RemoteTreeNode node)
        {
            switch (node.NodeType)
            {
                case RemoteNodeType.Folder:
                    // Check if folder contains system nodes
                    bool hasSystemChildren = HasSystemDescendants(node);
                    var message = hasSystemChildren
                        ? $"Delete folder \"{node.Name}\"?\nSystem items (hardware) will be moved back to root."
                        : $"Delete folder \"{node.Name}\" and all its contents?";

                    if (MessageBox.Show(message, "Delete Folder",
                        MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
                        return;

                    // Close any open tabs for items in this folder
                    CloseTabsInFolder(node);
                    // Delete user-defined items in folder from data store
                    RemoveUserItemsInFolder(node);
                    // Delete the folder (system children get rescued to root)
                    node.Delete(_rootNode);
                    break;

                case RemoteNodeType.UserWebsite:
                    var web = _userEntries.FirstOrDefault(e => e.HardwareId == node.Id);
                    if (web != null)
                    {
                        _userEntries.Remove(web);
                        _viewItemManager.SetUserEntries(_userEntries);
                        if (_openTabs.ContainsKey(node.Id))
                            CloseTab(node.Id);
                    }
                    node.Parent?.Children.Remove(node);
                    break;

                case RemoteNodeType.RdpConnection:
                    var rdp = _rdpEntries.FirstOrDefault(r => r.Id == node.Id);
                    if (rdp != null)
                    {
                        _rdpEntries.Remove(rdp);
                        _viewItemManager.SetRdpEntries(_rdpEntries);
                        if (_openTabs.ContainsKey(node.Id))
                            CloseTab(node.Id);
                    }
                    node.Parent?.Children.Remove(node);
                    break;
            }

            SaveTreeStructure();
        }

        private bool HasSystemDescendants(RemoteTreeNode node)
        {
            foreach (var child in node.Children)
            {
                if (child.IsSystemDefined) return true;
                if (child.IsContainer && HasSystemDescendants(child)) return true;
            }
            return false;
        }

        private void CloseTabsInFolder(RemoteTreeNode folder)
        {
            foreach (var child in folder.Children)
            {
                if (child.IsContainer)
                    CloseTabsInFolder(child);
                else if (_openTabs.ContainsKey(child.Id))
                    CloseTab(child.Id);
            }
        }

        private void RemoveUserItemsInFolder(RemoteTreeNode folder)
        {
            for (int i = folder.Children.Count - 1; i >= 0; i--)
            {
                var child = folder.Children[i];
                if (child.IsContainer)
                    RemoveUserItemsInFolder(child);

                if (child.NodeType == RemoteNodeType.UserWebsite)
                {
                    var web = _userEntries.FirstOrDefault(e => e.HardwareId == child.Id);
                    if (web != null) _userEntries.Remove(web);
                }
                else if (child.NodeType == RemoteNodeType.RdpConnection)
                {
                    var rdp = _rdpEntries.FirstOrDefault(r => r.Id == child.Id);
                    if (rdp != null) _rdpEntries.Remove(rdp);
                }
            }
            _viewItemManager.SetUserEntries(_userEntries);
            _viewItemManager.SetRdpEntries(_rdpEntries);
        }

        private void OnAddWebViewEntry(RemoteTreeNode parentNode)
        {
            var dialog = new AddWebViewEntryWindow();
            dialog.Owner = Window.GetWindow(this);
            if (dialog.ShowDialog() == true)
            {
                var entry = new HardwareDeviceInfo
                {
                    Name = dialog.EntryName,
                    Address = dialog.EntryUrl,
                    Username = dialog.EntryUsername,
                    Password = dialog.EntryPassword,
                    IsUserDefined = true,
                    HardwareId = Guid.NewGuid(),
                    RecordingServerName = "User Defined",
                };

                _userEntries.Add(entry);
                _viewItemManager.SetUserEntries(_userEntries);
                _viewItemManager.Save();

                var node = CreateWebsiteNode(entry);
                parentNode.Children.Add(node);
                parentNode.IsExpanded = true;
                SaveTreeStructure();
                RefreshContextMenu(node);
            }
        }

        private void OnAddRdpEntry(RemoteTreeNode parentNode)
        {
            var dialog = new AddRdpEntryWindow();
            dialog.Owner = Window.GetWindow(this);
            if (dialog.ShowDialog() == true)
            {
                var entry = new RdpConnectionInfo
                {
                    Name = dialog.EntryName,
                    Host = dialog.EntryHost,
                    Port = dialog.EntryPort,
                    Username = dialog.EntryUsername,
                    Password = dialog.EntryPassword,
                    EnableNLA = dialog.EntryEnableNLA,
                    EnableClipboard = dialog.EntryEnableClipboard,
                    Id = Guid.NewGuid(),
                };

                _rdpEntries.Add(entry);
                _viewItemManager.SetRdpEntries(_rdpEntries);
                _viewItemManager.Save();

                var node = CreateRdpNode(entry);
                parentNode.Children.Add(node);
                parentNode.IsExpanded = true;
                SaveTreeStructure();
                RefreshContextMenu(node);
            }
        }

        private void EditWebViewEntry(RemoteTreeNode node)
        {
            var entry = _userEntries.FirstOrDefault(e => e.HardwareId == node.Id);
            if (entry == null) return;

            var dialog = new AddWebViewEntryWindow(entry);
            dialog.Owner = Window.GetWindow(this);
            if (dialog.ShowDialog() == true)
            {
                if (_openTabs.ContainsKey(entry.HardwareId))
                    CloseTab(entry.HardwareId);

                entry.Name = dialog.EntryName;
                entry.Address = dialog.EntryUrl;
                entry.Username = dialog.EntryUsername;
                entry.Password = dialog.EntryPassword;

                _viewItemManager.SetUserEntries(_userEntries);
                _viewItemManager.Save();

                node.Name = entry.Name;
            }
        }

        private void EditRdpEntry(RemoteTreeNode node)
        {
            var entry = _rdpEntries.FirstOrDefault(r => r.Id == node.Id);
            if (entry == null) return;

            var dialog = new AddRdpEntryWindow(entry);
            dialog.Owner = Window.GetWindow(this);
            if (dialog.ShowDialog() == true)
            {
                if (_openTabs.ContainsKey(entry.Id))
                    CloseTab(entry.Id);

                entry.Name = dialog.EntryName;
                entry.Host = dialog.EntryHost;
                entry.Port = dialog.EntryPort;
                entry.Username = dialog.EntryUsername;
                entry.Password = dialog.EntryPassword;
                entry.EnableNLA = dialog.EntryEnableNLA;
                entry.EnableClipboard = dialog.EntryEnableClipboard;

                _viewItemManager.SetRdpEntries(_rdpEntries);
                _viewItemManager.Save();

                node.Name = entry.Name;
            }
        }

        #endregion

        #region Drag and Drop

        private void TreeViewItem_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            _dragStartPoint = e.GetPosition(deviceTree);
            _isDragging = false;
            _dragOccurred = false;
        }

        private void TreeViewItem_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_dragOccurred) return;
            if (!_initialized) return;

            var item = FindTreeViewItem(e.OriginalSource as DependencyObject);
            var node = item?.DataContext as RemoteTreeNode;
            if (node != null && !node.IsContainer)
            {
                OpenNodeItem(node);
            }
        }

        private void TreeViewItem_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed || _isDragging) return;

            var pos = e.GetPosition(deviceTree);
            if (Math.Abs(pos.X - _dragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(pos.Y - _dragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
                return;

            var item = FindTreeViewItem(e.OriginalSource as DependencyObject);
            var node = item?.DataContext as RemoteTreeNode;
            // Cannot drag root node
            if (node == null || node.NodeType == RemoteNodeType.Root) return;

            _isDragging = true;
            _dragOccurred = true;
            DragDrop.DoDragDrop(item, node, DragDropEffects.Move);
            _isDragging = false;
            ClearHighlight();
        }

        private void TreeViewItem_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;

            var item = FindTreeViewItem(e.OriginalSource as DependencyObject);
            var target = item?.DataContext as RemoteTreeNode;
            var source = e.Data.GetData(typeof(RemoteTreeNode)) as RemoteTreeNode;
            if (source == null || target == null) { ClearHighlight(); return; }

            bool valid = CanDrop(source, target);
            if (valid) e.Effects = DragDropEffects.Move;

            SetHighlight(item, valid);
        }

        private void TreeViewItem_DragLeave(object sender, DragEventArgs e)
        {
            var item = sender as TreeViewItem;
            if (item == _highlightedItem)
            {
                var pos = e.GetPosition(item);
                if (pos.X < 0 || pos.Y < 0 || pos.X > item.ActualWidth || pos.Y > item.ActualHeight)
                    ClearHighlight();
            }
        }

        private void TreeViewItem_Drop(object sender, DragEventArgs e)
        {
            e.Handled = true;
            ClearHighlight();

            var item = FindTreeViewItem(e.OriginalSource as DependencyObject);
            var target = item?.DataContext as RemoteTreeNode;
            var source = e.Data.GetData(typeof(RemoteTreeNode)) as RemoteTreeNode;

            if (source == null || target == null || !CanDrop(source, target)) return;

            var destination = target.IsContainer ? target : target.Parent;
            if (destination != null)
            {
                source.MoveTo(destination);
                destination.IsExpanded = true;
                SaveTreeStructure();

                // Refresh context menu for moved node
                RefreshContextMenu(source);
            }
        }

        private bool CanDrop(RemoteTreeNode source, RemoteTreeNode target)
        {
            if (source == target) return false;
            if (source.NodeType == RemoteNodeType.Root) return false;
            var dest = target.IsContainer ? target : target.Parent;
            if (dest == null || dest == source.Parent) return false;
            if (source.IsAncestorOf(dest)) return false;
            return true;
        }

        private void SetHighlight(TreeViewItem item, bool valid)
        {
            ClearHighlight();
            _highlightedItem = item;
            if (item == null) return;

            var rowBorder = FindChild<Border>(item, "RowBorder");
            if (rowBorder != null)
            {
                rowBorder.Background = valid ? DropValidBgBrush : DropInvalidBgBrush;
                rowBorder.BorderBrush = valid ? DropValidBorderBrush : DropInvalidBorderBrush;
                rowBorder.BorderThickness = new Thickness(1);
            }
        }

        private void ClearHighlight()
        {
            if (_highlightedItem != null)
            {
                var rowBorder = FindChild<Border>(_highlightedItem, "RowBorder");
                if (rowBorder != null)
                {
                    rowBorder.ClearValue(Border.BackgroundProperty);
                    rowBorder.ClearValue(Border.BorderBrushProperty);
                    rowBorder.ClearValue(Border.BorderThicknessProperty);
                }
                _highlightedItem = null;
            }
        }

        #endregion

        #region Credential Bar

        private void UpdateCredentialBar(string name, string username, string password, bool showBar)
        {
            bool hasCreds = !string.IsNullOrEmpty(username) || !string.IsNullOrEmpty(password) || showBar;
            credentialBar.Visibility = hasCreds ? Visibility.Visible : Visibility.Collapsed;
            if (!hasCreds) return;

            deviceNameText.Text = name ?? "";
            usernameText.Text = username ?? "";
            _passwordVisible = false;
            passwordText.Text = MaskPassword(password);
            togglePasswordButton.Content = "Show";
        }

        private string MaskPassword(string password)
        {
            if (string.IsNullOrEmpty(password)) return "";
            return new string('\u2022', password.Length);
        }

        private void OnCopyUsername(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(_selectedUsername))
            {
                try { Clipboard.SetText(_selectedUsername); } catch { }
            }
        }

        private void OnCopyPassword(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(_selectedPassword))
            {
                try { Clipboard.SetText(_selectedPassword); } catch { }
            }
        }

        private void OnTogglePassword(object sender, RoutedEventArgs e)
        {
            _passwordVisible = !_passwordVisible;
            passwordText.Text = _passwordVisible
                ? (_selectedPassword ?? "")
                : MaskPassword(_selectedPassword);
            togglePasswordButton.Content = _passwordVisible ? "Hide" : "Show";
        }

        #endregion

        #region Tab Management

        private void OpenOrFocusWebViewTab(HardwareDeviceInfo device)
        {
            if (_openTabs.ContainsKey(device.HardwareId))
            {
                ActivateTab(device.HardwareId);
                return;
            }

            var url = device.IsUserDefined ? device.Address : device.WebUrl;
            if (string.IsNullOrEmpty(url)) return;

            var webView = new WebView2();
            webView.Visibility = Visibility.Collapsed;

            var tab = new TabEntry
            {
                Id = device.HardwareId,
                Name = device.Name,
                TabType = TabType.WebView,
                WebView = webView,
                WebInfo = device,
                TabButton = CreateTabButton(device.HardwareId, device.Name, TabType.WebView)
            };

            _openTabs[device.HardwareId] = tab;
            browserHost.Children.Add(webView);
            tabStrip.Children.Add(tab.TabButton);

            InitializeWebView(tab, url);
            ActivateTab(device.HardwareId);
        }

        private void OpenOrFocusRdpTab(RdpConnectionInfo rdp)
        {
            if (_openTabs.ContainsKey(rdp.Id))
            {
                ActivateTab(rdp.Id);
                return;
            }

            if (string.IsNullOrEmpty(rdp.Host)) return;

            var container = new Grid();
            container.Visibility = Visibility.Collapsed;

            var host = new System.Windows.Forms.Integration.WindowsFormsHost();
            var overlay = CreateRdpOverlay(rdp);

            container.Children.Add(host);
            container.Children.Add(overlay);

            var tab = new TabEntry
            {
                Id = rdp.Id,
                Name = rdp.Name,
                TabType = TabType.Rdp,
                RdpHost = host,
                RdpContainer = container,
                RdpOverlay = overlay,
                RdpInfo = rdp,
                TabButton = CreateTabButton(rdp.Id, rdp.Name, TabType.Rdp)
            };

            _openTabs[rdp.Id] = tab;
            browserHost.Children.Add(container);
            tabStrip.Children.Add(tab.TabButton);

            ActivateTab(rdp.Id);
            ConnectRdp(tab);
        }

        private Border CreateRdpOverlay(RdpConnectionInfo rdp)
        {
            var nameText = new TextBlock
            {
                Text = rdp.Name ?? "Remote Desktop",
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFE6EDF3")),
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 4)
            };

            var infoText = new TextBlock
            {
                Text = rdp.DisplayAddress,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF8B949E")),
                FontSize = 13,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 12)
            };

            var statusText = new TextBlock
            {
                Text = "Connecting...",
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF42A5F5")),
                FontSize = 13,
                HorizontalAlignment = HorizontalAlignment.Center,
                Tag = "statusText"
            };

            var stack = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            stack.Children.Add(nameText);
            stack.Children.Add(infoText);
            stack.Children.Add(statusText);

            return new Border
            {
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF1C2326")),
                Child = stack,
                Visibility = Visibility.Visible
            };
        }

        private void SetRdpOverlayStatus(TabEntry tab, string message, bool isError)
        {
            if (tab.RdpOverlay?.Child is StackPanel stack)
            {
                foreach (var child in stack.Children)
                {
                    if (child is TextBlock tb && tb.Tag as string == "statusText")
                    {
                        tb.Text = message;
                        tb.Foreground = new SolidColorBrush(isError
                            ? (Color)ColorConverter.ConvertFromString("#FFFF6B6B")
                            : (Color)ColorConverter.ConvertFromString("#FF42A5F5"));
                        break;
                    }
                }
            }
        }

        private async void InitializeWebView(TabEntry tab, string url)
        {
            try
            {
                if (_webView2Environment == null)
                {
                    _webView2Environment = await CoreWebView2Environment.CreateAsync(
                        null, _webView2UserDataFolder);
                }

                await tab.WebView.EnsureCoreWebView2Async(_webView2Environment);

                if (_viewItemManager.AutoAcceptCerts)
                {
                    tab.WebView.CoreWebView2.ServerCertificateErrorDetected += OnCertificateError;
                }

                tab.WebView.CoreWebView2.BasicAuthenticationRequested += (s, e) =>
                {
                    OnBasicAuthRequested(tab, e);
                };

                tab.WebView.CoreWebView2.Navigate(url);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[RemoteManager] WebView2 init error: {ex.Message}");
            }
        }

        private void OnCertificateError(object sender, CoreWebView2ServerCertificateErrorDetectedEventArgs e)
        {
            e.Action = CoreWebView2ServerCertificateErrorAction.AlwaysAllow;
        }

        private void OnBasicAuthRequested(TabEntry tab, CoreWebView2BasicAuthenticationRequestedEventArgs e)
        {
            if (!_viewItemManager.AutoLogin) return;
            if (tab.AuthAttempted) return; // Only auto-fill once to avoid infinite retry loop

            var device = tab.WebInfo;
            if (device == null) return;

            var username = device.Username;
            var password = device.Password;

            if (string.IsNullOrEmpty(username) && string.IsNullOrEmpty(password)) return;

            tab.AuthAttempted = true;
            e.Response.UserName = username ?? "";
            e.Response.Password = password ?? "";
        }

        private void ActivateTab(Guid tabId)
        {
            foreach (var kvp in _openTabs)
            {
                if (kvp.Value.WebView != null) kvp.Value.WebView.Visibility = Visibility.Collapsed;
                if (kvp.Value.RdpContainer != null) kvp.Value.RdpContainer.Visibility = Visibility.Collapsed;
                UpdateTabButtonStyle(kvp.Value.TabButton, false);
            }

            if (_openTabs.TryGetValue(tabId, out var active))
            {
                if (active.WebView != null) active.WebView.Visibility = Visibility.Visible;
                if (active.RdpContainer != null)
                {
                    active.RdpContainer.Visibility = Visibility.Visible;
                }
                UpdateTabButtonStyle(active.TabButton, true);
                _activeTabId = tabId;
                welcomePanel.Visibility = Visibility.Collapsed;

                // Sync tree selection to match active tab
                SelectTreeNodeById(tabId);
            }
        }

        private void CloseTab(Guid tabId)
        {
            if (!_openTabs.TryGetValue(tabId, out var tab)) return;

            if (tab.WebView != null) browserHost.Children.Remove(tab.WebView);
            if (tab.RdpContainer != null) browserHost.Children.Remove(tab.RdpContainer);
            tabStrip.Children.Remove(tab.TabButton);

            tab.TabButton.MouseLeftButtonDown -= OnTabClicked;
            DisposeTabContent(tab);

            _openTabs.Remove(tabId);

            if (_activeTabId == tabId)
            {
                _activeTabId = null;
                if (_openTabs.Count > 0)
                {
                    ActivateTab(_openTabs.Keys.Last());
                }
                else
                {
                    welcomePanel.Visibility = Visibility.Visible;
                    credentialBar.Visibility = Visibility.Collapsed;
                    ClearTreeSelection();
                }
            }
        }

        private void DisposeTabContent(TabEntry tab)
        {
            if (tab.WebView != null)
            {
                if (tab.WebView.CoreWebView2 != null)
                    tab.WebView.CoreWebView2.ServerCertificateErrorDetected -= OnCertificateError;
                try { tab.WebView.Dispose(); } catch { }
            }

            if (tab.RdpClient != null)
            {
                DisconnectRdp(tab);
            }
        }

        private Border CreateTabButton(Guid id, string name, TabType type)
        {
            var closeBtn = new Button
            {
                Content = "\u00D7",
                Style = (Style)FindResource("TabCloseButton"),
                Tag = id
            };
            closeBtn.Click += OnTabCloseClicked;

            var icon = new FontAwesome5.ImageAwesome
            {
                Icon = type == TabType.Rdp
                    ? FontAwesome5.EFontAwesomeIcon.Solid_Desktop
                    : FontAwesome5.EFontAwesomeIcon.Solid_Globe,
                Width = 10,
                Height = 10,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(
                    type == TabType.Rdp ? "#FF42A5F5" : "#FF8B949E")),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 5, 0)
            };

            var displayName = name ?? "";
            var header = new TextBlock
            {
                Text = displayName.Length > 22 ? displayName.Substring(0, 19) + "..." : displayName,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFE6EDF3")),
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0)
            };

            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            panel.Children.Add(icon);
            panel.Children.Add(header);
            panel.Children.Add(closeBtn);

            var border = new Border
            {
                Child = panel,
                Padding = new Thickness(10, 6, 6, 6),
                Margin = new Thickness(0, 0, 1, 0),
                Cursor = Cursors.Hand,
                Tag = id,
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF1C2326"))
            };
            border.MouseLeftButtonDown += OnTabClicked;

            return border;
        }

        private void UpdateTabButtonStyle(Border tabButton, bool active)
        {
            tabButton.Background = new SolidColorBrush(
                (Color)ColorConverter.ConvertFromString(active ? "#FF252A2E" : "#FF0D1117"));
        }

        private void OnTabClicked(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border border && border.Tag is Guid id)
                ActivateTab(id);
        }

        private void OnTabCloseClicked(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is Guid id)
                CloseTab(id);
        }

        #endregion

        #region RDP Connection

        private void ConnectRdp(TabEntry tab)
        {
            var rdp = tab.RdpInfo;
            if (rdp == null || string.IsNullOrEmpty(rdp.Host)) return;

            if (tab.RdpOverlay != null) tab.RdpOverlay.Visibility = Visibility.Visible;
            SetRdpOverlayStatus(tab, "Connecting...", false);

            try
            {
                tab.RdpHost.Width = 1;
                tab.RdpHost.Height = 1;
                tab.RdpHost.Visibility = Visibility.Visible;
                tab.RdpHost.UpdateLayout();

                var client = new AxMsRdpClient8NotSafeForScripting();
                tab.RdpHost.Child = client;
                tab.RdpClient = client;

                client.OnConnected += (s, ev) =>
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (tab.RdpOverlay != null) tab.RdpOverlay.Visibility = Visibility.Collapsed;
                        tab.RdpHost.Width = double.NaN;
                        tab.RdpHost.Height = double.NaN;
                    }));
                };

                client.OnDisconnected += (s, ev) =>
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        var reason = ((IMsTscAxEvents_OnDisconnectedEvent)ev).discReason;
                        Debug.WriteLine($"[RemoteManager] RDP disconnected: code {reason}");

                        tab.RdpHost.Width = 1;
                        tab.RdpHost.Height = 1;
                        if (tab.RdpOverlay != null) tab.RdpOverlay.Visibility = Visibility.Visible;

                        if (reason == 1 || reason == 2 || reason == 3)
                            SetRdpOverlayStatus(tab, "Disconnected", false);
                        else
                            SetRdpOverlayStatus(tab, $"{GetDisconnectReason(reason)} (code {reason})", true);
                    }));
                };

                client.OnFatalError += (s, ev) =>
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        var code = ((IMsTscAxEvents_OnFatalErrorEvent)ev).errorCode;
                        Debug.WriteLine($"[RemoteManager] RDP fatal error: {code}");

                        tab.RdpHost.Width = 1;
                        tab.RdpHost.Height = 1;
                        if (tab.RdpOverlay != null) tab.RdpOverlay.Visibility = Visibility.Visible;
                        SetRdpOverlayStatus(tab, $"Fatal error (code {code})", true);
                    }));
                };

                tab.RdpHost.UpdateLayout();

                client.Server = rdp.Host;
                client.UserName = rdp.Username ?? "";

                int width = (int)browserHost.ActualWidth;
                int height = (int)browserHost.ActualHeight;
                if (width < 200) width = 1024;
                if (height < 200) height = 768;
                client.DesktopWidth = width;
                client.DesktopHeight = height;
                client.ColorDepth = 32;

                client.AdvancedSettings8.SmartSizing = true;
                client.AdvancedSettings8.EnableCredSspSupport = rdp.EnableNLA;
                client.AdvancedSettings8.RDPPort = rdp.Port;
                client.AdvancedSettings8.AuthenticationLevel = 2;
                client.AdvancedSettings8.NegotiateSecurityLayer = !rdp.EnableNLA;

                client.AdvancedSettings8.RedirectDrives = false;
                client.AdvancedSettings8.RedirectPrinters = false;
                client.AdvancedSettings8.RedirectClipboard = rdp.EnableClipboard;
                client.AdvancedSettings8.RedirectSmartCards = false;
                client.AdvancedSettings8.RedirectPorts = false;

                client.AdvancedSettings8.PublicMode = true;
                client.AdvancedSettings8.overallConnectionTimeout = 5;
                client.AdvancedSettings8.singleConnectionTimeout = 5;

                if (!string.IsNullOrEmpty(rdp.Password))
                {
                    var secured = (IMsTscNonScriptable)client.GetOcx();
                    secured.ClearTextPassword = rdp.Password;
                }

                client.Connect();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[RemoteManager] RDP connect error: {ex.Message}");
                SetRdpOverlayStatus(tab, $"Connection failed: {ex.Message}", true);
            }
        }

        private static string GetDisconnectReason(int code)
        {
            switch (code)
            {
                case 0: return "No information available";
                case 1: return "Local disconnection";
                case 2: return "Remote disconnection by user";
                case 3: return "Remote disconnection by server";
                case 260: return "DNS name lookup failure";
                case 264: return "Connection timed out";
                case 516: return "Could not connect - check IP address";
                case 520: return "Host not found";
                case 772: return "Windows Sockets send failed";
                case 1028: return "Windows Sockets receive failed";
                case 1288: return "DNS lookup failed";
                case 1540: return "Host name lookup failed";
                case 1796: return "Connection timed out";
                case 2052: return "Bad IP address";
                case 2308: return "Connection lost";
                case 2055: return "Login failed - bad username or password";
                case 2825: return "Remote computer requires NLA";
                case 3335: return "Account locked out";
                case 3847: return "Password has expired";
                default: return "Disconnected";
            }
        }

        private void DisconnectRdp(TabEntry tab)
        {
            if (tab.RdpClient != null)
            {
                try
                {
                    if (tab.RdpClient.Connected != 0)
                        tab.RdpClient.Disconnect();
                }
                catch { }

                try
                {
                    tab.RdpHost.Child = null;
                    tab.RdpClient.Dispose();
                }
                catch { }

                tab.RdpClient = null;
            }
        }

        #endregion

        #region Refresh

        private void OnRefreshClicked(object sender, RoutedEventArgs e)
        {
            LoadDevicesAsync();
        }

        private void OnCloseAllTabs(object sender, RoutedEventArgs e)
        {
            var ids = _openTabs.Keys.ToList();
            foreach (var id in ids)
                CloseTab(id);
        }

        #endregion

        #region Settings

        private void OnAutoAcceptCertsChanged(object sender, RoutedEventArgs e)
        {
            _viewItemManager.AutoAcceptCerts = autoAcceptCertsCheckBox.IsChecked == true;
            _viewItemManager.Save();
        }

        private void OnAutoLoginChanged(object sender, RoutedEventArgs e)
        {
            _viewItemManager.AutoLogin = autoLoginCheckBox.IsChecked == true;
            _viewItemManager.Save();
        }

        #endregion

        #region UI Helpers

        private string PromptInput(string title, string label, string defaultValue)
        {
            var win = new Window
            {
                Title = title,
                Width = 350,
                SizeToContent = SizeToContent.Height,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize,
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF1C2326")),
                Owner = Window.GetWindow(this)
            };

            var stack = new StackPanel { Margin = new Thickness(16, 14, 16, 14) };

            stack.Children.Add(new TextBlock
            {
                Text = label,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFD0D0D0")),
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 6)
            });

            var textBox = new TextBox
            {
                Text = defaultValue ?? "",
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF2A2A2A")),
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFE6EDF3")),
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF444444")),
                CaretBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFE6EDF3")),
                Padding = new Thickness(6, 5, 6, 5),
                FontSize = 12,
            };
            textBox.SelectAll();
            stack.Children.Add(textBox);

            string result = null;

            var okBtn = CreateDialogButton("OK", FontAwesome5.EFontAwesomeIcon.Solid_Check);
            okBtn.Margin = new Thickness(8, 0, 0, 0);
            okBtn.Click += (s, e) =>
            {
                var val = textBox.Text?.Trim();
                if (!string.IsNullOrEmpty(val))
                {
                    result = val;
                    win.DialogResult = true;
                }
            };

            var cancelBtn = CreateDialogButton("Cancel", null);
            cancelBtn.Click += (s, e) => { win.DialogResult = false; };

            textBox.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    var val = textBox.Text?.Trim();
                    if (!string.IsNullOrEmpty(val))
                    {
                        result = val;
                        win.DialogResult = true;
                    }
                }
            };

            var btnPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 14, 0, 0)
            };
            btnPanel.Children.Add(cancelBtn);
            btnPanel.Children.Add(okBtn);
            stack.Children.Add(btnPanel);

            win.Content = stack;
            textBox.Focus();

            if (win.ShowDialog() == true)
                return result;
            return null;
        }

        private void SelectTreeNodeById(Guid id)
        {
            var node = FindNodeById(_rootNode, id);
            if (node == null) return;

            var tvi = GetTreeViewItem(node);
            if (tvi != null)
                tvi.IsSelected = true;
        }

        private void ClearTreeSelection()
        {
            var selected = deviceTree.SelectedItem as RemoteTreeNode;
            if (selected != null)
            {
                var tvi = GetTreeViewItem(selected);
                if (tvi != null)
                    tvi.IsSelected = false;
            }
        }

        private RemoteTreeNode FindNodeById(RemoteTreeNode parent, Guid id)
        {
            if (parent.Id == id) return parent;
            foreach (var child in parent.Children)
            {
                var found = FindNodeById(child, id);
                if (found != null) return found;
            }
            return null;
        }

        private void RefreshContextMenu(RemoteTreeNode node)
        {
            deviceTree.Dispatcher.BeginInvoke(new Action(() =>
            {
                var tvi = GetTreeViewItem(node);
                if (tvi != null)
                {
                    var rowBorder = FindChild<Border>(tvi, "RowBorder");
                    if (rowBorder != null)
                        rowBorder.ContextMenu = CreateContextMenuForNode(node);
                }
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private TreeViewItem GetTreeViewItem(RemoteTreeNode node)
        {
            // Walk the path from root to node
            var path = new List<RemoteTreeNode>();
            var current = node;
            while (current != null)
            {
                path.Insert(0, current);
                current = current.Parent;
            }

            ItemsControl container = deviceTree;
            foreach (var pathNode in path)
            {
                if (container == null) return null;
                var tvi = container.ItemContainerGenerator.ContainerFromItem(pathNode) as TreeViewItem;
                if (tvi == null)
                {
                    // Force generation
                    container.UpdateLayout();
                    tvi = container.ItemContainerGenerator.ContainerFromItem(pathNode) as TreeViewItem;
                }
                if (tvi == null) return null;
                container = tvi;
            }

            return container as TreeViewItem;
        }

        private ControlTemplate CreateDarkContextMenuTemplate()
        {
            var template = new ControlTemplate(typeof(ContextMenu));

            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.BackgroundProperty,
                new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF2A2A2A")));
            border.SetValue(Border.BorderBrushProperty,
                new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF444444")));
            border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(3));
            border.SetValue(Border.PaddingProperty, new Thickness(0, 4, 0, 4));

            var itemsPresenter = new FrameworkElementFactory(typeof(ItemsPresenter));
            border.AppendChild(itemsPresenter);

            template.VisualTree = border;
            return template;
        }

        private ControlTemplate CreateDarkMenuItemTemplate()
        {
            var template = new ControlTemplate(typeof(MenuItem));

            var border = new FrameworkElementFactory(typeof(Border));
            border.Name = "bd";
            border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            border.SetValue(Border.PaddingProperty, new Thickness(12, 6, 20, 6));

            var content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(ContentPresenter.ContentSourceProperty, "Header");
            content.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(content);

            template.VisualTree = border;

            var hover = new Trigger { Property = MenuItem.IsHighlightedProperty, Value = true };
            hover.Setters.Add(new Setter(Border.BackgroundProperty,
                new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF363636")), "bd"));
            template.Triggers.Add(hover);

            return template;
        }

        private Button CreateDialogButton(string text, FontAwesome5.EFontAwesomeIcon? icon)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal };

            if (icon.HasValue)
            {
                panel.Children.Add(new FontAwesome5.ImageAwesome
                {
                    Icon = icon.Value,
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF2196F3")),
                    Width = 10, Height = 10,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 6, 0)
                });
            }

            panel.Children.Add(new TextBlock
            {
                Text = text,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFD0D0D0")),
                VerticalAlignment = VerticalAlignment.Center
            });

            var btn = new Button
            {
                Content = panel,
                Cursor = Cursors.Hand,
                Template = CreateSmallButtonTemplate(),
            };
            return btn;
        }

        private ControlTemplate CreateSmallButtonTemplate()
        {
            var template = new ControlTemplate(typeof(Button));
            var border = new FrameworkElementFactory(typeof(Border));
            border.Name = "bd";
            border.SetValue(Border.BackgroundProperty,
                new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF2A2A2A")));
            border.SetValue(Border.BorderBrushProperty,
                new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF444444")));
            border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(3));
            border.SetValue(Border.PaddingProperty, new Thickness(12, 4, 12, 4));

            var content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            content.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(content);

            template.VisualTree = border;

            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Border.BackgroundProperty,
                new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF363636")), "bd"));
            hover.Setters.Add(new Setter(Border.BorderBrushProperty,
                new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF555555")), "bd"));
            template.Triggers.Add(hover);

            var pressed = new Trigger { Property = UIElement.IsStylusCapturedProperty, Value = true };
            pressed.Setters.Add(new Setter(Border.BackgroundProperty,
                new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF404040")), "bd"));
            template.Triggers.Add(pressed);

            return template;
        }

        private static TreeViewItem FindTreeViewItem(DependencyObject source)
        {
            while (source != null && !(source is TreeViewItem))
                source = VisualTreeHelper.GetParent(source);
            return source as TreeViewItem;
        }

        private static T FindChild<T>(DependencyObject parent, string name) where T : FrameworkElement
        {
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T t && t.Name == name)
                    return t;
                var result = FindChild<T>(child, name);
                if (result != null)
                    return result;
            }
            return null;
        }

        #endregion

        #region Types

        private enum TabType { WebView, Rdp }

        private class TabEntry
        {
            public Guid Id { get; set; }
            public string Name { get; set; }
            public TabType TabType { get; set; }
            public WebView2 WebView { get; set; }
            public System.Windows.Forms.Integration.WindowsFormsHost RdpHost { get; set; }
            public Grid RdpContainer { get; set; }
            public Border RdpOverlay { get; set; }
            public AxMsRdpClient8NotSafeForScripting RdpClient { get; set; }
            public RdpConnectionInfo RdpInfo { get; set; }
            public HardwareDeviceInfo WebInfo { get; set; }
            public bool AuthAttempted { get; set; }
            public Border TabButton { get; set; }
        }

        #endregion

        #region Natural Sort

        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
        private static extern int StrCmpLogicalW(string x, string y);

        private static readonly NaturalSortComparer NaturalSort = new NaturalSortComparer();

        private class NaturalSortComparer : IComparer<string>
        {
            public int Compare(string x, string y) => StrCmpLogicalW(x ?? "", y ?? "");
        }

        #endregion
    }
}
