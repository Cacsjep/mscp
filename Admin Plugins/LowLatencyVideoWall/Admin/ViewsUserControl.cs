using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LowLatencyVideoWall.Admin.Wpf;
using LowLatencyVideoWall.Config;
using LowLatencyVideoWall.Model;
using Material.Icons;
using VideoOS.Platform;

namespace LowLatencyVideoWall.Admin
{
    /// <summary>
    /// Edits one wall view: its name, and the rectangles the cameras occupy.
    ///
    /// One configuration item is one view, because a rule action picks its target from a
    /// dropdown the Management Client builds out of the plugin's configuration items of that
    /// kind. The item name is therefore the profile name and nothing else.
    ///
    /// The layout itself is drawn in LayoutEditorControl on the same 0 to 1000 normalised
    /// space PaneDefinition stores.
    /// </summary>
    internal sealed class ViewsUserControl : System.Windows.Controls.UserControl
    {
        private readonly ResourceDictionary _res = AdminTheme.BuildResources();

        private readonly TextBox _name = AdminLayout.BareTextBox();
        private readonly TextBox _description = AdminLayout.BareTextBox();

        private Button _assignCamera, _clearCamera, _deletePane, _applyGrid, _clearLayout;
        private ComboBox _gridPreset;
        private TextBlock _status;

        private readonly LayoutEditorControl _editor = new LayoutEditorControl();

        // Read once per detail control and reused by every camera pick, so building a sixteen
        // pane wall is not sixteen configuration reads.
        private readonly List<Item> _cameras = new List<Item>();
        private bool _discoveryStarted;

        private Guid _currentId = Guid.Empty;
        private bool _suppressChangeEvents;

        internal event EventHandler ConfigurationChangedByUser;

        internal ViewsUserControl()
        {
            Resources.MergedDictionaries.Add(_res);
            Background = Brushes.White;
            Content = BuildUi();

            _name.TextChanged += (s, e) => OnUserChange();
            _description.TextChanged += (s, e) => OnUserChange();

            _editor.LayoutChanged += (s, e) => OnUserChange();
            _editor.SelectionChanged += (s, e) => UpdateButtons();
            _editor.PaneActivated += (s, e) => Guarded(PickCameraForSelected);

            UpdateButtons();
            UpdateStatus();
        }

        // ---- the contract the ItemManager uses -------------------------------------------

        internal string DisplayName => (_name.Text ?? "").Trim();

        internal void FillContent(Item item)
        {
            if (item == null)
            {
                ClearContent();
                return;
            }

            _suppressChangeEvents = true;
            try
            {
                _currentId = item.FQID?.ObjectId ?? Guid.Empty;

                ViewProfile profile = null;
                try
                {
                    profile = WallConfigStore.LoadViewProfile(_currentId);
                }
                catch (Exception ex)
                {
                    AdminUi.ShowError("Low Latency Video Wall",
                        "This wall view could not be read from the configuration.", ex);
                }

                _name.Text = profile?.Name ?? item.Name ?? "";
                _description.Text = profile?.Description ?? "";
                _editor.LoadPanes(profile?.Panes);
            }
            finally
            {
                _suppressChangeEvents = false;
            }

            UpdateButtons();
            UpdateStatus();
            StartCameraPrefetch();
        }

        internal void ClearContent()
        {
            _suppressChangeEvents = true;
            try
            {
                _currentId = Guid.Empty;
                _name.Text = "";
                _description.Text = "";
                _editor.LoadPanes(null);
            }
            finally
            {
                _suppressChangeEvents = false;
            }

            UpdateButtons();
            UpdateStatus();
        }

        /// <summary>Null when the form is valid, otherwise the message to show the admin.</summary>
        internal string ValidateInput()
        {
            var name = DisplayName;
            if (name.Length == 0) return "Please give this wall view a name.";

            // The rule action picks a view by the name shown in a dropdown, so two views with
            // the same name are indistinguishable to whoever writes the rule.
            try
            {
                var clash = WallConfigStore.LoadViewProfiles()
                    .FirstOrDefault(p => p.Id != _currentId
                                         && string.Equals((p.Name ?? "").Trim(), name,
                                             StringComparison.CurrentCultureIgnoreCase));
                if (clash != null)
                    return $"Another wall view is already called \"{name}\". Pick a different name.";
            }
            catch (Exception ex)
            {
                LowLatencyVideoWallDefinition.Log.Error(
                    $"Checking wall view names failed: {AdminUi.Flatten(ex)}");
            }

            return null;
        }

        /// <summary>The profile as the form currently stands, keyed on the item it belongs to.</summary>
        internal ViewProfile BuildProfile(Guid id)
        {
            var profile = new ViewProfile
            {
                Id = id == Guid.Empty ? Guid.NewGuid() : id,
                Name = DisplayName,
                Description = (_description.Text ?? "").Trim()
            };

            foreach (var pane in _editor.GetPanes())
                profile.Panes.Add(pane);

            return profile;
        }

        /// <summary>Keeps the tree label in step with the name field after a save.</summary>
        internal void UpdateItem(Item item)
        {
            if (item == null) return;
            item.Name = DisplayName;
        }

        internal void SetDisplayName(string name)
        {
            _suppressChangeEvents = true;
            try
            {
                _name.Text = name ?? "";
            }
            finally
            {
                _suppressChangeEvents = false;
            }
        }

        // ---- layout ----------------------------------------------------------------------

        private FrameworkElement BuildUi()
        {
            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var header = BuildHeaderCard();
            Grid.SetRow(header, 0);
            root.Children.Add(header);

            var layout = BuildLayoutCard();
            layout.Margin = new Thickness(10, 10, 10, 10);
            Grid.SetRow(layout, 1);
            root.Children.Add(layout);

            return root;
        }

        private Border BuildHeaderCard()
        {
            var fields = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
            fields.Children.Add(AdminLayout.Labeled(_res, "Name",
                AdminTheme.IconInputBox(MaterialIconKind.TextBoxOutline, _name, 300)));
            fields.Children.Add(AdminLayout.Labeled(_res, "Description",
                AdminTheme.IconInputBox(MaterialIconKind.TextBoxOutline, _description, 420)));

            var panel = new StackPanel();
            panel.Children.Add(AdminLayout.PageTitle("Wall view"));
            panel.Children.Add(fields);

            return AdminLayout.Card(panel, 14);
        }

        private Border BuildLayoutCard()
        {
            var panel = new DockPanel { LastChildFill = true };

            var header = AdminLayout.SectionHeader(_res, "LAYOUT");
            DockPanel.SetDock(header, Dock.Top);
            panel.Children.Add(header);

            var toolbar = BuildToolbar();
            DockPanel.SetDock(toolbar, Dock.Top);
            panel.Children.Add(toolbar);

            _status = new TextBlock
            {
                Foreground = AdminTheme.Muted,
                FontSize = 12.5,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 10, 0, 0)
            };
            DockPanel.SetDock(_status, Dock.Bottom);
            panel.Children.Add(_status);

            // The editor is the one element that takes whatever space is left, which is what
            // keeps the canvas as big as the window allows.
            _editor.MinHeight = 220;
            panel.Children.Add(_editor);

            return AdminLayout.Card(panel, 16);
        }

        private FrameworkElement BuildToolbar()
        {
            _assignCamera = AdminTheme.AccentButton(_res, MaterialIconKind.CameraPlusOutline, "Assign camera");
            _assignCamera.Click += (s, e) => Guarded(PickCameraForSelected);

            _clearCamera = AdminTheme.SecondaryButton(_res, MaterialIconKind.VideoOffOutline, "Clear camera");
            _clearCamera.Margin = new Thickness(8, 0, 0, 0);
            _clearCamera.Click += (s, e) => Guarded(() => _editor.ClearCameraOnSelected());

            _deletePane = AdminTheme.SecondaryButton(_res, MaterialIconKind.TrashCanOutline, "Delete pane");
            _deletePane.Margin = new Thickness(8, 0, 24, 0);
            _deletePane.Click += (s, e) => Guarded(() => _editor.DeleteSelected());

            _gridPreset = new ComboBox
            {
                Style = (Style)_res["FilterComboBoxBorderlessStyle"],
                ItemContainerStyle = (Style)_res["FilterComboBoxItemStyle"],
                VerticalAlignment = VerticalAlignment.Center
            };
            foreach (var preset in new[] { "1x1", "2x1", "2x2", "3x2", "3x3", "4x3", "4x4" })
                _gridPreset.Items.Add(preset);
            _gridPreset.SelectedIndex = 2;

            var presetBox = AdminTheme.IconInputBox(MaterialIconKind.ViewGridOutline, _gridPreset, 100,
                null, "The even grid the Apply grid button lays down");

            _applyGrid = AdminTheme.SecondaryButton(_res, MaterialIconKind.ViewGridPlusOutline, "Apply grid");
            _applyGrid.Margin = new Thickness(8, 0, 0, 0);
            _applyGrid.Click += (s, e) => Guarded(ApplyGridPreset);

            _clearLayout = AdminTheme.SecondaryButton(_res, MaterialIconKind.GridOff, "Clear layout");
            _clearLayout.Margin = new Thickness(8, 0, 0, 0);
            _clearLayout.Click += (s, e) => Guarded(ClearLayout);

            var bar = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
            bar.Children.Add(_assignCamera);
            bar.Children.Add(_clearCamera);
            bar.Children.Add(_deletePane);
            bar.Children.Add(presetBox);
            bar.Children.Add(_applyGrid);
            bar.Children.Add(_clearLayout);
            return bar;
        }

        // ---- actions ---------------------------------------------------------------------

        private void PickCameraForSelected()
        {
            var pane = _editor.SelectedPane;
            if (pane == null)
            {
                MessageBox.Show("Select a pane first, then assign its camera.",
                    "Low Latency Video Wall", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (!CameraPicker.TryPick(_cameras, out var cameraId, out var cameraName)) return;

            // The same camera twice on one wall means two decoders for one stream, which is
            // exactly what this plugin exists to avoid, so say so and let the admin decide.
            if (_editor.GetPanes().Any(p => p.CameraId == cameraId))
            {
                var answer = MessageBox.Show(
                    $"\"{cameraName}\" is already on this wall. Add it a second time?",
                    "Low Latency Video Wall", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (answer != MessageBoxResult.Yes) return;
            }

            _editor.AssignCameraToSelected(cameraId, cameraName);
        }

        private void ApplyGridPreset()
        {
            var preset = _gridPreset.SelectedItem as string;
            if (string.IsNullOrEmpty(preset)) return;

            var parts = preset.Split('x');
            if (parts.Length != 2
                || !int.TryParse(parts[0], out var columns)
                || !int.TryParse(parts[1], out var rows))
                return;

            if (_editor.PaneCount > 0
                && !AdminSupport.ConfirmDestructive($"Replace the layout with a {preset} grid?",
                    "The panes on this wall view, and the cameras already assigned to them, are lost."))
                return;

            _editor.FillWithGrid(columns, rows);
        }

        private void ClearLayout()
        {
            if (_editor.PaneCount == 0) return;

            if (!AdminSupport.ConfirmDestructive("Remove every pane from this wall view?",
                    "The wall view stays, but it will have nothing on it until you draw new panes."))
                return;

            _editor.ClearAll();
        }

        private void StartCameraPrefetch()
        {
            if (_discoveryStarted || _cameras.Count > 0) return;
            _discoveryStarted = true;

            // Off the UI thread: the camera list is a configuration read.
            Task.Run(() =>
            {
                var cameras = CameraPicker.ReadCameras();
                AdminUi.Post(this, () =>
                {
                    _cameras.Clear();
                    _cameras.AddRange(cameras.Where(c => c != null));
                });
            });
        }

        // ---- plumbing --------------------------------------------------------------------

        private void UpdateButtons()
        {
            var hasSelection = _editor.SelectedPane != null;
            _assignCamera.IsEnabled = hasSelection;
            _clearCamera.IsEnabled = hasSelection && _editor.SelectedPane.HasCamera;
            _deletePane.IsEnabled = hasSelection;
        }

        private void UpdateStatus()
        {
            var panes = _editor.GetPanes();
            var empty = panes.Count(p => p.CameraId == Guid.Empty);

            _status.Foreground = empty > 0 ? AdminTheme.Amber : AdminTheme.Muted;
            _status.Text = panes.Count == 0
                ? "No panes yet. Drag on the grid to create one."
                : empty == 0
                    ? $"{panes.Count} pane(s), every pane has a camera."
                    : $"{panes.Count} pane(s), {empty} without a camera. An empty pane stays black on the wall.";
        }

        private void OnUserChange()
        {
            if (_suppressChangeEvents) return;
            UpdateStatus();
            ConfigurationChangedByUser?.Invoke(this, EventArgs.Empty);
        }

        private void Guarded(Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                AdminUi.ShowError("Low Latency Video Wall",
                    "The wall view editor hit an unexpected error.", ex);
            }
        }
    }
}
