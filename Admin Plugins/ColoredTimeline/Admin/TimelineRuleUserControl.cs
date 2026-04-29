using ColoredTimeline.Background;
using CommunitySDK;
using FontAwesome5;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Media.Imaging;
using VideoOS.Platform;
using VideoOS.Platform.Proxy.Alarm;
using VideoOS.Platform.Proxy.AlarmClient;
using VideoOS.Platform.UI;

namespace ColoredTimeline.Admin
{
    public class TimelineRuleUserControl : UserControl
    {
        private const string DefaultColor = "#1E88E5";

        private Label _lblName;
        private TextBox _txtName;
        private CheckBox _chkEnabled;

        private Label _lblColor;
        private Panel _colorSwatch;
        private Button _btnPickColor;

        private GroupBox _grpCameras;
        private ListBox _lstCameras;
        private Button _btnAddCamera;
        private Button _btnRemoveCamera;

        private GroupBox _grpStart;
        private Label _txtStart;
        private Button _btnPickStart;
        private Button _btnClearStart;

        private GroupBox _grpStop;
        private Label _txtStop;
        private Button _btnPickStop;
        private Button _btnClearStop;

        private CheckBox _chkAutoClose;
        private NumericUpDown _numAutoCloseSeconds;
        private Label _lblAutoCloseSuffix;

        private CheckBox _chkStartUseMarker;
        private PictureBox _icoStartGlyph;
        private Button _btnPickStartIcon;
        private Panel _swatchStartIconColor;
        private Button _btnPickStartIconColor;

        private CheckBox _chkStopUseMarker;
        private PictureBox _icoStopGlyph;
        private Button _btnPickStopIcon;
        private Panel _swatchStopIconColor;
        private Button _btnPickStopIconColor;

        private string _startIconName = "Solid_Play";
        private string _stopIconName = "Solid_Stop";
        private string _startIconColorHex = DefaultColor;
        private string _stopIconColorHex = DefaultColor;

        private Label _lblEventsTable;
        private CheckBox _chkOnlySelectedCameras;
        private Button _btnRefreshEvents;
        private ListView _lvEvents;
        private ToolTip _tip;
        private CancellationTokenSource _eventsCts;

        private readonly List<(Guid Id, string Name)> _selectedCameras = new List<(Guid, string)>();
        private string _ribbonColor = DefaultColor;
        private bool _filling;

        private readonly PluginLog _log = new PluginLog("ColoredTimeline - RuleUC");

        internal event EventHandler ConfigurationChangedByUser;

        public TimelineRuleUserControl()
        {
            InitializeComponent();
            _txtName.TextChanged += OnUserChange;
            _chkEnabled.CheckedChanged += OnUserChange;
            _btnPickColor.Click += OnPickColor;
            _colorSwatch.Click += OnPickColor;
            _btnAddCamera.Click += OnAddCamera;
            _btnRemoveCamera.Click += OnRemoveCamera;
            _btnPickStart.Click += (s, e) => OnPickEvent(_txtStart);
            _btnPickStop.Click += (s, e) => OnPickEvent(_txtStop);
            _btnClearStart.Click += (s, e) => { SetEventField(_txtStart, ""); OnUserChange(s, e); };
            _btnClearStop.Click += (s, e) => { SetEventField(_txtStop, ""); OnUserChange(s, e); };
            _txtStart.DoubleClick += (s, e) => OnPickEvent(_txtStart);
            _txtStop.DoubleClick += (s, e) => OnPickEvent(_txtStop);
            _lvEvents.MouseDoubleClick += OnEventsTableDoubleClick;
            _btnRefreshEvents.Click += (s, e) => RefreshEventsTable();
            ApplyColorToSwatch();

            if (!EventTypeCache.IsLoaded)
                EventTypeCache.Loaded += OnEventTypesLoaded;

            // Initial population once the handle is created (need it for BeginInvoke).
            // Also paint the marker glyph previews from their default values so the boxes
            // aren't empty before the first FillContent/ClearContent call.
            HandleCreated += (s, e) => { RefreshEventsTable(); RefreshIconPreviews(); };
        }

        private void OnEventTypesLoaded(object sender, EventArgs e)
        {
            EventTypeCache.Loaded -= OnEventTypesLoaded;
            // Re-render the Start/Stop labels: their display now depends on the cache's
            // DisplayName lookup, which wasn't available the first time SetEventField ran.
            BeginInvokeSafe(() =>
            {
                SetEventField(_txtStart, _txtStart.Tag as string);
                SetEventField(_txtStop, _txtStop.Tag as string);
            });
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            EventTypeCache.Loaded -= OnEventTypesLoaded;
            base.OnHandleDestroyed(e);
        }

        internal string DisplayName
        {
            get => _txtName.Text;
            set => _txtName.Text = value;
        }

        private void OnUserChange(object sender, EventArgs e)
        {
            if (_filling) return;
            ConfigurationChangedByUser?.Invoke(this, EventArgs.Empty);
        }

        private void InitializeComponent()
        {
            SuspendLayout();

            _tip = new ToolTip { AutoPopDelay = 10000, InitialDelay = 400, ReshowDelay = 200 };

            _lblName = new Label { Text = "Name:", Location = new Point(12, 15), AutoSize = true };
            _txtName = new TextBox { Location = new Point(120, 12), Size = new Size(360, 20) };
            _chkEnabled = new CheckBox { Text = "Enabled", Location = new Point(120, 38), AutoSize = true, Checked = true };

            _lblColor = new Label { Text = "Ribbon color:", Location = new Point(12, 65), AutoSize = true };
            _colorSwatch = new Panel
            {
                Location = new Point(120, 60),
                Size = new Size(40, 22),
                BorderStyle = BorderStyle.FixedSingle,
                Cursor = Cursors.Hand
            };
            _btnPickColor = new Button { Text = "Pick...", Location = new Point(168, 60), Size = new Size(75, 23) };

            _grpCameras = new GroupBox { Text = "Cameras", Location = new Point(12, 95), Size = new Size(936, 175) };
            _lstCameras = new ListBox { Location = new Point(10, 22), Size = new Size(916, 110) };
            _btnAddCamera = new Button { Text = "Add Camera...", Location = new Point(10, 138), Size = new Size(110, 23) };
            _btnRemoveCamera = new Button { Text = "Remove", Location = new Point(126, 138), Size = new Size(80, 23) };
            _grpCameras.Controls.AddRange(new Control[] { _lstCameras, _btnAddCamera, _btnRemoveCamera });

            // Two side-by-side GroupBoxes for Start / Stop event. Each box hosts:
            // (1) the event display label, (2) a per-event "Marker" row (checkbox +
            // glyph + Icon... + color swatch + Color...), (3) Pick/Clear at the bottom.
            _grpStart = new GroupBox { Text = "Start event", Location = new Point(12, 280), Size = new Size(462, 116) };
            _txtStart = new Label
            {
                Location = new Point(10, 22),
                Size = new Size(442, 22),
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = SystemColors.Window,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(4, 0, 4, 0),
                AutoEllipsis = true
            };
            // All controls in this row use the same Y (48) and Height (24) so they share
            // a single horizontal centerline (Y=60) regardless of font/glyph metrics.
            _chkStartUseMarker = new CheckBox
            {
                Text = "Marker",
                Location = new Point(10, 48),
                Size = new Size(72, 24),
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleLeft,
                Checked = false
            };
            _icoStartGlyph = new PictureBox
            {
                Location = new Point(86, 48),
                Size = new Size(24, 24),
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = SystemColors.Window,
                SizeMode = PictureBoxSizeMode.CenterImage
            };
            _btnPickStartIcon = new Button { Text = "Icon...", Location = new Point(114, 48), Size = new Size(64, 24) };
            _swatchStartIconColor = new Panel
            {
                Location = new Point(184, 48),
                Size = new Size(24, 24),
                BorderStyle = BorderStyle.FixedSingle,
                Cursor = Cursors.Hand
            };
            _btnPickStartIconColor = new Button { Text = "Color...", Location = new Point(212, 48), Size = new Size(64, 24) };
            _btnPickStart = new Button { Text = "Pick...", Location = new Point(298, 84), Size = new Size(75, 23) };
            _btnClearStart = new Button { Text = "Clear", Location = new Point(379, 84), Size = new Size(75, 23) };
            _grpStart.Controls.AddRange(new Control[]
            {
                _txtStart,
                _chkStartUseMarker, _icoStartGlyph, _btnPickStartIcon, _swatchStartIconColor, _btnPickStartIconColor,
                _btnPickStart, _btnClearStart
            });

            _grpStop = new GroupBox { Text = "Stop event", Location = new Point(486, 280), Size = new Size(462, 116) };
            _txtStop = new Label
            {
                Location = new Point(10, 22),
                Size = new Size(442, 22),
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = SystemColors.Window,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(4, 0, 4, 0),
                AutoEllipsis = true
            };
            _chkStopUseMarker = new CheckBox
            {
                Text = "Marker",
                Location = new Point(10, 48),
                Size = new Size(72, 24),
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleLeft,
                Checked = false
            };
            _icoStopGlyph = new PictureBox
            {
                Location = new Point(86, 48),
                Size = new Size(24, 24),
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = SystemColors.Window,
                SizeMode = PictureBoxSizeMode.CenterImage
            };
            _btnPickStopIcon = new Button { Text = "Icon...", Location = new Point(114, 48), Size = new Size(64, 24) };
            _swatchStopIconColor = new Panel
            {
                Location = new Point(184, 48),
                Size = new Size(24, 24),
                BorderStyle = BorderStyle.FixedSingle,
                Cursor = Cursors.Hand
            };
            _btnPickStopIconColor = new Button { Text = "Color...", Location = new Point(212, 48), Size = new Size(64, 24) };
            _btnPickStop = new Button { Text = "Pick...", Location = new Point(298, 84), Size = new Size(75, 23) };
            _btnClearStop = new Button { Text = "Clear", Location = new Point(379, 84), Size = new Size(75, 23) };
            _grpStop.Controls.AddRange(new Control[]
            {
                _txtStop,
                _chkStopUseMarker, _icoStopGlyph, _btnPickStopIcon, _swatchStopIconColor, _btnPickStopIconColor,
                _btnPickStop, _btnClearStop
            });

            // "Close pair if no stop event after N seconds" - per-rule cap for unmatched Starts.
            _chkAutoClose = new CheckBox
            {
                Text = "Close pair if no stop event after",
                Location = new Point(12, 408),
                AutoSize = true,
                Checked = false
            };
            _numAutoCloseSeconds = new NumericUpDown
            {
                Location = new Point(220, 406),
                Size = new Size(70, 22),
                Minimum = 1,
                Maximum = 3600,
                Value = 10,
                Enabled = false
            };
            _lblAutoCloseSuffix = new Label
            {
                Text = "seconds. When enabled, the Stop event is optional.",
                Location = new Point(296, 408),
                AutoSize = true,
                ForeColor = SystemColors.ControlDarkDark
            };
            _chkAutoClose.CheckedChanged += (s, e) =>
            {
                _numAutoCloseSeconds.Enabled = _chkAutoClose.Checked;
                OnUserChange(s, e);
            };
            _numAutoCloseSeconds.ValueChanged += OnUserChange;

            // Per-event marker wiring. Each side has its own UseMarker checkbox + icon +
            // color picker, so a rule can paint a Start marker only, a Stop marker only,
            // or both.
            _chkStartUseMarker.CheckedChanged += (s, e) => { ApplyMarkersEnabledState(); OnUserChange(s, e); };
            _chkStopUseMarker.CheckedChanged  += (s, e) => { ApplyMarkersEnabledState(); OnUserChange(s, e); };
            _btnPickStartIcon.Click += (s, e) => OnPickIcon(true);
            _btnPickStopIcon.Click  += (s, e) => OnPickIcon(false);
            _icoStartGlyph.Click += (s, e) => OnPickIcon(true);
            _icoStopGlyph.Click  += (s, e) => OnPickIcon(false);
            _btnPickStartIconColor.Click += (s, e) => OnPickIconColor(true);
            _btnPickStopIconColor.Click  += (s, e) => OnPickIconColor(false);
            _swatchStartIconColor.Click += (s, e) => OnPickIconColor(true);
            _swatchStopIconColor.Click  += (s, e) => OnPickIconColor(false);

            _lblEventsTable = new Label
            {
                Text = "Events from the last 24 h (double-click to use as Start, Shift+double-click for Stop):",
                Location = new Point(12, 438),
                AutoSize = true,
                ForeColor = SystemColors.ControlDarkDark
            };
            _chkOnlySelectedCameras = new CheckBox
            {
                Text = "Show only events from selected cameras",
                AutoSize = true,
                Checked = false,
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            _chkOnlySelectedCameras.CheckedChanged += (s, e) => RefreshEventsTable();
            _btnRefreshEvents = new Button
            {
                Text = "Refresh",
                Location = new Point(873, 434),
                Size = new Size(75, 22),
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            // Place checkbox 5 px to the left of Refresh. PreferredSize at construction time
            // is unreliable (font may not be inherited yet), so reposition after layout.
            void RepositionShowOnly()
            {
                var w = _chkOnlySelectedCameras.PreferredSize.Width;
                _chkOnlySelectedCameras.Location = new Point(_btnRefreshEvents.Left - w - 5, 436);
            }
            HandleCreated += (s, e) => RepositionShowOnly();
            FontChanged += (s, e) => RepositionShowOnly();
            _chkOnlySelectedCameras.TextChanged += (s, e) => RepositionShowOnly();

            _lvEvents = new ListView
            {
                Location = new Point(12, 458),
                Size = new Size(936, 200),
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                MultiSelect = false,
                HideSelection = false,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom
            };
            _lvEvents.Columns.Add("Time", 140);
            _lvEvents.Columns.Add("Camera", 250);
            _lvEvents.Columns.Add("Name", 540);
            // Time stays fixed; Camera/Name share the remaining width 30/70.
            // ListView doesn't auto-fill, so we resize the last two columns whenever the
            // grid is resized (anchor stretches the control across the form's width).
            _lvEvents.SizeChanged += (s, e) => AutoSizeEventColumns();

            Controls.AddRange(new Control[]
            {
                _lblName, _txtName, _chkEnabled,
                _lblColor, _colorSwatch, _btnPickColor,
                _grpCameras,
                _grpStart, _grpStop,
                _chkAutoClose, _numAutoCloseSeconds, _lblAutoCloseSuffix,
                _lblEventsTable, _chkOnlySelectedCameras, _btnRefreshEvents, _lvEvents
            });

            Name = "TimelineRuleUserControl";
            Size = new Size(960, 671);
            ResumeLayout(false);
            PerformLayout();
        }

        private void ApplyColorToSwatch()
        {
            try
            {
                _colorSwatch.BackColor = ColorTranslator.FromHtml(_ribbonColor);
            }
            catch
            {
                _ribbonColor = DefaultColor;
                _colorSwatch.BackColor = ColorTranslator.FromHtml(DefaultColor);
            }
        }

        private void ApplyMarkersEnabledState()
        {
            bool startOn = _chkStartUseMarker.Checked;
            _icoStartGlyph.Enabled = startOn;
            _btnPickStartIcon.Enabled = startOn;
            _swatchStartIconColor.Enabled = startOn;
            _btnPickStartIconColor.Enabled = startOn;

            bool stopOn = _chkStopUseMarker.Checked;
            _icoStopGlyph.Enabled = stopOn;
            _btnPickStopIcon.Enabled = stopOn;
            _swatchStopIconColor.Enabled = stopOn;
            _btnPickStopIconColor.Enabled = stopOn;
        }

        private void RefreshIconPreviews()
        {
            _icoStartGlyph.Image = RenderIconPreview(_startIconName, _startIconColorHex);
            _icoStopGlyph.Image = RenderIconPreview(_stopIconName, _stopIconColorHex);
            try { _swatchStartIconColor.BackColor = ColorTranslator.FromHtml(_startIconColorHex); }
            catch { _swatchStartIconColor.BackColor = ColorTranslator.FromHtml(DefaultColor); }
            try { _swatchStopIconColor.BackColor = ColorTranslator.FromHtml(_stopIconColorHex); }
            catch { _swatchStopIconColor.BackColor = ColorTranslator.FromHtml(DefaultColor); }
        }

        private static System.Drawing.Image RenderIconPreview(string iconName, string colorHex)
        {
            EFontAwesomeIcon icon;
            if (!MarkerIconRenderer.TryParseIcon(iconName, out icon))
                icon = EFontAwesomeIcon.Solid_Bell;
            var color = MarkerIconRenderer.ParseColor(colorHex,
                System.Windows.Media.Color.FromRgb(0x1E, 0x88, 0xE5));
            var bs = MarkerIconRenderer.Render(icon, color, 18);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(bs));
            // Bitmap requires the source MemoryStream to remain alive. Disposing the stream
            // (e.g. via using { }) leaves the Bitmap painting blank until something forces
            // a re-render. Same gotcha called out in CommunitySDK/PluginIcon.cs.
            var ms = new MemoryStream();
            enc.Save(ms);
            ms.Position = 0;
            return new System.Drawing.Bitmap(ms);
        }

        private void OnPickIcon(bool isStart)
        {
            EFontAwesomeIcon current;
            MarkerIconRenderer.TryParseIcon(isStart ? _startIconName : _stopIconName, out current);
            using (var dlg = new IconPickerDialog(current, isStart ? _startIconColorHex : _stopIconColorHex))
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    var name = dlg.SelectedIcon.ToString();
                    if (isStart) _startIconName = name; else _stopIconName = name;
                    RefreshIconPreviews();
                    OnUserChange(this, EventArgs.Empty);
                }
            }
        }

        private void OnPickIconColor(bool isStart)
        {
            Color start;
            try { start = ColorTranslator.FromHtml(isStart ? _startIconColorHex : _stopIconColorHex); }
            catch { start = ColorTranslator.FromHtml(DefaultColor); }

            using (var dlg = new ColorDialog
            {
                AllowFullOpen = true,
                AnyColor = true,
                FullOpen = true,
                Color = start
            })
            {
                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    var hex = ColorTranslator.ToHtml(dlg.Color);
                    if (isStart) _startIconColorHex = hex; else _stopIconColorHex = hex;
                    RefreshIconPreviews();
                    if (!_filling) OnUserChange(this, EventArgs.Empty);
                }
            }
        }

        private void OnPickColor(object sender, EventArgs e)
        {
            using (var dlg = new ColorDialog
            {
                AllowFullOpen = true,
                AnyColor = true,
                FullOpen = true,
                Color = _colorSwatch.BackColor
            })
            {
                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    _ribbonColor = ColorTranslator.ToHtml(dlg.Color);
                    ApplyColorToSwatch();
                    if (!_filling)
                        ConfigurationChangedByUser?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        private void OnPickEvent(Label target)
        {
            if (!EventTypeCache.IsLoaded)
            {
                MessageBox.Show("Event types are still loading. Please wait a moment and try again.",
                    "Colored Timeline", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            // Tag holds the unmodified event name (used for EventLog filter & saved property);
            // Text shows the friendly form without tns1:/tnsaxis: prefixes and with -0/-1 mapped
            // to (Falling)/(Rising).
            using (var dlg = new EventPickerDialog(EventTypeCache.Items, target.Tag as string))
            {
                if (dlg.ShowDialog(this) == DialogResult.OK && !string.IsNullOrEmpty(dlg.SelectedEvent))
                {
                    SetEventField(target, dlg.SelectedEvent);
                    OnUserChange(target, EventArgs.Empty);
                }
            }
        }

        private void SetEventField(Label target, string fullName)
        {
            target.Tag = fullName ?? "";
            target.Text = LookupDisplayName(fullName);
            // Show the raw event name in a tooltip so the user can always verify what was saved.
            try { _tip.SetToolTip(target, string.IsNullOrEmpty(fullName) ? null : fullName); }
            catch { }
        }

        // Returns the friendly DisplayName for a raw event name (the same string Mgmt Client's
        // Alarm Definition picker shows). Falls back to the namespace-stripped/Falling-Rising
        // formatted form if the cache hasn't loaded yet or the name isn't a known EventType.
        private static string LookupDisplayName(string fullName)
        {
            if (string.IsNullOrEmpty(fullName)) return "";
            if (EventTypeCache.IsLoaded)
            {
                foreach (var et in EventTypeCache.Items)
                {
                    if (string.Equals(et.Name, fullName, StringComparison.OrdinalIgnoreCase))
                        return string.IsNullOrEmpty(et.DisplayName) ? FormatEventForDisplay(fullName) : et.DisplayName;
                }
            }
            return FormatEventForDisplay(fullName);
        }

        // Strip tns1:/tnsaxis:/other-namespace prefix and convert trailing -0/-1 to (Falling)/(Rising).
        private static string FormatEventForDisplay(string fullName)
        {
            if (string.IsNullOrEmpty(fullName)) return "";
            string s = fullName;
            if (s.StartsWith("tns1:", StringComparison.OrdinalIgnoreCase))
                s = s.Substring("tns1:".Length);
            else if (s.StartsWith("tnsaxis:", StringComparison.OrdinalIgnoreCase))
                s = s.Substring("tnsaxis:".Length);
            else
            {
                int colon = s.IndexOf(':');
                if (colon > 0) s = s.Substring(colon + 1);
            }
            // Milestone's Alarm Definition picker maps "Rising" to the -0 suffix and "Falling"
            // to -1 for vendor-namespaced ONVIF topics (verified 2026-04-29 against the Axis
            // ObjectAnalytics ScenarioANY topic). The suffix is a Source-Index, not the
            // `active` flag, so the polarity is opposite to the literal ONVIF convention.
            if (s.EndsWith("-0", StringComparison.Ordinal))
                s = s.Substring(0, s.Length - 2) + " (Rising)";
            else if (s.EndsWith("-1", StringComparison.Ordinal))
                s = s.Substring(0, s.Length - 2) + " (Falling)";
            else if (s.EndsWith("/0", StringComparison.Ordinal))
                s = s.Substring(0, s.Length - 2) + " (Rising)";
            else if (s.EndsWith("/1", StringComparison.Ordinal))
                s = s.Substring(0, s.Length - 2) + " (Falling)";
            return s;
        }

        private void OnAddCamera(object sender, EventArgs e)
        {
            try
            {
                var picker = new ItemPickerWpfWindow
                {
                    Items = Configuration.Instance.GetItemsByKind(Kind.Camera),
                    KindsFilter = new List<Guid> { Kind.Camera },
                    SelectionMode = SelectionModeOptions.AutoCloseOnSelect
                };
                if (picker.ShowDialog() == true && picker.SelectedItems != null && picker.SelectedItems.Any())
                {
                    var cam = picker.SelectedItems.First();
                    var id = cam.FQID.ObjectId;
                    if (!_selectedCameras.Any(c => c.Id == id))
                    {
                        _selectedCameras.Add((id, cam.Name));
                        _lstCameras.Items.Add(cam.Name);
                        OnUserChange(sender, e);
                    }
                }
            }
            catch (Exception ex)
            {
                _log.Error($"Camera picker failed: {ex.Message}");
                MessageBox.Show("Could not open camera picker. See MIPLog.txt for details.",
                    "Colored Timeline", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void OnRemoveCamera(object sender, EventArgs e)
        {
            var idx = _lstCameras.SelectedIndex;
            if (idx >= 0)
            {
                _selectedCameras.RemoveAt(idx);
                _lstCameras.Items.RemoveAt(idx);
                OnUserChange(sender, e);
            }
        }

        // Pulls the last 24 h of EventLog rows from the master site (no camera filter, so
        // we see every device's events) and lists each distinct (camera, message) pair.
        // Same data source as the timeline filter, so the strings here are exactly what
        // ColoredTimelineSequenceSource will match against at runtime.
        private void RefreshEventsTable()
        {
            try { _eventsCts?.Cancel(); } catch { }
            _eventsCts = new CancellationTokenSource();
            var ct = _eventsCts.Token;

            // Snapshot UI state on the UI thread before launching the background query.
            bool onlySelected = _chkOnlySelectedCameras != null && _chkOnlySelectedCameras.Checked;
            var selectedSnapshot = _selectedCameras.Select(c => c.Id).ToList();

            _lvEvents.BeginUpdate();
            try
            {
                _lvEvents.Items.Clear();
                var loading = new ListViewItem("");
                loading.SubItems.Add("");
                loading.SubItems.Add("Loading EventLog...");
                _lvEvents.Items.Add(loading);
            }
            finally { _lvEvents.EndUpdate(); }

            Task.Run(() =>
            {
                IAlarmClient alarmClient = null;
                try
                {
                    var serverId = EnvironmentManager.Instance.MasterSite.ServerId;
                    alarmClient = new AlarmClientManager().GetAlarmClient(serverId);
                    if (alarmClient == null)
                    {
                        ShowEventsError("Could not connect to AlarmClient.");
                        return;
                    }

                    var filter = new EventFilter
                    {
                        Conditions = new[]
                        {
                            new Condition { Target = Target.Timestamp, Operator = Operator.GreaterThan, Value = DateTime.UtcNow.AddHours(-24) },
                            new Condition { Target = Target.Timestamp, Operator = Operator.LessThan,    Value = DateTime.UtcNow.AddMinutes(1) }
                        },
                        Orders = new[] { new OrderBy { Target = Target.Timestamp, Order = Order.Descending } }
                    };

                    if (ct.IsCancellationRequested) return;
                    var rows = alarmClient.GetEventLines(0, int.MaxValue, filter) ?? Array.Empty<EventLine>();
                    if (ct.IsCancellationRequested) return;

                    // Optional client-side filter for "Show only events from selected cameras".
                    // EventFilter Conditions are AND-combined, so multiple cameras can't be expressed
                    // server-side without N round-trips - the dataset is already capped at 24 h, so
                    // post-filtering is fine.
                    HashSet<Guid> selectedSet = onlySelected ? new HashSet<Guid>(selectedSnapshot) : null;

                    // No dedup - one row per EventLog entry. Cap at MaxRows newest-first so the
                    // grid stays responsive on busy systems.
                    const int MaxRows = 500;
                    var ordered = rows
                        .Where(r => !string.IsNullOrEmpty(r.Message))
                        .Where(r => selectedSet == null || selectedSet.Contains(r.CameraId))
                        .OrderByDescending(r => r.Timestamp)
                        .Take(MaxRows)
                        .ToList();

                    var camNameCache = new Dictionary<Guid, string>();
                    var data = new List<(DateTime Time, string CamName, string Message)>(ordered.Count);
                    foreach (var r in ordered)
                    {
                        if (ct.IsCancellationRequested) return;
                        data.Add((r.Timestamp, ResolveCameraName(r.CameraId, camNameCache), r.Message));
                    }

                    if (ct.IsCancellationRequested) return;
                    int totalAfterFilter = rows.Count(r => !string.IsNullOrEmpty(r.Message));
                    BeginInvokeSafe(() =>
                    {
                        _lvEvents.BeginUpdate();
                        try
                        {
                            _lvEvents.Items.Clear();
                            if (data.Count == 0)
                            {
                                var empty = new ListViewItem("");
                                empty.SubItems.Add("");
                                empty.SubItems.Add("No events in EventLog over the last 24 h.");
                                _lvEvents.Items.Add(empty);
                                return;
                            }
                            foreach (var d in data)
                            {
                                var row = new ListViewItem(d.Time.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff"));
                                row.SubItems.Add(d.CamName);
                                row.SubItems.Add(LookupDisplayName(d.Message));
                                row.Tag = d.Message;
                                _lvEvents.Items.Add(row);
                            }
                            _lblEventsTable.Text = totalAfterFilter > MaxRows
                                ? $"Events from the last 24 h - showing newest {MaxRows} of {totalAfterFilter} (double-click to use as Start, Shift+double-click for Stop):"
                                : $"Events from the last 24 h - {totalAfterFilter} row(s) (double-click to use as Start, Shift+double-click for Stop):";
                        }
                        finally { _lvEvents.EndUpdate(); }
                    });
                }
                catch (Exception ex)
                {
                    var msg = (ex is AggregateException ag)
                        ? string.Join("; ", ag.Flatten().InnerExceptions.Select(e => e.Message))
                        : ex.Message;
                    _log.Error($"RefreshEventsTable failed: {msg}");
                    ShowEventsError("EventLog query failed: " + msg);
                }
            }, ct);
        }

        private void AutoSizeEventColumns()
        {
            if (_lvEvents == null || _lvEvents.Columns.Count < 3) return;
            var time = _lvEvents.Columns[0];
            var cam = _lvEvents.Columns[1];
            var name = _lvEvents.Columns[2];
            // Reserve a little for vertical scrollbar + border.
            int remaining = _lvEvents.ClientSize.Width - time.Width - 4;
            if (remaining < 200) remaining = 200;
            cam.Width = (int)(remaining * 0.30);
            name.Width = remaining - cam.Width;
        }

        private void ShowEventsError(string text)
        {
            BeginInvokeSafe(() =>
            {
                _lvEvents.BeginUpdate();
                try
                {
                    _lvEvents.Items.Clear();
                    var row = new ListViewItem("");
                    row.SubItems.Add("");
                    row.SubItems.Add(text);
                    _lvEvents.Items.Add(row);
                }
                finally { _lvEvents.EndUpdate(); }
            });
        }

        private void BeginInvokeSafe(Action action)
        {
            try
            {
                if (IsDisposed || !IsHandleCreated) return;
                BeginInvoke(action);
            }
            catch { }
        }

        private static string ResolveCameraName(Guid cameraId, Dictionary<Guid, string> cache)
        {
            if (cache.TryGetValue(cameraId, out var cached)) return cached;
            string name = cameraId == Guid.Empty ? "(no camera)" : cameraId.ToString();
            try
            {
                var item = Configuration.Instance.GetItem(cameraId, Kind.Camera);
                if (item != null && !string.IsNullOrEmpty(item.Name)) name = item.Name;
            }
            catch { }
            cache[cameraId] = name;
            return name;
        }


        private void OnEventsTableDoubleClick(object sender, MouseEventArgs e)
        {
            var hit = _lvEvents.HitTest(e.Location);
            var item = hit?.Item;
            if (item?.Tag is string fullName && !string.IsNullOrEmpty(fullName))
            {
                var target = (Control.ModifierKeys & Keys.Shift) == Keys.Shift ? _txtStop : _txtStart;
                SetEventField(target, fullName);
                OnUserChange(this, EventArgs.Empty);
            }
        }

        public void FillContent(Item item)
        {
            if (item == null) return;
            _filling = true;
            try
            {
                _txtName.Text = item.Name;
                _chkEnabled.Checked = !item.Properties.ContainsKey("Enabled") || item.Properties["Enabled"] != "No";
                _ribbonColor = item.Properties.ContainsKey("RibbonColor") && !string.IsNullOrWhiteSpace(item.Properties["RibbonColor"])
                    ? item.Properties["RibbonColor"]
                    : DefaultColor;
                ApplyColorToSwatch();

                _selectedCameras.Clear();
                _lstCameras.Items.Clear();
                var ids = (item.Properties.ContainsKey("CameraIds") ? item.Properties["CameraIds"] : "")
                    .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
                var names = (item.Properties.ContainsKey("CameraNames") ? item.Properties["CameraNames"] : "")
                    .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < ids.Length; i++)
                {
                    if (!Guid.TryParse(ids[i].Trim(), out var camId)) continue;
                    var camName = i < names.Length ? names[i].Trim() : "(unknown)";
                    try
                    {
                        var camItem = Configuration.Instance.GetItem(camId, Kind.Camera);
                        if (camItem != null) camName = camItem.Name;
                    }
                    catch { }
                    _selectedCameras.Add((camId, camName));
                    _lstCameras.Items.Add(camName);
                }

                SetEventField(_txtStart, item.Properties.ContainsKey("StartEvent") ? item.Properties["StartEvent"] : "");
                SetEventField(_txtStop, item.Properties.ContainsKey("StopEvent") ? item.Properties["StopEvent"] : "");

                _chkAutoClose.Checked = item.Properties.ContainsKey("AutoCloseEnabled")
                    && item.Properties["AutoCloseEnabled"] == "Yes";
                int seconds = 10;
                if (item.Properties.ContainsKey("AutoCloseSeconds"))
                    int.TryParse(item.Properties["AutoCloseSeconds"], out seconds);
                if (seconds < 1) seconds = 1;
                if (seconds > 3600) seconds = 3600;
                _numAutoCloseSeconds.Value = seconds;
                _numAutoCloseSeconds.Enabled = _chkAutoClose.Checked;

                // Per-event marker enable. Fall back to legacy "UseMarkers" (single global
                // flag) so rules saved before per-event support keep working.
                bool legacyUseMarkers = item.Properties.ContainsKey("UseMarkers")
                    && item.Properties["UseMarkers"] == "Yes";
                _chkStartUseMarker.Checked = item.Properties.ContainsKey("StartUseMarker")
                    ? item.Properties["StartUseMarker"] == "Yes"
                    : legacyUseMarkers;
                _chkStopUseMarker.Checked = item.Properties.ContainsKey("StopUseMarker")
                    ? item.Properties["StopUseMarker"] == "Yes"
                    : legacyUseMarkers;
                _startIconName = item.Properties.ContainsKey("StartIcon") && !string.IsNullOrEmpty(item.Properties["StartIcon"])
                    ? item.Properties["StartIcon"] : "Solid_Play";
                _stopIconName = item.Properties.ContainsKey("StopIcon") && !string.IsNullOrEmpty(item.Properties["StopIcon"])
                    ? item.Properties["StopIcon"] : "Solid_Stop";
                _startIconColorHex = item.Properties.ContainsKey("StartIconColor") && !string.IsNullOrEmpty(item.Properties["StartIconColor"])
                    ? item.Properties["StartIconColor"] : _ribbonColor;
                _stopIconColorHex = item.Properties.ContainsKey("StopIconColor") && !string.IsNullOrEmpty(item.Properties["StopIconColor"])
                    ? item.Properties["StopIconColor"] : _ribbonColor;
                RefreshIconPreviews();
                ApplyMarkersEnabledState();
            }
            finally
            {
                _filling = false;
            }
            RefreshEventsTable();
        }

        public void ClearContent()
        {
            _filling = true;
            try
            {
                _txtName.Text = "";
                _chkEnabled.Checked = true;
                _ribbonColor = DefaultColor;
                ApplyColorToSwatch();
                _selectedCameras.Clear();
                _lstCameras.Items.Clear();
                SetEventField(_txtStart, "");
                SetEventField(_txtStop, "");
                _chkAutoClose.Checked = false;
                _numAutoCloseSeconds.Value = 10;
                _numAutoCloseSeconds.Enabled = false;
                _chkStartUseMarker.Checked = false;
                _chkStopUseMarker.Checked = false;
                _startIconName = "Solid_Play";
                _stopIconName = "Solid_Stop";
                _startIconColorHex = _ribbonColor;
                _stopIconColorHex = _ribbonColor;
                RefreshIconPreviews();
                ApplyMarkersEnabledState();
            }
            finally
            {
                _filling = false;
            }
            RefreshEventsTable();
        }

        public string ValidateInput()
        {
            if (string.IsNullOrWhiteSpace(_txtName.Text))
                return "Name is required.";
            if (_selectedCameras.Count == 0)
                return "At least one camera must be selected.";
            if (string.IsNullOrWhiteSpace(_txtStart.Tag as string))
                return "Start event is required.";
            // Stop is required only when auto-close is OFF; with auto-close on, every Start
            // is closed by the configured timeout so the Stop event is optional.
            if (!_chkAutoClose.Checked && string.IsNullOrWhiteSpace(_txtStop.Tag as string))
                return "Stop event is required (or enable auto-close to allow an empty Stop).";
            try { ColorTranslator.FromHtml(_ribbonColor); }
            catch { return "Ribbon color is invalid."; }
            return null;
        }

        public void UpdateItem(Item item)
        {
            if (item == null) return;
            item.Name = _txtName.Text;
            item.Properties["Enabled"] = _chkEnabled.Checked ? "Yes" : "No";
            item.Properties["RibbonColor"] = _ribbonColor;
            item.Properties["StartEvent"] = (_txtStart.Tag as string) ?? "";
            item.Properties["StopEvent"] = (_txtStop.Tag as string) ?? "";
            item.Properties["CameraIds"] = string.Join(";", _selectedCameras.Select(c => c.Id.ToString()));
            item.Properties["CameraNames"] = string.Join(";", _selectedCameras.Select(c => c.Name));
            item.Properties["AutoCloseEnabled"] = _chkAutoClose.Checked ? "Yes" : "No";
            item.Properties["AutoCloseSeconds"] = ((int)_numAutoCloseSeconds.Value).ToString();
            item.Properties["StartUseMarker"] = _chkStartUseMarker.Checked ? "Yes" : "No";
            item.Properties["StopUseMarker"] = _chkStopUseMarker.Checked ? "Yes" : "No";
            // Drop the legacy global UseMarkers key on save so old/new state can't disagree.
            item.Properties["UseMarkers"] = (_chkStartUseMarker.Checked || _chkStopUseMarker.Checked) ? "Yes" : "No";
            item.Properties["StartIcon"] = _startIconName ?? "";
            item.Properties["StopIcon"] = _stopIconName ?? "";
            item.Properties["StartIconColor"] = _startIconColorHex ?? "";
            item.Properties["StopIconColor"] = _stopIconColorHex ?? "";
        }
    }
}
