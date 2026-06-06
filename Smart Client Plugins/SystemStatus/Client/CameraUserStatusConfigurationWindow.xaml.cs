using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SystemStatus.Background;

namespace SystemStatus.Client
{
    /// <summary>
    /// Split configuration window for the Folder &amp; Role view item: settings on the left, a live preview
    /// of the actual tile on the right. The preview re-renders on every change and on background-plugin
    /// updates, so the operator sees exactly what the tile will show before clicking OK.
    /// </summary>
    public partial class CameraUserStatusConfigurationWindow : Window
    {
        private readonly CameraUserStatusViewItemManager _manager;
        private readonly SystemStatusBackgroundPlugin _plugin;
        private bool _suspend = true;   // block preview churn until the initial load finishes

        public CameraUserStatusConfigurationWindow(CameraUserStatusViewItemManager manager)
        {
            _manager = manager;
            _plugin = SystemStatusBackgroundPlugin.Instance;
            InitializeComponent();

            LoadFromManager();
            WireEvents();

            _suspend = false;
            UpdateSelectionEnabled();
            UpdateDensityVisibility();
            UpdatePreview();

            if (_plugin != null) _plugin.StatusChanged += OnStatusChanged;
            Closed += (s, e) => { if (_plugin != null) _plugin.StatusChanged -= OnStatusChanged; };
        }

        // ── load / save ──────────────────────────────────────────────────────
        private void LoadFromManager()
        {
            var s = CameraUserStatusSettings.FromManager(_manager);

            chkIndividual.IsChecked = s.IndividualSelection;
            chkPrefix.IsChecked = s.ShowServerPrefix;
            cmbRender.SelectedIndex = s.Dashboard ? 1 : 0;
            cmbDisplay.SelectedIndex = DisplayIndex(s);
            chkFoldersFirst.IsChecked = s.FoldersFirst;
            chkSuffix.IsChecked = s.ShowSuffix;
            chkHighlight.IsChecked = s.HighlightOffline;
            cmbSort.SelectedIndex = s.SortByOffline ? 1 : 0;
            cmbDensity.SelectedIndex = s.Compact ? 1 : 0;

            sldTextSize.Value = s.TextSize;
            lblTextSize.Text = ((int)s.TextSize).ToString(CultureInfo.InvariantCulture);
            sldCardMin.Value = s.CardMinWidth;
            lblCardMin.Text = ((int)s.CardMinWidth).ToString(CultureInfo.InvariantCulture);
            txtCountColor.Text = _manager?.CountColor ?? "#FF2A8FE0";
            txtOfflineColor.Text = _manager?.OfflineColor ?? "#FFE0902A";
            UpdateSwatch(countSwatch, txtCountColor.Text);
            UpdateSwatch(offlineSwatch, txtOfflineColor.Text);

            PopulateFolders(s.SelectedFolders);
            PopulateRoles(s.SelectedRoles);
        }

        private void OnOk(object sender, RoutedEventArgs e)
        {
            _manager.IndividualSelection = Bool(chkIndividual);
            _manager.ShowServerPrefix = Bool(chkPrefix);
            _manager.SelectedFolders = CameraUserStatusSettings.JoinSet(CheckedLabels(folderPanel));
            _manager.SelectedRoles = CameraUserStatusSettings.JoinSet(CheckedLabels(rolePanel));
            _manager.DisplayMode = DisplayModeFromIndex(cmbDisplay.SelectedIndex);
            _manager.RenderMode = cmbRender.SelectedIndex == 1 ? "Dashboard" : "List";
            _manager.FoldersFirst = Bool(chkFoldersFirst);
            _manager.ShowSuffix = Bool(chkSuffix);
            _manager.HighlightOffline = Bool(chkHighlight);
            _manager.SortBy = cmbSort.SelectedIndex == 1 ? "Offline" : "Name";
            _manager.Density = cmbDensity.SelectedIndex == 1 ? "Compact" : "Comfortable";
            _manager.TextSize = ((int)Math.Round(sldTextSize.Value)).ToString(CultureInfo.InvariantCulture);
            _manager.CountColor = NormalizeColor(txtCountColor.Text, "#FF2A8FE0");
            _manager.OfflineColor = NormalizeColor(txtOfflineColor.Text, "#FFE0902A");
            _manager.CardMinWidth = ((int)Math.Round(sldCardMin.Value)).ToString(CultureInfo.InvariantCulture);

            DialogResult = true;
            Close();
        }

        private void OnCancel(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        // ── selection lists ──────────────────────────────────────────────────
        private IEnumerable<string> AvailableFolders()
        {
            if (_plugin == null) return Enumerable.Empty<string>();
            return _plugin.GetFolderCameraStatus(chkPrefix.IsChecked == true).Select(r => r.Folder);
        }

        private IEnumerable<string> AvailableRoles()
        {
            if (_plugin == null) return Enumerable.Empty<string>();
            return _plugin.GetRoleUserStatus().Select(r => r.Role);
        }

        private void PopulateFolders(HashSet<string> checked_)
        {
            folderPanel.Children.Clear();
            foreach (var label in AvailableFolders())
                folderPanel.Children.Add(MakeCheck(label, checked_.Contains(label)));
            if (folderPanel.Children.Count == 0)
                folderPanel.Children.Add(EmptyNote("(no folders)"));
        }

        private void PopulateRoles(HashSet<string> checked_)
        {
            rolePanel.Children.Clear();
            foreach (var label in AvailableRoles())
                rolePanel.Children.Add(MakeCheck(label, checked_.Contains(label)));
            if (rolePanel.Children.Count == 0)
                rolePanel.Children.Add(EmptyNote("(no roles)"));
        }

        private CheckBox MakeCheck(string label, bool isChecked)
        {
            var cb = new CheckBox { Content = label, IsChecked = isChecked, Margin = new Thickness(0, 2, 0, 2) };
            cb.Checked += OnAnyChanged;
            cb.Unchecked += OnAnyChanged;
            return cb;
        }

        private static TextBlock EmptyNote(string text) =>
            new TextBlock { Text = text, Foreground = Brushes.Gray, FontStyle = FontStyles.Italic };

        private static HashSet<string> CheckedSet(Panel panel) =>
            new HashSet<string>(CheckedLabels(panel), StringComparer.OrdinalIgnoreCase);

        private static List<string> CheckedLabels(Panel panel) =>
            panel.Children.OfType<CheckBox>()
                 .Where(c => c.IsChecked == true && c.Content is string)
                 .Select(c => (string)c.Content)
                 .ToList();

        // ── events / preview ─────────────────────────────────────────────────
        private void WireEvents()
        {
            chkIndividual.Checked += OnIndividualChanged;
            chkIndividual.Unchecked += OnIndividualChanged;
            chkPrefix.Checked += OnPrefixChanged;
            chkPrefix.Unchecked += OnPrefixChanged;

            foreach (var cb in new[] { chkFoldersFirst, chkSuffix, chkHighlight })
            {
                cb.Checked += OnAnyChanged;
                cb.Unchecked += OnAnyChanged;
            }
            cmbRender.SelectionChanged += (s, e) => { UpdateDensityVisibility(); UpdatePreview(); };
            cmbDisplay.SelectionChanged += OnAnyChanged;
            cmbSort.SelectionChanged += OnAnyChanged;
            cmbDensity.SelectionChanged += OnAnyChanged;
            sldTextSize.ValueChanged += (s, e) =>
            {
                lblTextSize.Text = ((int)Math.Round(sldTextSize.Value)).ToString(CultureInfo.InvariantCulture);
                UpdatePreview();
            };
            sldCardMin.ValueChanged += (s, e) =>
            {
                lblCardMin.Text = ((int)Math.Round(sldCardMin.Value)).ToString(CultureInfo.InvariantCulture);
                UpdatePreview();
            };
            txtCountColor.TextChanged += (s, e) => { UpdateSwatch(countSwatch, txtCountColor.Text); UpdatePreview(); };
            txtOfflineColor.TextChanged += (s, e) => { UpdateSwatch(offlineSwatch, txtOfflineColor.Text); UpdatePreview(); };
        }

        private void OnAnyChanged(object sender, RoutedEventArgs e) => UpdatePreview();

        private void OnIndividualChanged(object sender, RoutedEventArgs e)
        {
            UpdateSelectionEnabled();
            // First time the operator turns this on with nothing picked, start from "all selected"
            // so the tile isn't blank - they then untick what they don't want.
            if (chkIndividual.IsChecked == true &&
                CheckedSet(folderPanel).Count == 0 && CheckedSet(rolePanel).Count == 0)
            {
                _suspend = true;
                SetAll(folderPanel, true);
                SetAll(rolePanel, true);
                _suspend = false;
            }
            UpdatePreview();
        }

        private void OnPrefixChanged(object sender, RoutedEventArgs e)
        {
            // Folder labels depend on the prefix; repopulate, keeping ticks that still match.
            PopulateFolders(CheckedSet(folderPanel));
            UpdatePreview();
        }

        private void UpdateSelectionEnabled()
        {
            selectionGrid.IsEnabled = chkIndividual.IsChecked == true;
        }

        // Row spacing applies only to list rows; card width only to dashboard cards.
        private void UpdateDensityVisibility()
        {
            bool dashboard = cmbRender.SelectedIndex == 1;
            var listVis = dashboard ? Visibility.Collapsed : Visibility.Visible;
            lblDensity.Visibility = listVis;
            cmbDensity.Visibility = listVis;
            cardSizePanel.Visibility = dashboard ? Visibility.Visible : Visibility.Collapsed;
        }

        private static void SetAll(Panel panel, bool value)
        {
            foreach (var cb in panel.Children.OfType<CheckBox>()) cb.IsChecked = value;
        }

        private void OnStatusChanged(object sender, StatusChangedEventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(UpdatePreview));
        }

        private void UpdatePreview()
        {
            if (_suspend) return;
            var s = BuildSettingsFromControls();
            preview.Render(s, s.BuildFolders(_plugin), s.BuildRoles(_plugin));
        }

        private CameraUserStatusSettings BuildSettingsFromControls()
        {
            var s = new CameraUserStatusSettings
            {
                IndividualSelection = chkIndividual.IsChecked == true,
                ShowServerPrefix = chkPrefix.IsChecked == true,
                SelectedFolders = CheckedSet(folderPanel),
                SelectedRoles = CheckedSet(rolePanel),
                ShowFolders = cmbDisplay.SelectedIndex != 2,   // all / cameras-only
                ShowRoles = cmbDisplay.SelectedIndex != 1,     // all / roles-only
                FoldersFirst = chkFoldersFirst.IsChecked == true,
                ShowSuffix = chkSuffix.IsChecked == true,
                HighlightOffline = chkHighlight.IsChecked == true,
                Compact = cmbDensity.SelectedIndex == 1,
                SortByOffline = cmbSort.SelectedIndex == 1,
                Dashboard = cmbRender.SelectedIndex == 1,
                TextSize = Math.Max(8, Math.Round(sldTextSize.Value)),
                CardMinWidth = Math.Round(sldCardMin.Value)
            };
            var count = TryBrush(txtCountColor.Text);
            if (count != null) s.CountBrush = count;
            var offline = TryBrush(txtOfflineColor.Text);
            if (offline != null) s.OfflineBrush = offline;
            return s;
        }

        // ── color helpers ────────────────────────────────────────────────────
        private static void UpdateSwatch(Border swatch, string hex)
        {
            var b = TryBrush(hex);
            swatch.Background = b ?? Brushes.Transparent;
        }

        private static Brush TryBrush(string hex)
        {
            if (string.IsNullOrWhiteSpace(hex)) return null;
            try
            {
                var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex.Trim()));
                if (b.CanFreeze) b.Freeze();
                return b;
            }
            catch { return null; }
        }

        private static string NormalizeColor(string hex, string fallback) =>
            TryBrush(hex) != null ? hex.Trim() : fallback;

        private static string Bool(CheckBox cb) => cb.IsChecked == true ? "true" : "false";

        // Display mode <-> combo index: 0 = Show all, 1 = Cameras only, 2 = Roles only.
        private static int DisplayIndex(CameraUserStatusSettings s)
        {
            if (s.ShowFolders && !s.ShowRoles) return 1;
            if (!s.ShowFolders && s.ShowRoles) return 2;
            return 0;
        }

        private static string DisplayModeFromIndex(int index)
        {
            switch (index)
            {
                case 1: return "Cameras";
                case 2: return "Roles";
                default: return "All";
            }
        }
    }
}
