using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using LowLatencyVideoWall.Admin.Wpf;
using LowLatencyVideoWall.Model;
using Material.Icons;

namespace LowLatencyVideoWall.Admin
{
    /// <summary>
    /// One row per camera in the Milestone configuration, with what the camera itself reports
    /// over VAPIX next to it, so an installer can see at a glance which cameras are not set up
    /// for low latency and fix them from here.
    ///
    /// The table is filled in two passes. The configuration read is fast and gives every row
    /// its identity immediately; the per camera VAPIX reads are slow, run eight at a time and
    /// land in their own row as they arrive. A site with 200 cameras is therefore readable
    /// within a second even though the full status sweep takes a lot longer.
    ///
    /// Reachability is the dot in front of the camera name, and the address and model the row
    /// no longer has columns for are in that cell's tooltip.
    ///
    /// Nothing network related ever runs on the UI thread, and nothing touches a control from
    /// a background thread: every handoff goes through AdminUi.Post.
    /// </summary>
    internal sealed class OverviewUserControl : System.Windows.Controls.UserControl, IAdminPage
    {
        // Eight at a time: enough to hide per camera latency on a big site, low enough that
        // a recording server's cameras are not all hammered at once.
        private const int MaxParallelReads = 8;

        private readonly ResourceDictionary _res = AdminTheme.BuildResources();

        private readonly ObservableCollection<CamRow> _rows = new ObservableCollection<CamRow>();
        private readonly Dictionary<Guid, CamRow> _byCamera = new Dictionary<Guid, CamRow>();
        private readonly List<WallCamera> _cameras = new List<WallCamera>();

        private Button _refresh, _lowLatencyOn, _wdrOff;
        private ProgressBar _progress;
        private TextBlock _status;
        private DataGrid _grid;

        private Style _cellStyle;
        private Style _warningCellStyle;

        private CancellationTokenSource _cts;
        private bool _busy;
        private bool _loadedOnce;
        private int _expectedReads;
        private int _completedReads;

        internal OverviewUserControl()
        {
            Resources.MergedDictionaries.Add(_res);
            Background = Brushes.White;
            Content = BuildUi();

            // Belt and braces: the page loads itself as soon as it is on screen, so it still
            // fills even if the Management Client never calls Init on the hosting node control.
            Loaded += (s, e) => Activate();
        }

        // ---- IAdminPage -----------------------------------------------------------------

        public void Activate()
        {
            // Lazy: clicking through the tree should not trigger a site wide VAPIX sweep
            // more than once, a refresh is one button away.
            if (_loadedOnce) return;
            _loadedOnce = true;
            Guarded(() => StartRefresh(false));
        }

        public void Deactivate() => CancelWork();

        // ---- layout ---------------------------------------------------------------------

        private FrameworkElement BuildUi()
        {
            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var header = BuildHeaderCard();
            Grid.SetRow(header, 0);
            root.Children.Add(header);

            var table = BuildTableCard();
            table.Margin = new Thickness(10, 10, 10, 10);
            Grid.SetRow(table, 1);
            root.Children.Add(table);

            return root;
        }

        private Border BuildHeaderCard()
        {
            _refresh = AdminTheme.AccentButton(_res, MaterialIconKind.Refresh, "Refresh");
            _refresh.Click += (s, e) => Guarded(() => StartRefresh(true));

            _lowLatencyOn = AdminTheme.SecondaryButton(_res, MaterialIconKind.Speedometer, "Low latency mode on (all)");
            _lowLatencyOn.Margin = new Thickness(8, 0, 0, 0);
            _lowLatencyOn.Click += (s, e) => Guarded(() => StartBulk(BulkAction.LowLatencyModeOn));

            _wdrOff = AdminTheme.SecondaryButton(_res, MaterialIconKind.WeatherSunny, "WDR off (all)");
            _wdrOff.Margin = new Thickness(8, 0, 0, 0);
            _wdrOff.Click += (s, e) => Guarded(() => StartBulk(BulkAction.WdrOff));

            var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            actions.Children.Add(_refresh);
            actions.Children.Add(_lowLatencyOn);
            actions.Children.Add(_wdrOff);

            var titleRow = new DockPanel { LastChildFill = false };
            var title = AdminLayout.PageTitle("Camera overview");
            DockPanel.SetDock(title, Dock.Left);
            DockPanel.SetDock(actions, Dock.Right);
            titleRow.Children.Add(title);
            titleRow.Children.Add(actions);

            _progress = new ProgressBar
            {
                Height = 3,
                Width = 220,
                Visibility = Visibility.Collapsed,
                Margin = new Thickness(0, 0, 12, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = AdminTheme.Accent
            };
            _status = new TextBlock
            {
                Foreground = AdminTheme.Muted,
                FontSize = 12.5,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };

            var statusRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 10, 0, 0)
            };
            statusRow.Children.Add(_progress);
            statusRow.Children.Add(_status);

            var panel = new StackPanel();
            panel.Children.Add(titleRow);
            panel.Children.Add(statusRow);

            return AdminLayout.Card(panel, 14);
        }

        private Border BuildTableCard()
        {
            _cellStyle = BuildCellStyle(null);
            _warningCellStyle = BuildCellStyle(nameof(CamRow.Warnings));

            _grid = new DataGrid();
            AdminTheme.ConfigureGrid(_grid);
            _grid.Resources.MergedDictionaries.Add(_res);
            _grid.IsReadOnly = true;
            _grid.CellStyle = (Style)_res["DataGridCellStyle"];
            _grid.RowStyle = BuildRowStyle();
            _grid.RowHeaderWidth = 0;
            _grid.ItemsSource = _rows;

            _grid.Columns.Add(BuildNameColumn());
            AddTextColumn("Capture mode", nameof(CamRow.CaptureMode), 130);
            AddTextColumn("WDR", nameof(CamRow.Wdr), 60);
            AddTextColumn("Low latency", nameof(CamRow.LowLatency), 85);
            AddTextColumn("Cfg fps", nameof(CamRow.ConfiguredFps), 60);
            AddTextColumn("Rotation", nameof(CamRow.Rotation), 70);
            AddTextColumn("H.264 profile", nameof(CamRow.H264Profile), 95);
            AddTextColumn("Warnings", nameof(CamRow.Warnings), 200, warning: true);

            var panel = new DockPanel { LastChildFill = true };
            var header = AdminLayout.SectionHeader(_res, "CAMERAS");
            DockPanel.SetDock(header, Dock.Top);
            panel.Children.Add(header);
            panel.Children.Add(_grid);

            return AdminLayout.Card(panel, 16);
        }

        private void AddTextColumn(string header, string path, double weight, bool warning = false)
        {
            _grid.Columns.Add(new DataGridTextColumn
            {
                Header = header,
                Binding = new Binding(path),
                Width = new DataGridLength(weight, DataGridLengthUnitType.Star),
                IsReadOnly = true,
                SortMemberPath = path,
                ElementStyle = warning ? _warningCellStyle : _cellStyle
            });
        }

        /// <summary>
        /// The cell look for every text column: the row decides its own colour, because a
        /// camera this plugin cannot drive and one that simply did not answer have to read
        /// differently without the grid knowing anything about VAPIX.
        /// </summary>
        private Style BuildCellStyle(string tooltipPath)
        {
            var style = new Style(typeof(TextBlock));
            style.Setters.Add(new Setter(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center));
            style.Setters.Add(new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis));
            style.Setters.Add(new Setter(TextBlock.ForegroundProperty, new Binding(nameof(CamRow.Fore))));
            style.Setters.Add(new Setter(TextBlock.FontStyleProperty, new Binding(nameof(CamRow.Italic))));
            if (tooltipPath != null)
                style.Setters.Add(new Setter(FrameworkElement.ToolTipProperty, new Binding(tooltipPath)));
            return style;
        }

        /// <summary>
        /// The shared row style plus the two tints the overview adds: amber for a camera that
        /// answered but is misconfigured, grey for hardware this plugin cannot drive. An
        /// unreachable row is left to the theme's own HasError trigger.
        /// </summary>
        private Style BuildRowStyle()
        {
            var style = new Style(typeof(DataGridRow), (Style)_res[typeof(DataGridRow)]);

            style.Triggers.Add(new DataTrigger
            {
                Binding = new Binding(nameof(CamRow.WarnTint)),
                Value = true,
                Setters = { new Setter(Control.BackgroundProperty, Tint("#FFF8E1")) }
            });
            style.Triggers.Add(new DataTrigger
            {
                Binding = new Binding(nameof(CamRow.Unsupported)),
                Value = true,
                Setters = { new Setter(Control.BackgroundProperty, Tint("#F7F7F7")) }
            });

            return style;
        }

        private DataGridTemplateColumn BuildNameColumn()
        {
            var row = new FrameworkElementFactory(typeof(StackPanel));
            row.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
            row.SetBinding(FrameworkElement.ToolTipProperty, new Binding(nameof(CamRow.Details)));

            var dot = new FrameworkElementFactory(typeof(Ellipse));
            dot.SetValue(FrameworkElement.WidthProperty, 8.0);
            dot.SetValue(FrameworkElement.HeightProperty, 8.0);
            dot.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 9, 0));
            dot.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            dot.SetBinding(Shape.FillProperty, new Binding(nameof(CamRow.DotBrush)));

            var name = new FrameworkElementFactory(typeof(TextBlock));
            name.SetBinding(TextBlock.TextProperty, new Binding(nameof(CamRow.Name)));
            name.SetBinding(TextBlock.ForegroundProperty, new Binding(nameof(CamRow.Fore)));
            name.SetBinding(TextBlock.FontStyleProperty, new Binding(nameof(CamRow.Italic)));
            name.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            name.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);

            row.AppendChild(dot);
            row.AppendChild(name);

            return new DataGridTemplateColumn
            {
                Header = "Camera",
                Width = new DataGridLength(190, DataGridLengthUnitType.Star),
                IsReadOnly = true,
                SortMemberPath = nameof(CamRow.Name),
                CellTemplate = new DataTemplate { VisualTree = row }
            };
        }

        private static Brush Tint(string hex)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            brush.Freeze();
            return brush;
        }

        // ---- refresh --------------------------------------------------------------------

        private void StartRefresh(bool forgetCredentials)
        {
            if (_busy) return;

            CancelWork();
            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            if (forgetCredentials) AdminVapixBridge.ForgetPasswords();

            SetBusy(true);
            _byCamera.Clear();
            _cameras.Clear();
            _rows.Clear();
            _expectedReads = 0;
            _completedReads = 0;
            _progress.Visibility = Visibility.Visible;
            _progress.IsIndeterminate = true;
            _status.Foreground = AdminTheme.Muted;
            _status.Text = "Reading the Milestone configuration...";

            Task.Run(() => RefreshWorker(token));
        }

        private async Task RefreshWorker(CancellationToken token)
        {
            List<WallCamera> cameras;
            try
            {
                cameras = AdminVapixBridge.DiscoverCameras()
                    .OrderBy(c => c.RecordingServer ?? "", StringComparer.CurrentCultureIgnoreCase)
                    .ThenBy(c => c.Name ?? "", StringComparer.CurrentCultureIgnoreCase)
                    .ToList();
            }
            catch (Exception ex)
            {
                AdminUi.Post(this, () =>
                {
                    SetBusy(false);
                    _progress.Visibility = Visibility.Collapsed;
                    _status.Foreground = AdminTheme.Danger;
                    _status.Text = "Reading cameras failed. See the log for detail.";
                    AdminUi.ShowError("Low Latency Video Wall",
                        "The camera list could not be read from the Milestone configuration.", ex);
                });
                return;
            }

            var axis = cameras.Where(c => c != null && c.IsAxis).ToList();

            AdminUi.Post(this, () =>
            {
                PopulateRows(cameras);
                _expectedReads = axis.Count;
                _completedReads = 0;
                if (_expectedReads == 0)
                {
                    _progress.Visibility = Visibility.Collapsed;
                    _status.Text = cameras.Count == 0
                        ? "No cameras found in the configuration."
                        : $"{cameras.Count} camera(s), none of them Axis, nothing to read.";
                    SetBusy(false);
                }
                else
                {
                    _progress.IsIndeterminate = false;
                    _progress.Minimum = 0;
                    _progress.Maximum = _expectedReads;
                    _progress.Value = 0;
                    _status.Text = $"Reading camera status 0 of {_expectedReads}...";
                }
            });

            if (axis.Count == 0) return;

            using (var gate = new SemaphoreSlim(MaxParallelReads))
            {
                var reads = axis.Select(camera => ReadOneAsync(camera, gate, token)).ToList();

                try
                {
                    await Task.WhenAll(reads).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    // Individual failures are already captured per row; anything landing here
                    // is unexpected and only needs a log line.
                    LowLatencyVideoWallDefinition.Log.Error(
                        $"Camera status sweep ended with an error: {AdminUi.Flatten(ex)}");
                }
            }

            if (token.IsCancellationRequested) return;

            AdminUi.Post(this, () =>
            {
                _progress.Visibility = Visibility.Collapsed;
                SetBusy(false);
                var flagged = _rows.Count(r => r.Flagged);
                var unreachable = _rows.Count(r => r.Unreachable);
                _status.Foreground = flagged > 0 || unreachable > 0 ? AdminTheme.Amber : AdminTheme.Ok;
                _status.Text =
                    $"{_cameras.Count} camera(s), {_expectedReads} Axis read, " +
                    $"{unreachable} unreachable, {flagged} with warnings.";
            });
        }

        private async Task ReadOneAsync(WallCamera camera, SemaphoreSlim gate, CancellationToken token)
        {
            try
            {
                await gate.WaitAsync(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            try
            {
                if (token.IsCancellationRequested) return;

                CameraStatus status = null;
                string error = null;

                try
                {
                    status = await AdminVapixBridge.ReadStatusAsync(camera, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    error = AdminUi.Flatten(ex);
                    LowLatencyVideoWallDefinition.Log.Error(
                        $"Status read failed for '{camera?.Name}' at {camera?.Host}: {error}");
                }

                if (token.IsCancellationRequested) return;

                AdminUi.Post(this, () => ApplyStatus(camera, status, error));
            }
            finally
            {
                try { gate.Release(); } catch { }
            }
        }

        // ---- rows -----------------------------------------------------------------------

        private void PopulateRows(List<WallCamera> cameras)
        {
            _cameras.Clear();
            _cameras.AddRange(cameras);

            _rows.Clear();
            _byCamera.Clear();

            foreach (var camera in cameras)
            {
                if (camera == null) continue;

                var row = new CamRow(camera);
                _rows.Add(row);

                if (_byCamera.ContainsKey(camera.CameraId))
                {
                    // Two configuration items with the same camera id should not happen, but
                    // a duplicate would silently drop a row otherwise.
                    LowLatencyVideoWallDefinition.Log.Error(
                        $"Duplicate camera id {camera.CameraId} for '{camera.Name}', the later row will not update");
                }
                else
                {
                    _byCamera[camera.CameraId] = row;
                }
            }
        }

        private void ApplyStatus(WallCamera camera, CameraStatus status, string error)
        {
            try
            {
                _completedReads++;
                if (_progress.Visibility == Visibility.Visible && !_progress.IsIndeterminate)
                {
                    _progress.Value = Math.Min(_completedReads, _progress.Maximum);
                    _status.Text = $"Reading camera status {_completedReads} of {_expectedReads}...";
                }

                if (camera == null || !_byCamera.TryGetValue(camera.CameraId, out var row)) return;
                row.Apply(status, error);
            }
            catch (Exception ex)
            {
                // A single bad row must never take the page down.
                LowLatencyVideoWallDefinition.Log.Error(
                    $"Filling the overview row for '{camera?.Name}' failed: {AdminUi.Flatten(ex)}");
            }
        }

        // ---- bulk actions ---------------------------------------------------------------

        /// <summary>
        /// Applies one setting to every Axis camera on the site. There is deliberately no
        /// confirmation dialog: this is the point of the button. The compensation is that
        /// AdminVapixBridge writes every old and new value to the plugin log before the
        /// summary appears, because nothing here can be undone.
        /// </summary>
        private void StartBulk(BulkAction action)
        {
            if (_busy) return;

            var targets = _cameras.Where(c => c != null && c.IsAxis).ToList();
            if (targets.Count == 0)
            {
                MessageBox.Show("There are no Axis cameras in the list to apply this to.",
                    "Low Latency Video Wall", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            CancelWork();
            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            SetBusy(true);
            _progress.Visibility = Visibility.Visible;
            _progress.IsIndeterminate = true;
            _status.Foreground = AdminTheme.Muted;
            _status.Text = $"Applying {AdminVapixBridge.Describe(action)} to {targets.Count} camera(s)...";

            Task.Run(async () =>
            {
                List<BulkOutcome> outcomes = null;
                Exception failure = null;

                try
                {
                    outcomes = await AdminVapixBridge.ApplyAsync(action, targets, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    failure = ex;
                }

                if (token.IsCancellationRequested) return;
                AdminUi.Post(this, () => BulkFinished(action, outcomes, failure));
            });
        }

        private void BulkFinished(BulkAction action, List<BulkOutcome> outcomes, Exception failure)
        {
            _progress.Visibility = Visibility.Collapsed;
            SetBusy(false);

            var label = AdminVapixBridge.Describe(action);

            if (failure != null)
            {
                _status.Foreground = AdminTheme.Danger;
                _status.Text = $"{label} failed.";
                AdminUi.ShowError("Low Latency Video Wall",
                    $"{label} could not be applied. Some cameras may already have been changed, " +
                    "the log lists every change that went through.", failure);
                return;
            }

            outcomes = outcomes ?? new List<BulkOutcome>();
            var ok = outcomes.Count(o => o.Success);
            var failed = outcomes.Count - ok;

            _status.Foreground = failed > 0 ? AdminTheme.Amber : AdminTheme.Ok;
            _status.Text = $"{label}: {ok} succeeded, {failed} failed.";

            var lines = new List<string>();
            foreach (var o in outcomes.OrderBy(o => o.Success).ThenBy(o => o.CameraName,
                         StringComparer.CurrentCultureIgnoreCase))
            {
                lines.Add(o.Success
                    ? $"OK      {o.CameraName}: {o.Setting} '{o.OldValue}' -> '{o.NewValue}'"
                    : $"FAILED  {o.CameraName}: {o.Setting} stayed '{o.OldValue}' - {o.Error}");
            }

            if (lines.Count == 0) lines.Add("No camera reported a result.");

            AdminUi.ShowResultReport($"{label} - result",
                $"{ok} succeeded, {failed} failed. Every change is in the plugin log with its old and new value.",
                lines);

            // Read the site back so the table shows what the cameras now actually report
            // rather than what was asked for.
            Guarded(() => StartRefresh(false));
        }

        // ---- plumbing -------------------------------------------------------------------

        private void SetBusy(bool busy)
        {
            _busy = busy;
            _refresh.IsEnabled = !busy;
            _lowLatencyOn.IsEnabled = !busy;
            _wdrOff.IsEnabled = !busy;
            Cursor = busy ? Cursors.AppStarting : Cursors.Arrow;
        }

        private void CancelWork()
        {
            // Cancelled and dropped rather than disposed: a sweep that is still winding down
            // holds this token, and registering a wait on a disposed source throws.
            try
            {
                _cts?.Cancel();
            }
            catch
            {
            }
            finally
            {
                _cts = null;
            }
        }

        /// <summary>Last line of defence: nothing from this page may reach the Management Client.</summary>
        private void Guarded(Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                SetBusy(false);
                AdminUi.ShowError("Low Latency Video Wall",
                    "The camera overview hit an unexpected error.", ex);
            }
        }

        // ---- row model ------------------------------------------------------------------

        /// <summary>
        /// One camera in the table. It starts out as the configuration knows it and is filled
        /// in again when the VAPIX read for that camera lands, which is why every column is a
        /// notifying property rather than a cell the sweep writes into.
        /// </summary>
        internal sealed class CamRow : INotifyPropertyChanged
        {
            private static readonly Brush WarningFore = Make("#8C5A00");
            private static readonly Brush UnsupportedFore = Make("#9AA0A6");

            private string _captureMode = "", _wdr = "", _lowLatency = "", _configuredFps = "";
            private string _rotation = "", _h264Profile = "", _warnings = "";
            private Brush _dotBrush = AdminTheme.Grey;
            private Brush _fore = AdminTheme.Text;
            private FontStyle _italic = FontStyles.Normal;
            private bool _unreachable, _flagged, _warnTint;

            internal CamRow(WallCamera camera)
            {
                Name = camera.Name ?? "";
                Details = BuildDetails(camera);
                Unsupported = !camera.IsAxis;

                if (Unsupported)
                {
                    _fore = UnsupportedFore;
                    _italic = FontStyles.Italic;
                    _warnings = "Not an Axis camera, this plugin cannot read or change it";
                }
                else
                {
                    // The sweep has not reached this camera yet, and an empty row looks like
                    // a camera that answered with nothing.
                    _captureMode = _wdr = _lowLatency = _configuredFps = "...";
                    _rotation = _h264Profile = _warnings = "...";
                }
            }

            public string Name { get; }

            /// <summary>What the removed address and model columns used to say.</summary>
            public string Details { get; }

            public bool Unsupported { get; }

            public Brush DotBrush { get => _dotBrush; private set { _dotBrush = value; Raise(nameof(DotBrush)); } }
            public Brush Fore { get => _fore; private set { _fore = value; Raise(nameof(Fore)); } }
            public FontStyle Italic { get => _italic; private set { _italic = value; Raise(nameof(Italic)); } }

            public string CaptureMode { get => _captureMode; private set { _captureMode = value; Raise(nameof(CaptureMode)); } }
            public string Wdr { get => _wdr; private set { _wdr = value; Raise(nameof(Wdr)); } }
            public string LowLatency { get => _lowLatency; private set { _lowLatency = value; Raise(nameof(LowLatency)); } }
            public string ConfiguredFps { get => _configuredFps; private set { _configuredFps = value; Raise(nameof(ConfiguredFps)); } }
            public string Rotation { get => _rotation; private set { _rotation = value; Raise(nameof(Rotation)); } }
            public string H264Profile { get => _h264Profile; private set { _h264Profile = value; Raise(nameof(H264Profile)); } }
            public string Warnings { get => _warnings; private set { _warnings = value; Raise(nameof(Warnings)); } }

            /// <summary>Drives the shared row style's red tint.</summary>
            public bool HasError => _unreachable;

            /// <summary>Amber only while the camera did answer, so red wins for a dead one.</summary>
            public bool WarnTint { get => _warnTint; private set { _warnTint = value; Raise(nameof(WarnTint)); } }

            internal bool Unreachable => _unreachable;
            internal bool Flagged => _flagged;

            /// <summary>Second pass: what this camera actually reported.</summary>
            internal void Apply(CameraStatus status, string error)
            {
                if (status == null)
                {
                    CaptureMode = Wdr = LowLatency = ConfiguredFps = "";
                    Rotation = H264Profile = "";
                    Warnings = string.IsNullOrEmpty(error) ? "No answer from the camera" : error;
                    SetState(unreachable: true, flagged: true);
                    return;
                }

                CaptureMode = DescribeCaptureMode(status);
                Wdr = Trimmed(status.Wdr);
                LowLatency = Trimmed(status.LowLatencyMode);
                ConfiguredFps = status.ConfiguredFps > 0
                    ? status.ConfiguredFps.ToString(CultureInfo.CurrentCulture) : "";
                Rotation = status.Reachable
                    ? status.Rotation.ToString(CultureInfo.CurrentCulture) : "";
                H264Profile = Trimmed(status.H264Profile);

                var warnings = new List<string>();
                if (!string.IsNullOrWhiteSpace(status.Error)) warnings.Add(status.Error.Trim());
                if (status.Warnings != null) warnings.AddRange(status.Warnings.Where(w => !string.IsNullOrWhiteSpace(w)));
                if (!string.IsNullOrWhiteSpace(error)) warnings.Add(error);

                Warnings = string.Join("; ", warnings);
                SetState(unreachable: !status.Reachable, flagged: warnings.Count > 0);
            }

            private void SetState(bool unreachable, bool flagged)
            {
                _unreachable = unreachable;
                _flagged = flagged;

                DotBrush = unreachable ? AdminTheme.Danger : AdminTheme.Ok;
                Fore = unreachable ? AdminTheme.Danger : flagged ? WarningFore : AdminTheme.Text;
                Italic = FontStyles.Normal;
                WarnTint = flagged && !unreachable;
                Raise(nameof(HasError));
            }

            private static string BuildDetails(WallCamera camera)
            {
                var address = string.IsNullOrWhiteSpace(camera.Address) ? "no address" : camera.Address.Trim();
                var model = string.IsNullOrWhiteSpace(camera.Model) ? "unknown model" : camera.Model.Trim();
                var server = string.IsNullOrWhiteSpace(camera.RecordingServer) ? "" : camera.RecordingServer.Trim();
                var detail = $"{address}{Environment.NewLine}{model}";
                return server.Length == 0 ? detail : $"{detail}{Environment.NewLine}{server}";
            }

            private static string Trimmed(string value) => string.IsNullOrWhiteSpace(value) ? "" : value.Trim();

            private static string DescribeCaptureMode(CameraStatus status)
            {
                var name = Trimmed(status.CaptureModeName);
                var mode = Trimmed(status.CaptureMode);
                if (name.Length > 0 && mode.Length > 0 && !string.Equals(name, mode, StringComparison.OrdinalIgnoreCase))
                    return $"{name} ({mode})";
                return name.Length > 0 ? name : mode;
            }

            private static Brush Make(string hex)
            {
                var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
                brush.Freeze();
                return brush;
            }

            public event PropertyChangedEventHandler PropertyChanged;
            private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
