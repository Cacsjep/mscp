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
        // Curated marker-relevant icon set for video-timeline events. Every glyph here is
        // something an operator might pin to a moment in time: tags, comments, pins, flags,
        // status, alerts, people / vehicle types, access events, and movement direction.
        // Filtered against FontAwesome5 v2.1.11 Solid set so every entry compiles. The grid
        // auto-sizes from Icons.Length so adding more is fine.
        private static readonly EFontAwesomeIcon[] Icons = new[]
        {
            // Tags / labels / bookmarks (canonical "marker" shapes)
            EFontAwesomeIcon.Solid_Tag,
            EFontAwesomeIcon.Solid_Tags,
            EFontAwesomeIcon.Solid_Thumbtack,
            EFontAwesomeIcon.Solid_Bookmark,
            EFontAwesomeIcon.Solid_StickyNote,
            EFontAwesomeIcon.Solid_Stamp,
            EFontAwesomeIcon.Solid_Highlighter,
            EFontAwesomeIcon.Solid_Marker,
            EFontAwesomeIcon.Solid_Star,
            EFontAwesomeIcon.Solid_StarHalf,
            EFontAwesomeIcon.Solid_StarHalfAlt,
            EFontAwesomeIcon.Solid_Award,
            EFontAwesomeIcon.Solid_Certificate,
            EFontAwesomeIcon.Solid_Hashtag,
            EFontAwesomeIcon.Solid_At,
            // Comments / notes / annotations
            EFontAwesomeIcon.Solid_Comment,
            EFontAwesomeIcon.Solid_CommentAlt,
            EFontAwesomeIcon.Solid_CommentDots,
            EFontAwesomeIcon.Solid_Comments,
            EFontAwesomeIcon.Solid_CommentSlash,
            EFontAwesomeIcon.Solid_QuoteLeft,
            EFontAwesomeIcon.Solid_QuoteRight,
            EFontAwesomeIcon.Solid_Edit,
            EFontAwesomeIcon.Solid_Pen,
            EFontAwesomeIcon.Solid_PenAlt,
            EFontAwesomeIcon.Solid_PenFancy,
            EFontAwesomeIcon.Solid_PenSquare,
            EFontAwesomeIcon.Solid_PencilAlt,
            EFontAwesomeIcon.Solid_FileSignature,
            EFontAwesomeIcon.Solid_FileAlt,
            EFontAwesomeIcon.Solid_Clipboard,
            EFontAwesomeIcon.Solid_ClipboardCheck,
            EFontAwesomeIcon.Solid_ClipboardList,
            EFontAwesomeIcon.Solid_List,
            EFontAwesomeIcon.Solid_ListAlt,
            EFontAwesomeIcon.Solid_ListOl,
            EFontAwesomeIcon.Solid_ListUl,
            EFontAwesomeIcon.Solid_Receipt,
            EFontAwesomeIcon.Solid_EnvelopeOpenText,
            // Notifications / alerts / status
            EFontAwesomeIcon.Solid_Bell,
            EFontAwesomeIcon.Solid_BellSlash,
            EFontAwesomeIcon.Solid_Bullhorn,
            EFontAwesomeIcon.Solid_Envelope,
            EFontAwesomeIcon.Solid_EnvelopeOpen,
            EFontAwesomeIcon.Solid_ExclamationTriangle,
            EFontAwesomeIcon.Solid_ExclamationCircle,
            EFontAwesomeIcon.Solid_Exclamation,
            EFontAwesomeIcon.Solid_InfoCircle,
            EFontAwesomeIcon.Solid_Info,
            EFontAwesomeIcon.Solid_QuestionCircle,
            EFontAwesomeIcon.Solid_Question,
            EFontAwesomeIcon.Solid_CheckCircle,
            EFontAwesomeIcon.Solid_Check,
            EFontAwesomeIcon.Solid_TimesCircle,
            EFontAwesomeIcon.Solid_Times,
            EFontAwesomeIcon.Solid_Ban,
            EFontAwesomeIcon.Solid_ShieldAlt,
            // Pins / location
            EFontAwesomeIcon.Solid_MapMarkerAlt,
            EFontAwesomeIcon.Solid_MapMarker,
            EFontAwesomeIcon.Solid_MapPin,
            EFontAwesomeIcon.Solid_Map,
            EFontAwesomeIcon.Solid_Route,
            EFontAwesomeIcon.Solid_LocationArrow,
            EFontAwesomeIcon.Solid_Compass,
            EFontAwesomeIcon.Solid_Anchor,
            EFontAwesomeIcon.Solid_Flag,
            EFontAwesomeIcon.Solid_FlagCheckered,
            EFontAwesomeIcon.Solid_Globe,
            // Geometric markers (good as small dots / badges)
            EFontAwesomeIcon.Solid_Circle,
            EFontAwesomeIcon.Solid_DotCircle,
            EFontAwesomeIcon.Solid_Square,
            EFontAwesomeIcon.Solid_Bullseye,
            EFontAwesomeIcon.Solid_Crosshairs,
            EFontAwesomeIcon.Solid_Asterisk,
            EFontAwesomeIcon.Solid_Plus,
            EFontAwesomeIcon.Solid_Minus,
            EFontAwesomeIcon.Solid_PlusCircle,
            EFontAwesomeIcon.Solid_MinusCircle,
            EFontAwesomeIcon.Solid_PlusSquare,
            EFontAwesomeIcon.Solid_MinusSquare,
            // Direction arrows / chevrons / carets (good for "moved this way" markers)
            EFontAwesomeIcon.Solid_ArrowUp,
            EFontAwesomeIcon.Solid_ArrowDown,
            EFontAwesomeIcon.Solid_ArrowLeft,
            EFontAwesomeIcon.Solid_ArrowRight,
            EFontAwesomeIcon.Solid_ArrowAltCircleUp,
            EFontAwesomeIcon.Solid_ArrowAltCircleDown,
            EFontAwesomeIcon.Solid_ArrowAltCircleLeft,
            EFontAwesomeIcon.Solid_ArrowAltCircleRight,
            EFontAwesomeIcon.Solid_ChevronUp,
            EFontAwesomeIcon.Solid_ChevronDown,
            EFontAwesomeIcon.Solid_ChevronLeft,
            EFontAwesomeIcon.Solid_ChevronRight,
            EFontAwesomeIcon.Solid_AngleUp,
            EFontAwesomeIcon.Solid_AngleDown,
            EFontAwesomeIcon.Solid_AngleLeft,
            EFontAwesomeIcon.Solid_AngleRight,
            EFontAwesomeIcon.Solid_CaretUp,
            EFontAwesomeIcon.Solid_CaretDown,
            EFontAwesomeIcon.Solid_CaretLeft,
            EFontAwesomeIcon.Solid_CaretRight,
            EFontAwesomeIcon.Solid_Reply,
            EFontAwesomeIcon.Solid_ReplyAll,
            EFontAwesomeIcon.Solid_Share,
            EFontAwesomeIcon.Solid_ShareSquare,
            EFontAwesomeIcon.Solid_Undo,
            EFontAwesomeIcon.Solid_Redo,
            // Playback / state
            EFontAwesomeIcon.Solid_Play,
            EFontAwesomeIcon.Solid_Stop,
            EFontAwesomeIcon.Solid_Pause,
            EFontAwesomeIcon.Solid_PlayCircle,
            EFontAwesomeIcon.Solid_StopCircle,
            EFontAwesomeIcon.Solid_PauseCircle,
            EFontAwesomeIcon.Solid_StepForward,
            EFontAwesomeIcon.Solid_StepBackward,
            EFontAwesomeIcon.Solid_FastForward,
            EFontAwesomeIcon.Solid_FastBackward,
            EFontAwesomeIcon.Solid_Forward,
            EFontAwesomeIcon.Solid_Backward,
            // Time / scheduling (when an event happened)
            EFontAwesomeIcon.Solid_Clock,
            EFontAwesomeIcon.Solid_Calendar,
            EFontAwesomeIcon.Solid_CalendarAlt,
            EFontAwesomeIcon.Solid_CalendarDay,
            EFontAwesomeIcon.Solid_CalendarCheck,
            EFontAwesomeIcon.Solid_CalendarTimes,
            // People (who was seen)
            EFontAwesomeIcon.Solid_Walking,
            EFontAwesomeIcon.Solid_Running,
            EFontAwesomeIcon.Solid_User,
            EFontAwesomeIcon.Solid_Users,
            EFontAwesomeIcon.Solid_UserPlus,
            EFontAwesomeIcon.Solid_UserMinus,
            EFontAwesomeIcon.Solid_UserShield,
            EFontAwesomeIcon.Solid_UserSecret,
            EFontAwesomeIcon.Solid_UserTimes,
            EFontAwesomeIcon.Solid_UserCheck,
            EFontAwesomeIcon.Solid_UserClock,
            EFontAwesomeIcon.Solid_UserTie,
            EFontAwesomeIcon.Solid_UserNurse,
            EFontAwesomeIcon.Solid_UserMd,
            EFontAwesomeIcon.Solid_UserGraduate,
            EFontAwesomeIcon.Solid_UserLock,
            EFontAwesomeIcon.Solid_UserSlash,
            EFontAwesomeIcon.Solid_UserFriends,
            EFontAwesomeIcon.Solid_UserCog,
            EFontAwesomeIcon.Solid_UserEdit,
            EFontAwesomeIcon.Solid_UserTag,
            EFontAwesomeIcon.Solid_Child,
            EFontAwesomeIcon.Solid_Mask,
            EFontAwesomeIcon.Solid_HardHat,
            EFontAwesomeIcon.Solid_Vest,
            // Vehicles (what was seen)
            EFontAwesomeIcon.Solid_Car,
            EFontAwesomeIcon.Solid_CarSide,
            EFontAwesomeIcon.Solid_CarCrash,
            EFontAwesomeIcon.Solid_Truck,
            EFontAwesomeIcon.Solid_TruckMoving,
            EFontAwesomeIcon.Solid_Bus,
            EFontAwesomeIcon.Solid_Motorcycle,
            EFontAwesomeIcon.Solid_Bicycle,
            EFontAwesomeIcon.Solid_Plane,
            EFontAwesomeIcon.Solid_Ambulance,
            EFontAwesomeIcon.Solid_Taxi,
            EFontAwesomeIcon.Solid_Road,
            // Surveillance / vision
            EFontAwesomeIcon.Solid_Eye,
            EFontAwesomeIcon.Solid_EyeSlash,
            EFontAwesomeIcon.Solid_Camera,
            EFontAwesomeIcon.Solid_CameraRetro,
            EFontAwesomeIcon.Solid_Video,
            EFontAwesomeIcon.Solid_VideoSlash,
            EFontAwesomeIcon.Solid_Film,
            EFontAwesomeIcon.Solid_Search,
            EFontAwesomeIcon.Solid_SearchPlus,
            EFontAwesomeIcon.Solid_SearchMinus,
            EFontAwesomeIcon.Solid_SearchLocation,
            EFontAwesomeIcon.Solid_Wifi,
            EFontAwesomeIcon.Solid_Signal,
            // Access / doors / identity
            EFontAwesomeIcon.Solid_Lock,
            EFontAwesomeIcon.Solid_LockOpen,
            EFontAwesomeIcon.Solid_Unlock,
            EFontAwesomeIcon.Solid_UnlockAlt,
            EFontAwesomeIcon.Solid_Key,
            EFontAwesomeIcon.Solid_DoorOpen,
            EFontAwesomeIcon.Solid_DoorClosed,
            EFontAwesomeIcon.Solid_SignInAlt,
            EFontAwesomeIcon.Solid_SignOutAlt,
            EFontAwesomeIcon.Solid_IdCard,
            EFontAwesomeIcon.Solid_IdCardAlt,
            EFontAwesomeIcon.Solid_IdBadge,
            EFontAwesomeIcon.Solid_AddressCard,
            EFontAwesomeIcon.Solid_Fingerprint,
            EFontAwesomeIcon.Solid_Passport,
            // Hazards / alarms
            EFontAwesomeIcon.Solid_Fire,
            EFontAwesomeIcon.Solid_FireAlt,
            EFontAwesomeIcon.Solid_FireExtinguisher,
            EFontAwesomeIcon.Solid_Smog,
            EFontAwesomeIcon.Solid_Tint,
            EFontAwesomeIcon.Solid_Bolt,
            EFontAwesomeIcon.Solid_Bomb,
            EFontAwesomeIcon.Solid_Radiation,
            EFontAwesomeIcon.Solid_Biohazard,
            // Hand pointers (annotation-style)
            EFontAwesomeIcon.Solid_HandPointUp,
            EFontAwesomeIcon.Solid_HandPointDown,
            EFontAwesomeIcon.Solid_HandPointLeft,
            EFontAwesomeIcon.Solid_HandPointRight,
            EFontAwesomeIcon.Solid_ThumbsUp,
            EFontAwesomeIcon.Solid_ThumbsDown,
            // Venue / context
            EFontAwesomeIcon.Solid_Building,
            EFontAwesomeIcon.Solid_Home,
            EFontAwesomeIcon.Solid_Warehouse,
            EFontAwesomeIcon.Solid_Industry,
            EFontAwesomeIcon.Solid_Phone,
            EFontAwesomeIcon.Solid_PhoneAlt,
            EFontAwesomeIcon.Solid_PhoneSlash,
            // Containers / parcels
            EFontAwesomeIcon.Solid_Box,
            EFontAwesomeIcon.Solid_BoxOpen,
            EFontAwesomeIcon.Solid_Boxes,
            EFontAwesomeIcon.Solid_Cube,
            EFontAwesomeIcon.Solid_Briefcase,
            EFontAwesomeIcon.Solid_PaperPlane
        };

        private const int Cols = 12;
        private const int CellSize = 44;
        // Cap the visible rows so the dialog fits on small screens; the grid scrolls
        // vertically when the icon set is taller than this.
        private const int MaxVisibleRows = 10;

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
            // The picker is for choosing an icon shape only - it always renders glyphs in
            // black so they are easy to compare. The actual marker color is chosen via the
            // separate "Color..." button on the rule editor.
            _previewColor = System.Windows.Media.Colors.Black;

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
            int totalRows = (Icons.Length + Cols - 1) / Cols;
            int visibleRows = Math.Min(totalRows, MaxVisibleRows);
            // Reserve space for the scrollbar when the grid scrolls, so cells don't get
            // hidden under it.
            int gridWidth = Cols * CellSize + (totalRows > visibleRows ? SystemInformation.VerticalScrollBarWidth + 2 : 0);
            int gridHeight = visibleRows * CellSize;
            ClientSize = new Size(gridWidth + 24, gridHeight + 60);

            _grid = new FlowLayoutPanel
            {
                Location = new Point(12, 12),
                Size = new Size(gridWidth, gridHeight),
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                AutoScroll = totalRows > visibleRows
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
