using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FontAwesome5;

namespace MetadataDisplay.Client
{
    // WPF icon picker for the lamp render type. Curated FontAwesome Solid set with
    // search-as-you-type filtering. Returns the chosen icon as an EFontAwesomeIcon
    // value or EFontAwesomeIcon.None when the user picks "None".
    public partial class IconPickerWindow : Window
    {
        // Curated marker-relevant icon set: status, IO, vehicles, people, hazards,
        // doors, etc. Filtered against FontAwesome5 v2.1.11 Solid set.
        private static readonly EFontAwesomeIcon[] Icons = new[]
        {
            // Generic / status
            EFontAwesomeIcon.Solid_CheckCircle,
            EFontAwesomeIcon.Solid_Check,
            EFontAwesomeIcon.Solid_TimesCircle,
            EFontAwesomeIcon.Solid_Times,
            EFontAwesomeIcon.Solid_ExclamationTriangle,
            EFontAwesomeIcon.Solid_ExclamationCircle,
            EFontAwesomeIcon.Solid_Exclamation,
            EFontAwesomeIcon.Solid_InfoCircle,
            EFontAwesomeIcon.Solid_QuestionCircle,
            EFontAwesomeIcon.Solid_Ban,
            EFontAwesomeIcon.Solid_ShieldAlt,
            EFontAwesomeIcon.Solid_Bell,
            EFontAwesomeIcon.Solid_BellSlash,
            EFontAwesomeIcon.Solid_Bullhorn,
            EFontAwesomeIcon.Solid_Bookmark,
            EFontAwesomeIcon.Solid_Star,
            EFontAwesomeIcon.Solid_Flag,
            EFontAwesomeIcon.Solid_Tag,
            EFontAwesomeIcon.Solid_Tags,
            EFontAwesomeIcon.Solid_Heart,
            // Geometric / lamp-style
            EFontAwesomeIcon.Solid_Circle,
            EFontAwesomeIcon.Solid_DotCircle,
            EFontAwesomeIcon.Solid_Square,
            EFontAwesomeIcon.Solid_Bullseye,
            EFontAwesomeIcon.Solid_Crosshairs,
            EFontAwesomeIcon.Solid_Asterisk,
            EFontAwesomeIcon.Solid_PowerOff,
            EFontAwesomeIcon.Solid_Lightbulb,
            EFontAwesomeIcon.Solid_Sun,
            EFontAwesomeIcon.Solid_Moon,
            EFontAwesomeIcon.Solid_Bolt,
            // IO / electrical
            EFontAwesomeIcon.Solid_Plug,
            EFontAwesomeIcon.Solid_BatteryEmpty,
            EFontAwesomeIcon.Solid_BatteryQuarter,
            EFontAwesomeIcon.Solid_BatteryHalf,
            EFontAwesomeIcon.Solid_BatteryThreeQuarters,
            EFontAwesomeIcon.Solid_BatteryFull,
            EFontAwesomeIcon.Solid_ToggleOn,
            EFontAwesomeIcon.Solid_ToggleOff,
            EFontAwesomeIcon.Solid_Plus,
            EFontAwesomeIcon.Solid_Minus,
            EFontAwesomeIcon.Solid_Microchip,
            EFontAwesomeIcon.Solid_Memory,
            EFontAwesomeIcon.Solid_Hdd,
            EFontAwesomeIcon.Solid_NetworkWired,
            EFontAwesomeIcon.Solid_Wifi,
            EFontAwesomeIcon.Solid_Signal,
            EFontAwesomeIcon.Solid_BroadcastTower,
            // Surveillance
            EFontAwesomeIcon.Solid_Eye,
            EFontAwesomeIcon.Solid_EyeSlash,
            EFontAwesomeIcon.Solid_Camera,
            EFontAwesomeIcon.Solid_Video,
            EFontAwesomeIcon.Solid_VideoSlash,
            EFontAwesomeIcon.Solid_Film,
            EFontAwesomeIcon.Solid_Search,
            EFontAwesomeIcon.Solid_SearchLocation,
            EFontAwesomeIcon.Solid_LocationArrow,
            EFontAwesomeIcon.Solid_MapMarkerAlt,
            EFontAwesomeIcon.Solid_MapPin,
            EFontAwesomeIcon.Solid_Compass,
            EFontAwesomeIcon.Solid_Globe,
            // People
            EFontAwesomeIcon.Solid_Walking,
            EFontAwesomeIcon.Solid_Running,
            EFontAwesomeIcon.Solid_User,
            EFontAwesomeIcon.Solid_Users,
            EFontAwesomeIcon.Solid_UserShield,
            EFontAwesomeIcon.Solid_UserSecret,
            EFontAwesomeIcon.Solid_UserCheck,
            EFontAwesomeIcon.Solid_UserClock,
            EFontAwesomeIcon.Solid_UserTie,
            EFontAwesomeIcon.Solid_UserNurse,
            EFontAwesomeIcon.Solid_UserMd,
            EFontAwesomeIcon.Solid_UserLock,
            EFontAwesomeIcon.Solid_UserSlash,
            EFontAwesomeIcon.Solid_Child,
            EFontAwesomeIcon.Solid_Mask,
            EFontAwesomeIcon.Solid_HardHat,
            EFontAwesomeIcon.Solid_Vest,
            // Vehicles
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
            // Doors / access
            EFontAwesomeIcon.Solid_Lock,
            EFontAwesomeIcon.Solid_LockOpen,
            EFontAwesomeIcon.Solid_Unlock,
            EFontAwesomeIcon.Solid_Key,
            EFontAwesomeIcon.Solid_DoorOpen,
            EFontAwesomeIcon.Solid_DoorClosed,
            EFontAwesomeIcon.Solid_SignInAlt,
            EFontAwesomeIcon.Solid_SignOutAlt,
            EFontAwesomeIcon.Solid_IdCard,
            EFontAwesomeIcon.Solid_IdBadge,
            EFontAwesomeIcon.Solid_AddressCard,
            EFontAwesomeIcon.Solid_Fingerprint,
            EFontAwesomeIcon.Solid_Passport,
            // Hazards
            EFontAwesomeIcon.Solid_Fire,
            EFontAwesomeIcon.Solid_FireAlt,
            EFontAwesomeIcon.Solid_FireExtinguisher,
            EFontAwesomeIcon.Solid_Smog,
            EFontAwesomeIcon.Solid_Tint,
            EFontAwesomeIcon.Solid_Bomb,
            EFontAwesomeIcon.Solid_Radiation,
            EFontAwesomeIcon.Solid_Biohazard,
            EFontAwesomeIcon.Solid_Skull,
            EFontAwesomeIcon.Solid_SkullCrossbones,
            // Time
            EFontAwesomeIcon.Solid_Clock,
            EFontAwesomeIcon.Solid_Stopwatch,
            EFontAwesomeIcon.Solid_Hourglass,
            EFontAwesomeIcon.Solid_Calendar,
            EFontAwesomeIcon.Solid_CalendarCheck,
            EFontAwesomeIcon.Solid_CalendarTimes,
            // Comm / venue
            EFontAwesomeIcon.Solid_Phone,
            EFontAwesomeIcon.Solid_PhoneSlash,
            EFontAwesomeIcon.Solid_Envelope,
            EFontAwesomeIcon.Solid_Comment,
            EFontAwesomeIcon.Solid_Comments,
            EFontAwesomeIcon.Solid_Building,
            EFontAwesomeIcon.Solid_Home,
            EFontAwesomeIcon.Solid_Warehouse,
            EFontAwesomeIcon.Solid_Industry,
            EFontAwesomeIcon.Solid_Box,
            EFontAwesomeIcon.Solid_BoxOpen,
            EFontAwesomeIcon.Solid_Cube,
            // Direction
            EFontAwesomeIcon.Solid_ArrowUp,
            EFontAwesomeIcon.Solid_ArrowDown,
            EFontAwesomeIcon.Solid_ArrowLeft,
            EFontAwesomeIcon.Solid_ArrowRight,
            EFontAwesomeIcon.Solid_ChevronUp,
            EFontAwesomeIcon.Solid_ChevronDown,
            EFontAwesomeIcon.Solid_ChevronLeft,
            EFontAwesomeIcon.Solid_ChevronRight,
            EFontAwesomeIcon.Solid_Play,
            EFontAwesomeIcon.Solid_Pause,
            EFontAwesomeIcon.Solid_Stop,
            EFontAwesomeIcon.Solid_ThumbsUp,
            EFontAwesomeIcon.Solid_ThumbsDown,
            EFontAwesomeIcon.Solid_Thermometer,
            EFontAwesomeIcon.Solid_ThermometerHalf,
            EFontAwesomeIcon.Solid_TachometerAlt,
            EFontAwesomeIcon.Solid_Wrench,
            EFontAwesomeIcon.Solid_Cog,
            EFontAwesomeIcon.Solid_Cogs,
            EFontAwesomeIcon.Solid_Tools,
        };

        public EFontAwesomeIcon SelectedIcon { get; private set; }

        private readonly Dictionary<EFontAwesomeIcon, Button> _buttons = new Dictionary<EFontAwesomeIcon, Button>();

        public IconPickerWindow(EFontAwesomeIcon initial)
        {
            InitializeComponent();
            SelectedIcon = initial;
            BuildGrid();
            UpdateSelectedText();
            Loaded += (s, e) => searchBox.Focus();
        }

        private void BuildGrid()
        {
            iconGrid.Items.Clear();
            _buttons.Clear();
            foreach (var icon in Icons)
            {
                var btn = new Button
                {
                    Tag = icon,
                    ToolTip = icon.ToString(),
                    Style = (Style)FindResource("IconButton"),
                    Content = new ImageAwesome
                    {
                        Icon = icon,
                        Width = 22,
                        Height = 22,
                        Foreground = new SolidColorBrush(Color.FromRgb(0xD0, 0xD0, 0xD0)),
                    },
                };
                btn.Click += (s, e) =>
                {
                    SelectedIcon = (EFontAwesomeIcon)((Button)s).Tag;
                    UpdateSelectedText();
                    HighlightSelected();
                };
                btn.MouseDoubleClick += (s, e) =>
                {
                    SelectedIcon = (EFontAwesomeIcon)((Button)s).Tag;
                    DialogResult = true;
                    Close();
                };
                _buttons[icon] = btn;
                iconGrid.Items.Add(btn);
            }
            HighlightSelected();
        }

        private void HighlightSelected()
        {
            foreach (var kv in _buttons)
            {
                kv.Value.BorderBrush = kv.Key == SelectedIcon
                    ? new SolidColorBrush(Color.FromRgb(0x33, 0x99, 0xFF))
                    : new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44));
                kv.Value.BorderThickness = kv.Key == SelectedIcon ? new Thickness(2) : new Thickness(1);
            }
        }

        private void UpdateSelectedText()
        {
            selectedText.Text = SelectedIcon == EFontAwesomeIcon.None
                ? "Selected: (none — colored circle)"
                : "Selected: " + SelectedIcon;
        }

        private void OnSearchChanged(object sender, TextChangedEventArgs e)
        {
            var q = (searchBox.Text ?? "").Trim();
            foreach (var kv in _buttons)
            {
                var match = string.IsNullOrEmpty(q)
                    || kv.Key.ToString().IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;
                kv.Value.Visibility = match ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void OnOk(object sender, RoutedEventArgs e) { DialogResult = true; Close(); }
        private void OnCancel(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
        private void OnClear(object sender, RoutedEventArgs e)
        {
            SelectedIcon = EFontAwesomeIcon.None;
            DialogResult = true;
            Close();
        }
    }
}
