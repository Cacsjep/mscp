using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WebView.Models;
using WebView.Services;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using VideoOS.Platform.Client;

namespace WebView.Client
{
    public partial class WebViewViewItemWpfUserControl : ViewItemWpfUserControl
    {
        private readonly WebViewViewItemManager _viewItemManager;
        private List<HardwareDeviceInfo> _allDevices = new List<HardwareDeviceInfo>();
        private List<HardwareDeviceInfo> _userEntries = new List<HardwareDeviceInfo>();
        private readonly Dictionary<Guid, TabEntry> _openTabs = new Dictionary<Guid, TabEntry>();
        private Guid? _activeTabId;
        private bool _passwordVisible;
        private HardwareDeviceInfo _selectedDevice;
        private readonly string _webView2UserDataFolder;
        private CoreWebView2Environment _webView2Environment;
        private int _passwordReadGeneration;

        public WebViewViewItemWpfUserControl(WebViewViewItemManager viewItemManager)
        {
            _viewItemManager = viewItemManager;
            InitializeComponent();

            _webView2UserDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MSCPlugins", "WebView", "WebView2Data");
        }

        public override void Init()
        {
            autoAcceptCertsCheckBox.IsChecked = _viewItemManager.AutoAcceptCerts;
            _userEntries = _viewItemManager.GetUserEntries();
            LoadDevicesAsync();
        }

        public override void Close()
        {
            foreach (var tab in _openTabs.Values)
            {
                tab.TabButton.MouseLeftButtonDown -= OnTabClicked;
                if (tab.WebView.CoreWebView2 != null)
                    tab.WebView.CoreWebView2.ServerCertificateErrorDetected -= OnCertificateError;
                try { tab.WebView.Dispose(); } catch { }
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
                RebuildTree();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WebView] Failed to load devices: {ex.Message}");
            }
            finally
            {
                loadingOverlay.Visibility = Visibility.Collapsed;
            }
        }

        private void RebuildTree()
        {
            var query = searchBox.Text?.Trim();
            deviceTree.Items.Clear();

            // Filter hardware devices
            var devices = _allDevices.AsEnumerable();
            if (!string.IsNullOrWhiteSpace(query))
            {
                devices = devices.Where(d =>
                    (d.Name != null && d.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (d.IpAddress != null && d.IpAddress.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0));
            }

            // Hardware devices grouped by recording server
            var grouped = devices
                .GroupBy(d => d.RecordingServerName ?? "Unknown")
                .OrderBy(g => g.Key, NaturalSort);

            foreach (var group in grouped)
            {
                var serverNode = new TreeViewItem
                {
                    Header = CreateTreeHeader(group.Key, "#FF3399FF", FontAwesome5.EFontAwesomeIcon.Solid_Server),
                    IsExpanded = true,
                    Tag = null
                };

                foreach (var device in group.OrderBy(d => d.Name, NaturalSort))
                {
                    var deviceNode = new TreeViewItem
                    {
                        Header = CreateTreeHeader(device.Name, "#FFE6EDF3", FontAwesome5.EFontAwesomeIcon.Solid_Globe, "#FF8B949E"),
                        ToolTip = device.IpAddress,
                        Tag = device
                    };
                    serverNode.Items.Add(deviceNode);
                }

                deviceTree.Items.Add(serverNode);
            }

            // User Defined section
            var filteredUserEntries = _userEntries.AsEnumerable();
            if (!string.IsNullOrWhiteSpace(query))
            {
                filteredUserEntries = filteredUserEntries.Where(d =>
                    (d.Name != null && d.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (d.Address != null && d.Address.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0));
            }

            var userList = filteredUserEntries.ToList();
            if (userList.Count > 0 || string.IsNullOrWhiteSpace(query))
            {
                var userNode = new TreeViewItem
                {
                    Header = CreateTreeHeader("User Defined", "#FF8BC34A", FontAwesome5.EFontAwesomeIcon.Solid_Bookmark),
                    IsExpanded = true,
                    Tag = null
                };

                foreach (var entry in userList.OrderBy(d => d.Name, NaturalSort))
                {
                    var entryNode = new TreeViewItem
                    {
                        Header = CreateTreeHeader(entry.Name, "#FFE6EDF3", FontAwesome5.EFontAwesomeIcon.Solid_Globe, "#FF8B949E"),
                        ToolTip = entry.Address,
                        Tag = entry
                    };

                    // Right-click context menu to remove
                    var removeItem = new MenuItem { Header = "Remove" };
                    removeItem.Click += (s, e) => RemoveUserEntry(entry);
                    entryNode.ContextMenu = new ContextMenu();
                    entryNode.ContextMenu.Items.Add(removeItem);

                    userNode.Items.Add(entryNode);
                }

                deviceTree.Items.Add(userNode);
            }
        }

        private StackPanel CreateTreeHeader(string text, string colorHex, FontAwesome5.EFontAwesomeIcon icon, string iconColor = null)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            var fa = new FontAwesome5.ImageAwesome
            {
                Icon = icon,
                Width = 12,
                Height = 12,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(iconColor ?? colorHex)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0)
            };
            var tb = new TextBlock
            {
                Text = text ?? "",
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorHex)),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center
            };
            panel.Children.Add(fa);
            panel.Children.Add(tb);
            return panel;
        }

        #endregion

        #region Search

        private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
        {
            searchPlaceholder.Visibility = string.IsNullOrEmpty(searchBox.Text)
                ? Visibility.Visible : Visibility.Collapsed;
            if (_allDevices != null)
                RebuildTree();
        }

        #endregion

        #region Tree Selection

        private async void OnDeviceTreeSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            var treeItem = deviceTree.SelectedItem as TreeViewItem;
            if (treeItem?.Tag is HardwareDeviceInfo device)
            {
                _selectedDevice = device;
                OpenOrFocusTab(device);
                UpdateCredentialBar(device);

                // Read password on-demand for hardware devices only
                if (!device.IsUserDefined && device.Password == null && !string.IsNullOrEmpty(device.HardwarePath))
                {
                    var generation = ++_passwordReadGeneration;
                    try
                    {
                        var pwd = await Task.Run(() => DeviceDiscoveryService.ReadPassword(device.HardwarePath));
                        device.Password = pwd ?? "";
                        if (_passwordReadGeneration == generation && _selectedDevice == device)
                        {
                            _passwordVisible = false;
                            passwordText.Text = MaskPassword(device.Password);
                            togglePasswordButton.Content = "Show";
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[WebView] Password read failed: {ex.Message}");
                        device.Password = "";
                    }
                }
            }
        }

        #endregion

        #region Credential Bar

        private void UpdateCredentialBar(HardwareDeviceInfo device)
        {
            // Show credential bar if device has username or password
            bool hasCreds = !string.IsNullOrEmpty(device.Username) ||
                            !string.IsNullOrEmpty(device.Password) ||
                            !device.IsUserDefined;
            credentialBar.Visibility = hasCreds ? Visibility.Visible : Visibility.Collapsed;
            if (!hasCreds) return;

            deviceNameText.Text = device.Name ?? "";
            usernameText.Text = device.Username ?? "";
            _passwordVisible = false;
            passwordText.Text = MaskPassword(device.Password);
            togglePasswordButton.Content = "Show";
        }

        private string MaskPassword(string password)
        {
            if (string.IsNullOrEmpty(password)) return "";
            return new string('\u2022', password.Length);
        }

        private void OnCopyUsername(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(_selectedDevice?.Username))
            {
                try { Clipboard.SetText(_selectedDevice.Username); } catch { }
            }
        }

        private void OnCopyPassword(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(_selectedDevice?.Password))
            {
                try { Clipboard.SetText(_selectedDevice.Password); } catch { }
            }
        }

        private void OnTogglePassword(object sender, RoutedEventArgs e)
        {
            if (_selectedDevice == null) return;
            _passwordVisible = !_passwordVisible;
            passwordText.Text = _passwordVisible
                ? (_selectedDevice.Password ?? "")
                : MaskPassword(_selectedDevice.Password);
            togglePasswordButton.Content = _passwordVisible ? "Hide" : "Show";
        }

        #endregion

        #region Tab Management

        private void OpenOrFocusTab(HardwareDeviceInfo device)
        {
            if (_openTabs.ContainsKey(device.HardwareId))
            {
                ActivateTab(device.HardwareId);
                return;
            }

            // For user-defined entries, Address IS the URL
            var url = device.IsUserDefined ? device.Address : device.WebUrl;
            if (string.IsNullOrEmpty(url)) return;

            var webView = new WebView2();
            webView.Visibility = Visibility.Collapsed;

            var tab = new TabEntry
            {
                Device = device,
                WebView = webView,
                TabButton = CreateTabButton(device)
            };

            _openTabs[device.HardwareId] = tab;
            browserHost.Children.Add(webView);
            tabStrip.Children.Add(tab.TabButton);

            InitializeWebView(tab, url);
            ActivateTab(device.HardwareId);
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
                Debug.WriteLine($"[WebView] WebView2 init error: {ex.Message}");
            }
        }

        private void OnCertificateError(object sender, CoreWebView2ServerCertificateErrorDetectedEventArgs e)
        {
            e.Action = CoreWebView2ServerCertificateErrorAction.AlwaysAllow;
        }

        private void ActivateTab(Guid hardwareId)
        {
            foreach (var kvp in _openTabs)
            {
                kvp.Value.WebView.Visibility = Visibility.Collapsed;
                UpdateTabButtonStyle(kvp.Value.TabButton, false);
            }

            if (_openTabs.TryGetValue(hardwareId, out var active))
            {
                active.WebView.Visibility = Visibility.Visible;
                UpdateTabButtonStyle(active.TabButton, true);
                _activeTabId = hardwareId;
                _selectedDevice = active.Device;
                UpdateCredentialBar(active.Device);
                welcomePanel.Visibility = Visibility.Collapsed;
            }
        }

        private void CloseTab(Guid hardwareId)
        {
            if (!_openTabs.TryGetValue(hardwareId, out var tab)) return;

            browserHost.Children.Remove(tab.WebView);
            tabStrip.Children.Remove(tab.TabButton);

            tab.TabButton.MouseLeftButtonDown -= OnTabClicked;
            if (tab.WebView.CoreWebView2 != null)
                tab.WebView.CoreWebView2.ServerCertificateErrorDetected -= OnCertificateError;

            try { tab.WebView.Dispose(); } catch { }

            _openTabs.Remove(hardwareId);

            if (_activeTabId == hardwareId)
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

        private Border CreateTabButton(HardwareDeviceInfo device)
        {
            var closeBtn = new Button
            {
                Content = "\u00D7",
                Style = (Style)FindResource("TabCloseButton"),
                Tag = device.HardwareId
            };
            closeBtn.Click += OnTabCloseClicked;

            var name = device.Name ?? "";
            var header = new TextBlock
            {
                Text = name.Length > 25 ? name.Substring(0, 22) + "..." : name,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFE6EDF3")),
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0)
            };

            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            panel.Children.Add(header);
            panel.Children.Add(closeBtn);

            var border = new Border
            {
                Child = panel,
                Padding = new Thickness(10, 6, 6, 6),
                Margin = new Thickness(0, 0, 1, 0),
                Cursor = System.Windows.Input.Cursors.Hand,
                Tag = device.HardwareId,
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

        private void OnTabClicked(object sender, System.Windows.Input.MouseButtonEventArgs e)
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

        #region User Defined Entries

        private void OnAddUserEntry(object sender, RoutedEventArgs e)
        {
            var dialog = new AddUserEntryWindow();
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
                    HardwareId = WebViewViewItemManager.GenerateStableId(dialog.EntryUrl),
                    RecordingServerName = "User Defined",
                };

                _userEntries.Add(entry);
                _viewItemManager.SetUserEntries(_userEntries);
                _viewItemManager.Save();
                RebuildTree();
            }
        }

        private void RemoveUserEntry(HardwareDeviceInfo entry)
        {
            _userEntries.Remove(entry);
            _viewItemManager.SetUserEntries(_userEntries);
            _viewItemManager.Save();

            // Close tab if open
            if (_openTabs.ContainsKey(entry.HardwareId))
                CloseTab(entry.HardwareId);

            RebuildTree();
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

        private class TabEntry
        {
            public HardwareDeviceInfo Device { get; set; }
            public WebView2 WebView { get; set; }
            public Border TabButton { get; set; }
        }

        #region Natural Sort

        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
        private static extern int StrCmpLogicalW(string x, string y);

        private static readonly Comparison<string> NaturalCompare = (a, b) =>
            StrCmpLogicalW(a ?? "", b ?? "");

        private static readonly NaturalSortComparer NaturalSort = new NaturalSortComparer();

        private class NaturalSortComparer : IComparer<string>
        {
            public int Compare(string x, string y) => StrCmpLogicalW(x ?? "", y ?? "");
        }

        #endregion
    }
}
