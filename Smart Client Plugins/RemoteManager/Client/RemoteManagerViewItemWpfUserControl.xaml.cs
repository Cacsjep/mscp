using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
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
    public partial class RemoteManagerViewItemWpfUserControl : ViewItemWpfUserControl
    {
        private readonly RemoteManagerViewItemManager _viewItemManager;
        private List<HardwareDeviceInfo> _allDevices = new List<HardwareDeviceInfo>();
        private List<HardwareDeviceInfo> _userEntries = new List<HardwareDeviceInfo>();
        private List<RdpConnectionInfo> _rdpEntries = new List<RdpConnectionInfo>();
        private TagOrganization _tagOrg = new TagOrganization();
        private readonly Dictionary<Guid, TabEntry> _openTabs = new Dictionary<Guid, TabEntry>();
        private Guid? _activeTabId;
        private bool _passwordVisible;
        private string _selectedUsername;
        private string _selectedPassword;
        private string _selectedDeviceName;
        private readonly string _webView2UserDataFolder;
        private CoreWebView2Environment _webView2Environment;
        private int _passwordReadGeneration;

        // Tag filter state
        private readonly HashSet<string> _activeTagFilters = new HashSet<string>();
        private bool _initialized;
        private bool _rebuilding;

        public RemoteManagerViewItemWpfUserControl(RemoteManagerViewItemManager viewItemManager)
        {
            _viewItemManager = viewItemManager;
            InitializeComponent();

            _webView2UserDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MSCPlugins", "RemoteManager", "WebView2Data");
        }

        public override void Init()
        {
            _initialized = true;
            autoAcceptCertsCheckBox.IsChecked = _viewItemManager.AutoAcceptCerts;
            _userEntries = _viewItemManager.GetUserEntries();
            _rdpEntries = _viewItemManager.GetRdpEntries();
            _tagOrg = _viewItemManager.GetTagOrganization();
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
                CleanupStaleTags();
                RebuildList();
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

        private void CleanupStaleTags()
        {
            var validKeys = new HashSet<string>();
            foreach (var d in _allDevices)
                validKeys.Add($"hw:{d.HardwareId}");
            foreach (var e in _userEntries)
                validKeys.Add($"web:{e.HardwareId}");
            foreach (var r in _rdpEntries)
                validKeys.Add($"rdp:{r.Id}");

            var staleKeys = _tagOrg.ItemTags.Keys
                .Where(k => !validKeys.Contains(k))
                .ToList();

            if (staleKeys.Count > 0)
            {
                foreach (var k in staleKeys)
                    _tagOrg.ItemTags.Remove(k);
                _viewItemManager.SetTagOrganization(_tagOrg);
                _viewItemManager.Save();
            }
        }

        #endregion

        #region Tag System

        private string GetItemKey(object item)
        {
            if (item is HardwareDeviceInfo hw)
                return hw.IsUserDefined ? $"web:{hw.HardwareId}" : $"hw:{hw.HardwareId}";
            if (item is RdpConnectionInfo rdp)
                return $"rdp:{rdp.Id}";
            return null;
        }

        /// <summary>
        /// Returns all tags for an item: factory tags (type, server) + custom tags.
        /// </summary>
        private List<string> GetAllTags(object item)
        {
            var tags = new List<string>();

            if (item is HardwareDeviceInfo hw)
            {
                if (hw.IsUserDefined)
                {
                    tags.Add("Website");
                }
                else
                {
                    tags.Add("Hardware Web Interface");
                    if (!string.IsNullOrEmpty(hw.RecordingServerName))
                        tags.Add(hw.RecordingServerName);
                }
            }
            else if (item is RdpConnectionInfo)
            {
                tags.Add("RDP");
            }

            // Add custom tags
            var key = GetItemKey(item);
            if (key != null && _tagOrg.ItemTags.TryGetValue(key, out var customTags))
            {
                tags.AddRange(customTags);
            }

            return tags;
        }

        /// <summary>
        /// Returns only custom (user-created) tags for an item.
        /// </summary>
        private List<string> GetCustomTags(object item)
        {
            var key = GetItemKey(item);
            if (key != null && _tagOrg.ItemTags.TryGetValue(key, out var tags))
                return new List<string>(tags);
            return new List<string>();
        }

        private void SetCustomTags(object item, List<string> tags)
        {
            var key = GetItemKey(item);
            if (key == null) return;

            if (tags == null || tags.Count == 0)
                _tagOrg.ItemTags.Remove(key);
            else
                _tagOrg.ItemTags[key] = tags;
        }

        /// <summary>
        /// Collects all distinct tags across all items (factory + custom).
        /// </summary>
        private List<string> CollectAllTags()
        {
            var tagSet = new HashSet<string>();

            foreach (var d in _allDevices)
                foreach (var t in GetAllTags(d))
                    tagSet.Add(t);
            foreach (var e in _userEntries)
                foreach (var t in GetAllTags(e))
                    tagSet.Add(t);
            foreach (var r in _rdpEntries)
                foreach (var t in GetAllTags(r))
                    tagSet.Add(t);

            var result = tagSet.ToList();
            result.Sort(NaturalSort);
            return result;
        }

        #endregion

        #region List Building

        private void RebuildList()
        {
            _rebuilding = true;
            try
            {
                deviceList.Items.Clear();
                RebuildTagFilterBar();

                var query = searchBox.Text?.Trim();
                var isSearching = !string.IsNullOrWhiteSpace(query);

                var items = new List<object>();

                foreach (var d in _allDevices)
                {
                    if (isSearching && !MatchesSearch(d.Name, d.IpAddress, query)) continue;
                    if (!MatchesTagFilter(d)) continue;
                    items.Add(d);
                }
                foreach (var e in _userEntries)
                {
                    if (isSearching && !MatchesSearch(e.Name, e.Address, query)) continue;
                    if (!MatchesTagFilter(e)) continue;
                    items.Add(e);
                }
                foreach (var r in _rdpEntries)
                {
                    if (isSearching && !MatchesSearch(r.Name, r.Host, query)) continue;
                    if (!MatchesTagFilter(r)) continue;
                    items.Add(r);
                }

                items = SortItems(items);

                foreach (var item in items)
                    deviceList.Items.Add(CreateListItem(item));
            }
            finally
            {
                _rebuilding = false;
            }
        }

        private ListBoxItem CreateListItem(object item)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal };

            // Icon
            FontAwesome5.EFontAwesomeIcon icon;
            string iconColor;
            string name;
            string address;

            if (item is HardwareDeviceInfo hw)
            {
                icon = FontAwesome5.EFontAwesomeIcon.Solid_Globe;
                iconColor = "#FF8B949E";
                name = hw.Name;
                address = hw.IsUserDefined ? hw.Address : hw.IpAddress;
            }
            else if (item is RdpConnectionInfo rdp)
            {
                icon = FontAwesome5.EFontAwesomeIcon.Solid_Desktop;
                iconColor = "#FF42A5F5";
                name = rdp.Name;
                address = rdp.DisplayAddress;
            }
            else return new ListBoxItem();

            var fa = new FontAwesome5.ImageAwesome
            {
                Icon = icon,
                Width = 12,
                Height = 12,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(iconColor)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 8, 0)
            };
            panel.Children.Add(fa);

            var nameText = new TextBlock
            {
                Text = name ?? "",
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFE6EDF3")),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            panel.Children.Add(nameText);

            // Build tooltip with address + tags
            var allTags = GetAllTags(item);
            var tooltipPanel = new StackPanel();
            tooltipPanel.Children.Add(new TextBlock
            {
                Text = address ?? "",
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFE6EDF3")),
                FontSize = 12,
            });

            if (allTags.Count > 0)
            {
                var tagWrap = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
                foreach (var tag in allTags)
                    tagWrap.Children.Add(CreateItemTagChip(tag));
                tooltipPanel.Children.Add(tagWrap);
            }

            var listItem = new ListBoxItem
            {
                Content = panel,
                Tag = item,
                ToolTip = tooltipPanel,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
            };

            // Open on left-click only
            listItem.PreviewMouseLeftButtonUp += OnListItemLeftClick;

            // Context menu
            if (item is HardwareDeviceInfo hwItem && hwItem.IsUserDefined)
            {
                listItem.ContextMenu = CreateDarkContextMenu(
                    ("Edit", (s, e) => EditWebViewEntry(hwItem)),
                    ("Remove", (s, e) => RemoveWebViewEntry(hwItem)));
            }
            else if (item is HardwareDeviceInfo hwCamera)
            {
                listItem.ContextMenu = CreateDarkContextMenu(
                    ("Edit Tags", (s, e) => EditItemTags(hwCamera)));
            }
            else if (item is RdpConnectionInfo rdpItem)
            {
                listItem.ContextMenu = CreateDarkContextMenu(
                    ("Edit", (s, e) => EditRdpEntry(rdpItem)),
                    ("Remove", (s, e) => RemoveRdpEntry(rdpItem)));
            }

            return listItem;
        }

        private Border CreateItemTagChip(string tag)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal };

            panel.Children.Add(new FontAwesome5.ImageAwesome
            {
                Icon = FontAwesome5.EFontAwesomeIcon.Solid_Tag,
                Width = 7,
                Height = 7,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF8B949E")),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 4, 0),
            });

            panel.Children.Add(new TextBlock
            {
                Text = tag,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFB0B0B0")),
                FontSize = 10,
                VerticalAlignment = VerticalAlignment.Center,
            });

            return new Border
            {
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF2E3338")),
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF3A3F44")),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 3, 8, 3),
                Margin = new Thickness(2, 2, 2, 2),
                Child = panel,
            };
        }

        private bool MatchesSearch(string name, string secondary, string query)
        {
            return (name != null && name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) ||
                   (secondary != null && secondary.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private bool MatchesTagFilter(object item)
        {
            if (_activeTagFilters.Count == 0) return true;

            var tags = GetAllTags(item);
            // AND logic: item must have ALL selected filter tags
            foreach (var filter in _activeTagFilters)
            {
                if (!tags.Contains(filter))
                    return false;
            }
            return true;
        }

        private List<object> SortItems(List<object> items)
        {
            return items.OrderBy(i => GetName(i), NaturalSort).ToList();
        }

        private static string GetName(object item)
        {
            if (item is HardwareDeviceInfo hw) return hw.Name ?? "";
            if (item is RdpConnectionInfo rdp) return rdp.Name ?? "";
            return "";
        }

        #endregion

        #region Tag Filter Bar

        private void RebuildTagFilterBar()
        {
            tagFilterPanel.Children.Clear();
            var allTags = CollectAllTags();

            foreach (var tag in allTags)
            {
                var isActive = _activeTagFilters.Contains(tag);
                var chip = CreateFilterTagChip(tag, isActive);
                tagFilterPanel.Children.Add(chip);
            }

        }

        private Border CreateFilterTagChip(string tag, bool active)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal };

            // Icon: + for unselected, checkmark for selected
            var icon = new FontAwesome5.ImageAwesome
            {
                Icon = active
                    ? FontAwesome5.EFontAwesomeIcon.Solid_Check
                    : FontAwesome5.EFontAwesomeIcon.Solid_Plus,
                Width = 8,
                Height = 8,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(
                    active ? "#FF1C2326" : "#FF8B949E")),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 5, 0),
            };
            panel.Children.Add(icon);

            var text = new TextBlock
            {
                Text = tag,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(
                    active ? "#FF1C2326" : "#FFB0B0B0")),
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
            };
            panel.Children.Add(text);

            var bgColor = active ? "#FFFFC107" : "#FF2E3338";
            var border = new Border
            {
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(bgColor)),
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(bgColor)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(10, 4, 10, 4),
                Margin = new Thickness(0, 0, 6, 6),
                Cursor = Cursors.Hand,
                Tag = tag,
                Child = panel,
                SnapsToDevicePixels = true,
            };

            border.MouseLeftButtonDown += OnTagFilterClicked;

            // Hover effect
            border.MouseEnter += (s, e) =>
            {
                if (!_activeTagFilters.Contains(tag))
                {
                    var hoverBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF3A3F44"));
                    border.Background = hoverBrush;
                    border.BorderBrush = hoverBrush;
                }
            };
            border.MouseLeave += (s, e) =>
            {
                if (!_activeTagFilters.Contains(tag))
                {
                    var normalBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF2E3338"));
                    border.Background = normalBrush;
                    border.BorderBrush = normalBrush;
                }
            };

            return border;
        }

        private void OnTagFilterClicked(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border border && border.Tag is string tag)
            {
                if (_activeTagFilters.Contains(tag))
                    _activeTagFilters.Remove(tag);
                else
                    _activeTagFilters.Add(tag);

                RebuildList();
            }
        }


        #endregion

        #region Search

        private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_initialized) return;
            searchPlaceholder.Visibility = string.IsNullOrEmpty(searchBox.Text)
                ? Visibility.Visible : Visibility.Collapsed;
            RebuildList();
        }

        #endregion

        #region List Selection

        private void OnListItemLeftClick(object sender, MouseButtonEventArgs e)
        {
            if (!_initialized || _rebuilding) return;
            var listItem = sender as ListBoxItem;
            if (listItem?.Tag == null) return;

            if (listItem.Tag is HardwareDeviceInfo device)
            {
                OpenOrFocusWebViewTab(device);
                ReadPasswordOnDemand(device);
            }
            else if (listItem.Tag is RdpConnectionInfo rdp)
            {
                OpenOrFocusRdpTab(rdp);
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

            // Update credential bar
            _selectedDeviceName = device.Name;
            _selectedUsername = device.Username;
            _selectedPassword = device.Password;
            UpdateCredentialBar(device.Name, device.Username, device.Password, !device.IsUserDefined);
        }

        #endregion

        #region Tag Editing

        private void EditItemTags(object item)
        {
            var factoryTags = GetFactoryTags(item);
            var currentTags = GetCustomTags(item);
            var allExistingTags = CollectAllCustomTags();
            var name = GetName(item);

            var result = PromptEditTags(name, factoryTags, currentTags, allExistingTags);
            if (result == null) return;

            SetCustomTags(item, result);
            _viewItemManager.SetTagOrganization(_tagOrg);
            _viewItemManager.Save();
            RebuildList();
        }

        /// <summary>
        /// Returns only factory (built-in, non-removable) tags for an item.
        /// </summary>
        private List<string> GetFactoryTags(object item)
        {
            var tags = new List<string>();
            if (item is HardwareDeviceInfo hw)
            {
                if (hw.IsUserDefined)
                {
                    tags.Add("Website");
                }
                else
                {
                    tags.Add("Hardware Web Interface");
                    if (!string.IsNullOrEmpty(hw.RecordingServerName))
                        tags.Add(hw.RecordingServerName);
                }
            }
            else if (item is RdpConnectionInfo)
            {
                tags.Add("RDP");
            }
            return tags;
        }

        /// <summary>
        /// Collects all distinct custom tags across all items.
        /// </summary>
        private List<string> CollectAllCustomTags()
        {
            var tagSet = new HashSet<string>();
            foreach (var tags in _tagOrg.ItemTags.Values)
                foreach (var t in tags)
                    tagSet.Add(t);
            var result = tagSet.ToList();
            result.Sort(NaturalSort);
            return result;
        }

        private List<string> PromptEditTags(string itemName, List<string> factoryTags, List<string> currentTags, List<string> existingTags)
        {
            var win = new Window
            {
                Title = $"Edit Tags - {itemName}",
                Width = 400,
                SizeToContent = SizeToContent.Height,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize,
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF1C2326")),
                Owner = Window.GetWindow(this)
            };

            var selectedTags = new HashSet<string>(currentTags);

            var stack = new StackPanel { Margin = new Thickness(16, 14, 16, 14) };

            // Factory tags (read-only display)
            if (factoryTags.Count > 0)
            {
                stack.Children.Add(new TextBlock
                {
                    Text = "Default tags:",
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF8B949E")),
                    FontSize = 11,
                    Margin = new Thickness(0, 0, 0, 4)
                });

                var factoryPanel = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
                foreach (var tag in factoryTags)
                {
                    var chipPanel = new StackPanel { Orientation = Orientation.Horizontal };
                    chipPanel.Children.Add(new FontAwesome5.ImageAwesome
                    {
                        Icon = FontAwesome5.EFontAwesomeIcon.Solid_Lock,
                        Width = 7,
                        Height = 7,
                        Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF8B949E")),
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(0, 0, 5, 0),
                    });
                    chipPanel.Children.Add(new TextBlock
                    {
                        Text = tag,
                        Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFB0B0B0")),
                        FontSize = 11,
                    });
                    var chip = new Border
                    {
                        Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF2E3338")),
                        BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF3A3F44")),
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(4),
                        Padding = new Thickness(10, 5, 10, 5),
                        Margin = new Thickness(0, 0, 6, 6),
                        Child = chipPanel,
                    };
                    factoryPanel.Children.Add(chip);
                }
                stack.Children.Add(factoryPanel);
            }

            // Custom tags label
            stack.Children.Add(new TextBlock
            {
                Text = "Custom tags:",
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFD0D0D0")),
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 6)
            });

            // Tag chips panel (toggleable existing tags)
            var tagPanel = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
            Action rebuildTagPanel = null;
            rebuildTagPanel = () =>
            {
                tagPanel.Children.Clear();
                var allTags = new HashSet<string>(existingTags);
                foreach (var t in selectedTags) allTags.Add(t);
                var sorted = allTags.ToList();
                sorted.Sort(NaturalSort);

                foreach (var tag in sorted)
                {
                    var isSelected = selectedTags.Contains(tag);

                    var chipPanel = new StackPanel { Orientation = Orientation.Horizontal };

                    var chipIcon = new FontAwesome5.ImageAwesome
                    {
                        Icon = isSelected
                            ? FontAwesome5.EFontAwesomeIcon.Solid_Check
                            : FontAwesome5.EFontAwesomeIcon.Solid_Plus,
                        Width = 8,
                        Height = 8,
                        Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(
                            isSelected ? "#FF1C2326" : "#FF8B949E")),
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(0, 0, 5, 0),
                    };
                    chipPanel.Children.Add(chipIcon);

                    var chipText = new TextBlock
                    {
                        Text = tag,
                        Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(
                            isSelected ? "#FF1C2326" : "#FFB0B0B0")),
                        FontSize = 11,
                    };
                    chipPanel.Children.Add(chipText);

                    var chipBorder = new Border
                    {
                        Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(
                            isSelected ? "#FFFFC107" : "#FF2E3338")),
                        BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(
                            isSelected ? "#FFFFC107" : "#FF3A3F44")),
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(4),
                        Padding = new Thickness(10, 5, 10, 5),
                        Margin = new Thickness(0, 0, 6, 6),
                        Cursor = Cursors.Hand,
                        Tag = tag,
                        Child = chipPanel,
                    };

                    // Left-click: toggle selection
                    chipBorder.MouseLeftButtonDown += (s, e) =>
                    {
                        var t = chipBorder.Tag as string;
                        if (selectedTags.Contains(t))
                            selectedTags.Remove(t);
                        else
                            selectedTags.Add(t);
                        rebuildTagPanel();
                    };

                    // Right-click: remove tag entirely
                    chipBorder.MouseRightButtonDown += (s, e) =>
                    {
                        var t = chipBorder.Tag as string;
                        selectedTags.Remove(t);
                        existingTags.Remove(t);
                        rebuildTagPanel();
                    };

                    tagPanel.Children.Add(chipBorder);
                }
            };
            rebuildTagPanel();
            stack.Children.Add(tagPanel);

            // New tag input
            stack.Children.Add(new TextBlock
            {
                Text = "Add new tag:",
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFD0D0D0")),
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 4)
            });

            var inputGrid = new Grid();
            inputGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            inputGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });

            var newTagBox = new TextBox
            {
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF2A2A2A")),
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFE6EDF3")),
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF444444")),
                CaretBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFE6EDF3")),
                Padding = new Thickness(6, 5, 6, 5),
                FontSize = 12,
            };
            Grid.SetColumn(newTagBox, 0);
            inputGrid.Children.Add(newTagBox);

            var addTagBtn = CreateDialogButton("Add", FontAwesome5.EFontAwesomeIcon.Solid_Plus);
            addTagBtn.Margin = new Thickness(6, 0, 0, 0);
            Grid.SetColumn(addTagBtn, 1);

            Action addNewTag = () =>
            {
                var newTag = newTagBox.Text?.Trim();
                if (!string.IsNullOrEmpty(newTag) && !selectedTags.Contains(newTag))
                {
                    selectedTags.Add(newTag);
                    if (!existingTags.Contains(newTag))
                        existingTags.Add(newTag);
                    newTagBox.Text = "";
                    rebuildTagPanel();
                }
            };
            addTagBtn.Click += (s, e) => addNewTag();
            newTagBox.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter) addNewTag();
            };
            inputGrid.Children.Add(addTagBtn);

            stack.Children.Add(inputGrid);

            // Buttons
            List<string> result = null;

            var okBtn = CreateDialogButton("OK", FontAwesome5.EFontAwesomeIcon.Solid_Check);
            okBtn.Margin = new Thickness(8, 0, 0, 0);
            okBtn.Click += (s, e) =>
            {
                result = selectedTags.ToList();
                win.DialogResult = true;
            };

            var cancelBtn = CreateDialogButton("Cancel", null);
            cancelBtn.Click += (s, e) => { win.DialogResult = false; };

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
            newTagBox.Focus();

            if (win.ShowDialog() == true)
                return result;
            return null;
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

        #region Add Entries

        private void OnAddButtonClicked(object sender, RoutedEventArgs e)
        {
            var popup = new System.Windows.Controls.Primitives.Popup
            {
                PlacementTarget = addButton,
                Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom,
                StaysOpen = false,
                AllowsTransparency = true,
            };

            var stack = new StackPanel { Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF2A2A2A")) };

            var webBtn = CreateAddMenuButton("Web View", FontAwesome5.EFontAwesomeIcon.Solid_Globe, "#FF8B949E");
            webBtn.Click += (s, ev) => { popup.IsOpen = false; OnAddWebViewEntry(s, ev); };

            var rdpBtn = CreateAddMenuButton("RDP Connection", FontAwesome5.EFontAwesomeIcon.Solid_Desktop, "#FF42A5F5");
            rdpBtn.Click += (s, ev) => { popup.IsOpen = false; OnAddRdpEntry(s, ev); };

            stack.Children.Add(webBtn);
            stack.Children.Add(rdpBtn);

            var border = new Border
            {
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF2A2A2A")),
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF444444")),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Child = stack
            };

            popup.Child = border;
            popup.IsOpen = true;
        }

        private Button CreateAddMenuButton(string text, FontAwesome5.EFontAwesomeIcon icon, string iconColor)
        {
            var fa = new FontAwesome5.ImageAwesome
            {
                Icon = icon,
                Width = 10,
                Height = 10,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(iconColor)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0)
            };

            var tb = new TextBlock
            {
                Text = text,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFD0D0D0")),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center
            };

            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            panel.Children.Add(fa);
            panel.Children.Add(tb);

            var btn = new Button
            {
                Content = panel,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(12, 6, 16, 6),
                Cursor = Cursors.Hand,
                HorizontalContentAlignment = HorizontalAlignment.Left,
            };

            btn.Template = CreateMenuButtonTemplate();
            return btn;
        }

        private ControlTemplate CreateMenuButtonTemplate()
        {
            var template = new ControlTemplate(typeof(Button));
            var borderFactory = new FrameworkElementFactory(typeof(Border));
            borderFactory.Name = "bd";
            borderFactory.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            borderFactory.SetValue(Border.PaddingProperty, new Thickness(12, 6, 16, 6));

            var contentFactory = new FrameworkElementFactory(typeof(ContentPresenter));
            contentFactory.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Left);
            contentFactory.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            borderFactory.AppendChild(contentFactory);

            template.VisualTree = borderFactory;

            var hoverTrigger = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hoverTrigger.Setters.Add(new Setter(Border.BackgroundProperty,
                new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF363636")), "bd"));
            template.Triggers.Add(hoverTrigger);

            return template;
        }

        private void OnAddWebViewEntry(object sender, RoutedEventArgs e)
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

                // Save custom tags from dialog if present
                if (dialog.EntryTags != null && dialog.EntryTags.Count > 0)
                {
                    SetCustomTags(entry, dialog.EntryTags);
                    _viewItemManager.SetTagOrganization(_tagOrg);
                }

                _viewItemManager.Save();
                RebuildList();
            }
        }

        private void OnAddRdpEntry(object sender, RoutedEventArgs e)
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

                // Save custom tags from dialog if present
                if (dialog.EntryTags != null && dialog.EntryTags.Count > 0)
                {
                    SetCustomTags(entry, dialog.EntryTags);
                    _viewItemManager.SetTagOrganization(_tagOrg);
                }

                _viewItemManager.Save();
                RebuildList();
            }
        }

        #endregion

        #region Edit / Remove Entries

        private void EditWebViewEntry(HardwareDeviceInfo entry)
        {
            var dialog = new AddWebViewEntryWindow(entry, GetCustomTags(entry));
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

                SetCustomTags(entry, dialog.EntryTags);
                _viewItemManager.SetTagOrganization(_tagOrg);
                _viewItemManager.Save();
                RebuildList();
            }
        }

        private void EditRdpEntry(RdpConnectionInfo entry)
        {
            var dialog = new AddRdpEntryWindow(entry, GetCustomTags(entry));
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

                SetCustomTags(entry, dialog.EntryTags);
                _viewItemManager.SetTagOrganization(_tagOrg);
                _viewItemManager.Save();
                RebuildList();
            }
        }

        private void RemoveWebViewEntry(HardwareDeviceInfo entry)
        {
            _userEntries.Remove(entry);
            _tagOrg.ItemTags.Remove($"web:{entry.HardwareId}");
            _viewItemManager.SetUserEntries(_userEntries);
            _viewItemManager.SetTagOrganization(_tagOrg);
            _viewItemManager.Save();

            if (_openTabs.ContainsKey(entry.HardwareId))
                CloseTab(entry.HardwareId);

            RebuildList();
        }

        private void RemoveRdpEntry(RdpConnectionInfo entry)
        {
            _rdpEntries.Remove(entry);
            _tagOrg.ItemTags.Remove($"rdp:{entry.Id}");
            _viewItemManager.SetRdpEntries(_rdpEntries);
            _viewItemManager.SetTagOrganization(_tagOrg);
            _viewItemManager.Save();

            if (_openTabs.ContainsKey(entry.Id))
                CloseTab(entry.Id);

            RebuildList();
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

        #endregion

        #region UI Helpers

        private ContextMenu CreateDarkContextMenu(params (string text, RoutedEventHandler handler)[] items)
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

            foreach (var (text, handler) in items)
            {
                var mi = new MenuItem
                {
                    Header = text,
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFD0D0D0")),
                    Template = CreateDarkMenuItemTemplate(),
                };
                mi.Click += handler;
                menu.Items.Add(mi);
            }

            return menu;
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
