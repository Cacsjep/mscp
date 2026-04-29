using ColoredTimeline.Background;
using FontAwesome5;
using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ColoredTimeline.Admin
{
    // Curated grid of FontAwesome Solid icons useful for surveillance-related events
    // (motion, vehicles, people, alarms, doors, etc.). Click selects, OK/double-click
    // commits. Returns the chosen EFontAwesomeIcon as a string ("Solid_Play").
    internal class IconPickerDialog : Form
    {
        // Curated 32-icon set. Add/remove as needed - keep them visually distinguishable
        // at 16 px so users can tell them apart on the timeline.
        private static readonly EFontAwesomeIcon[] Icons = new[]
        {
            EFontAwesomeIcon.Solid_Play,
            EFontAwesomeIcon.Solid_Stop,
            EFontAwesomeIcon.Solid_Pause,
            EFontAwesomeIcon.Solid_Bell,
            EFontAwesomeIcon.Solid_BellSlash,
            EFontAwesomeIcon.Solid_Bullhorn,
            EFontAwesomeIcon.Solid_ExclamationTriangle,
            EFontAwesomeIcon.Solid_ExclamationCircle,
            EFontAwesomeIcon.Solid_InfoCircle,
            EFontAwesomeIcon.Solid_CheckCircle,
            EFontAwesomeIcon.Solid_TimesCircle,
            EFontAwesomeIcon.Solid_Ban,
            EFontAwesomeIcon.Solid_Walking,
            EFontAwesomeIcon.Solid_Running,
            EFontAwesomeIcon.Solid_Car,
            EFontAwesomeIcon.Solid_Truck,
            EFontAwesomeIcon.Solid_Motorcycle,
            EFontAwesomeIcon.Solid_Bicycle,
            EFontAwesomeIcon.Solid_Eye,
            EFontAwesomeIcon.Solid_EyeSlash,
            EFontAwesomeIcon.Solid_Camera,
            EFontAwesomeIcon.Solid_Video,
            EFontAwesomeIcon.Solid_VideoSlash,
            EFontAwesomeIcon.Solid_Lock,
            EFontAwesomeIcon.Solid_LockOpen,
            EFontAwesomeIcon.Solid_DoorOpen,
            EFontAwesomeIcon.Solid_DoorClosed,
            EFontAwesomeIcon.Solid_Fire,
            EFontAwesomeIcon.Solid_Lightbulb,
            EFontAwesomeIcon.Solid_Flag,
            EFontAwesomeIcon.Solid_MapMarkerAlt,
            EFontAwesomeIcon.Solid_Bolt
        };

        private const int Cols = 8;
        private const int CellSize = 44;

        public EFontAwesomeIcon SelectedIcon { get; private set; }

        private EFontAwesomeIcon _initial;
        private System.Windows.Media.Color _previewColor;
        private FlowLayoutPanel _grid;
        private Button _btnOk;
        private Button _btnCancel;

        public IconPickerDialog(EFontAwesomeIcon initial, string previewHexColor)
        {
            _initial = initial;
            SelectedIcon = initial;
            _previewColor = MarkerIconRenderer.ParseColor(previewHexColor,
                System.Windows.Media.Color.FromRgb(0x1E, 0x88, 0xE5));

            Build();
        }

        private void Build()
        {
            Text = "Pick icon";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(Cols * CellSize + 24, ((Icons.Length + Cols - 1) / Cols) * CellSize + 60);

            _grid = new FlowLayoutPanel
            {
                Location = new Point(12, 12),
                Size = new Size(Cols * CellSize, ((Icons.Length + Cols - 1) / Cols) * CellSize),
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                AutoScroll = false
            };

            foreach (var icon in Icons)
            {
                // FlatStyle.System uses native Win32 BS_PUSHBUTTON which doesn't paint
                // the Image property on themed buttons - only the selected one (which we
                // switched to Flat) was visible. FlatStyle.Standard honors Image.
                var btn = new Button
                {
                    Width = CellSize - 4,
                    Height = CellSize - 4,
                    Margin = new Padding(2),
                    FlatStyle = FlatStyle.Standard,
                    ImageAlign = ContentAlignment.MiddleCenter,
                    Image = RenderForButton(icon, _previewColor, 24),
                    Tag = icon
                };
                btn.Click += (s, e) =>
                {
                    SelectedIcon = (EFontAwesomeIcon)((Button)s).Tag;
                    HighlightSelected();
                };
                btn.DoubleClick += (s, e) =>
                {
                    SelectedIcon = (EFontAwesomeIcon)((Button)s).Tag;
                    DialogResult = DialogResult.OK;
                    Close();
                };
                _grid.Controls.Add(btn);
            }

            _btnOk = new Button
            {
                Text = "OK",
                DialogResult = DialogResult.OK,
                Location = new Point(ClientSize.Width - 168, ClientSize.Height - 36),
                Size = new Size(75, 26)
            };
            _btnCancel = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Location = new Point(ClientSize.Width - 87, ClientSize.Height - 36),
                Size = new Size(75, 26)
            };
            AcceptButton = _btnOk;
            CancelButton = _btnCancel;

            Controls.AddRange(new Control[] { _grid, _btnOk, _btnCancel });
            HighlightSelected();
        }

        private void HighlightSelected()
        {
            foreach (Control c in _grid.Controls)
            {
                if (c is Button b && b.Tag is EFontAwesomeIcon icon)
                {
                    if (icon == SelectedIcon)
                    {
                        b.FlatStyle = FlatStyle.Flat;
                        b.FlatAppearance.BorderColor = SystemColors.Highlight;
                        b.FlatAppearance.BorderSize = 2;
                    }
                    else
                    {
                        b.FlatStyle = FlatStyle.Standard;
                    }
                }
            }
        }

        // Convert the WPF-rendered BitmapSource to a System.Drawing.Image so a WinForms
        // Button can host it. Done via a PNG round-trip - one-shot at dialog open, fine.
        // The MemoryStream is intentionally NOT disposed: System.Drawing.Bitmap keeps a
        // reference to its source stream and the image goes blank if we dispose it. Same
        // gotcha noted in CommunitySDK/PluginIcon.cs.
        private static Image RenderForButton(EFontAwesomeIcon icon, System.Windows.Media.Color color, int size)
        {
            var bs = MarkerIconRenderer.Render(icon, color, size);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(bs));
            var ms = new MemoryStream();
            enc.Save(ms);
            ms.Position = 0;
            return new Bitmap(ms);
        }
    }
}
