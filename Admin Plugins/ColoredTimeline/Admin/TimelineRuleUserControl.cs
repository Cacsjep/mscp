using CommunitySDK;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using VideoOS.Platform;
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

        private Label _lblEventsTable;
        private ListView _lvEvents;
        private ToolTip _tip;

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
            ApplyColorToSwatch();

            if (!EventTypeCache.IsLoaded)
                EventTypeCache.Loaded += OnEventTypesLoaded;
        }

        private void OnEventTypesLoaded(object sender, EventArgs e)
        {
            EventTypeCache.Loaded -= OnEventTypesLoaded;
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

            _grpCameras = new GroupBox { Text = "Cameras", Location = new Point(12, 95), Size = new Size(736, 175) };
            _lstCameras = new ListBox { Location = new Point(10, 22), Size = new Size(716, 110) };
            _btnAddCamera = new Button { Text = "Add Camera...", Location = new Point(10, 138), Size = new Size(110, 23) };
            _btnRemoveCamera = new Button { Text = "Remove", Location = new Point(126, 138), Size = new Size(80, 23) };
            _grpCameras.Controls.AddRange(new Control[] { _lstCameras, _btnAddCamera, _btnRemoveCamera });

            // Two side-by-side GroupBoxes for Start / Stop event
            _grpStart = new GroupBox { Text = "Start event", Location = new Point(12, 280), Size = new Size(362, 92) };
            _txtStart = new Label
            {
                Location = new Point(10, 22),
                Size = new Size(342, 32),
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = SystemColors.Window,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(4, 0, 4, 0),
                AutoEllipsis = true
            };
            _btnPickStart = new Button { Text = "Pick...", Location = new Point(196, 60), Size = new Size(75, 23) };
            _btnClearStart = new Button { Text = "Clear", Location = new Point(277, 60), Size = new Size(75, 23) };
            _grpStart.Controls.AddRange(new Control[] { _txtStart, _btnPickStart, _btnClearStart });

            _grpStop = new GroupBox { Text = "Stop event", Location = new Point(386, 280), Size = new Size(362, 92) };
            _txtStop = new Label
            {
                Location = new Point(10, 22),
                Size = new Size(342, 32),
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = SystemColors.Window,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(4, 0, 4, 0),
                AutoEllipsis = true
            };
            _btnPickStop = new Button { Text = "Pick...", Location = new Point(196, 60), Size = new Size(75, 23) };
            _btnClearStop = new Button { Text = "Clear", Location = new Point(277, 60), Size = new Size(75, 23) };
            _grpStop.Controls.AddRange(new Control[] { _txtStop, _btnPickStop, _btnClearStop });

            _lblEventsTable = new Label
            {
                Text = "Events available on the selected camera(s) (double-click to use as Start, Shift+double-click for Stop):",
                Location = new Point(12, 382),
                AutoSize = true,
                ForeColor = SystemColors.ControlDarkDark
            };

            _lvEvents = new ListView
            {
                Location = new Point(12, 402),
                Size = new Size(736, 200),
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                MultiSelect = false,
                HideSelection = false,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom
            };
            _lvEvents.Columns.Add("Camera", 180);
            _lvEvents.Columns.Add("Event", 540);

            Controls.AddRange(new Control[]
            {
                _lblName, _txtName, _chkEnabled,
                _lblColor, _colorSwatch, _btnPickColor,
                _grpCameras,
                _grpStart, _grpStop,
                _lblEventsTable, _lvEvents
            });

            Name = "TimelineRuleUserControl";
            Size = new Size(760, 615);
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
            target.Text = FormatEventForDisplay(fullName);
            // Show the raw event name in a tooltip so the user can always verify what was saved.
            try { _tip.SetToolTip(target, string.IsNullOrEmpty(fullName) ? null : fullName); }
            catch { }
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
            if (s.EndsWith("-1", StringComparison.Ordinal))
                s = s.Substring(0, s.Length - 2) + " (Rising)";
            else if (s.EndsWith("-0", StringComparison.Ordinal))
                s = s.Substring(0, s.Length - 2) + " (Falling)";
            else if (s.EndsWith("/1", StringComparison.Ordinal))
                s = s.Substring(0, s.Length - 2) + " (Rising)";
            else if (s.EndsWith("/0", StringComparison.Ordinal))
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
                        RefreshEventsTable();
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
                RefreshEventsTable();
                OnUserChange(sender, e);
            }
        }

        // Walks each selected camera and lists every TriggerEvent leaf under it.
        // Names like "tnsaxis:CameraApplicationPlatform/ObjectAnalytics/Device1ScenarioANY-0"
        // appear here exactly as they do in EventLog, so the user can pick the precise
        // string our filter will match against.
        private void RefreshEventsTable()
        {
            _lvEvents.BeginUpdate();
            try
            {
                _lvEvents.Items.Clear();
                foreach (var cam in _selectedCameras)
                {
                    Item camItem = null;
                    try { camItem = Configuration.Instance.GetItem(cam.Id, Kind.Camera); }
                    catch (Exception ex) { _log.Error($"GetItem(camera {cam.Id}) failed: {ex.Message}"); }
                    if (camItem == null) continue;

                    var leaves = new List<string>();
                    try { CollectTriggerEventLeaves(camItem, leaves); }
                    catch (Exception ex) { _log.Error($"Enumerate events for '{cam.Name}' failed: {ex.Message}"); }

                    foreach (var name in leaves.Distinct(StringComparer.OrdinalIgnoreCase)
                                                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
                    {
                        var row = new ListViewItem(cam.Name);
                        row.SubItems.Add(name);
                        row.Tag = name;
                        _lvEvents.Items.Add(row);
                    }
                }
            }
            finally
            {
                _lvEvents.EndUpdate();
            }
        }

        private static void CollectTriggerEventLeaves(Item parent, List<string> leaves)
        {
            foreach (var child in parent.GetChildren())
            {
                if (child == null) continue;
                if (child.FQID != null && child.FQID.Kind == Kind.TriggerEvent)
                {
                    if (child.FQID.FolderType == FolderType.No)
                    {
                        if (!string.IsNullOrEmpty(child.Name)) leaves.Add(child.Name);
                    }
                    else
                    {
                        CollectTriggerEventLeaves(child, leaves);
                    }
                }
                else
                {
                    // Camera children include other kinds (Microphone, Speaker, Output, ...). Recurse
                    // only into folders to find TriggerEvent subtrees attached deeper.
                    if (child.FQID != null && child.FQID.FolderType != FolderType.No)
                        CollectTriggerEventLeaves(child, leaves);
                }
            }
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
                RefreshEventsTable();
            }
            finally
            {
                _filling = false;
            }
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
                _lvEvents.Items.Clear();
            }
            finally
            {
                _filling = false;
            }
        }

        public string ValidateInput()
        {
            if (string.IsNullOrWhiteSpace(_txtName.Text))
                return "Name is required.";
            if (_selectedCameras.Count == 0)
                return "At least one camera must be selected.";
            if (string.IsNullOrWhiteSpace(_txtStart.Tag as string))
                return "Start event is required.";
            if (string.IsNullOrWhiteSpace(_txtStop.Tag as string))
                return "Stop event is required.";
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
        }
    }
}
