using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using SystemStatus.Background;

namespace SystemStatus.Client
{
    /// <summary>
    /// One ready-to-render row for the Folder &amp; Role view item and its configuration preview. Carries
    /// display strings + brushes so the DataTemplate needs no value converters; the same shape serves
    /// both folder ("Devices") and role ("Users") rows.
    /// </summary>
    public sealed class CameraUserStatusDisplayRow
    {
        public string Name { get; set; }
        public string Online { get; set; }
        public string Total { get; set; }
        public string Suffix { get; set; }      // " Devices" / " Users" / "" when the suffix is hidden
        public string Unit { get; set; }        // "Devices" / "Users" - card caption (always present)
        public Brush CountBrush { get; set; }   // accent, or the offline-highlight color
        public Brush SubtleBrush { get; set; }
        public Brush TextBrush { get; set; }
        public double FontSize { get; set; }
        public double BigFontSize { get; set; } // dashboard card number size
        public double CardMinWidth { get; set; }
        public Thickness RowMargin { get; set; }
    }

    /// <summary>
    /// Snapshot of all Folder &amp; Role view-item settings, parsed once into typed values + frozen
    /// brushes. Built either from the saved manager (the live tile) or from the in-progress controls
    /// (the configuration preview). Turns the background plugin's raw folder/role status into display
    /// rows, applying the individual-selection filter, sort order, suffix, font size, density and the
    /// offline highlight.
    /// </summary>
    public sealed class CameraUserStatusSettings
    {
        public bool ShowServerPrefix = true;
        public bool IndividualSelection;
        public HashSet<string> SelectedFolders = NewSet();
        public HashSet<string> SelectedRoles = NewSet();
        public double TextSize = 13;
        public bool HighlightOffline;
        public bool ShowFolders = true;
        public bool ShowRoles = true;
        public bool FoldersFirst = true;
        public bool ShowSuffix = true;
        public bool Compact;
        public bool SortByOffline;
        public bool Dashboard;   // false = list rows, true = dashboard cards
        public double CardMinWidth = 150;

        public Brush CountBrush = Freeze("#FF2A8FE0");
        public Brush OfflineBrush = Freeze("#FFE0902A");

        private static readonly Brush SubtleBrush = Freeze("#FF9A9A9A");
        private static readonly Brush TextBrush = Freeze("#FFE6E6E6");

        public static CameraUserStatusSettings FromManager(CameraUserStatusViewItemManager m)
        {
            var s = new CameraUserStatusSettings();
            if (m == null) return s;
            s.ShowServerPrefix = !IsFalse(m.ShowServerPrefix);
            s.IndividualSelection = IsTrue(m.IndividualSelection);
            s.SelectedFolders = SplitSet(m.SelectedFolders);
            s.SelectedRoles = SplitSet(m.SelectedRoles);
            s.TextSize = ParseDouble(m.TextSize, 13);
            s.HighlightOffline = IsTrue(m.HighlightOffline);
            bool cameras = string.Equals(m.DisplayMode, "Cameras", StringComparison.OrdinalIgnoreCase);
            bool roles = string.Equals(m.DisplayMode, "Roles", StringComparison.OrdinalIgnoreCase);
            s.ShowFolders = !roles;    // "All" or "Cameras" show folders
            s.ShowRoles = !cameras;    // "All" or "Roles" show roles
            s.FoldersFirst = !IsFalse(m.FoldersFirst);
            s.ShowSuffix = !IsFalse(m.ShowSuffix);
            s.Compact = string.Equals(m.Density, "Compact", StringComparison.OrdinalIgnoreCase);
            s.SortByOffline = string.Equals(m.SortBy, "Offline", StringComparison.OrdinalIgnoreCase);
            s.Dashboard = string.Equals(m.RenderMode, "Dashboard", StringComparison.OrdinalIgnoreCase);
            s.CardMinWidth = ParseDouble(m.CardMinWidth, 150);
            s.CountBrush = Freeze(m.CountColor, "#FF2A8FE0");
            s.OfflineBrush = Freeze(m.OfflineColor, "#FFE0902A");
            return s;
        }

        public List<CameraUserStatusDisplayRow> BuildFolders(SystemStatusBackgroundPlugin plugin)
        {
            if (plugin == null) return new List<CameraUserStatusDisplayRow>();
            var src = plugin.GetFolderCameraStatus(ShowServerPrefix)
                .Where(r => !IndividualSelection || SelectedFolders.Contains(r.Folder))
                .Select(r => new Triple(r.Folder, r.Online, r.Total));
            return ToRows(src, " Devices");
        }

        public List<CameraUserStatusDisplayRow> BuildRoles(SystemStatusBackgroundPlugin plugin)
        {
            if (plugin == null) return new List<CameraUserStatusDisplayRow>();
            var src = plugin.GetRoleUserStatus()
                .Where(r => !IndividualSelection || SelectedRoles.Contains(r.Role))
                .Select(r => new Triple(r.Role, r.LoggedIn, r.Total));
            return ToRows(src, " Users");
        }

        private List<CameraUserStatusDisplayRow> ToRows(IEnumerable<Triple> src, string suffix)
        {
            // Name order already comes sorted from the plugin; only re-sort for "most offline first".
            IEnumerable<Triple> ordered = SortByOffline
                ? src.OrderByDescending(x => x.Total - x.Online)
                     .ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
                : src;

            var margin = Compact ? new Thickness(0, 2, 0, 2) : new Thickness(0, 5, 0, 5);
            var unit = suffix.Trim();
            return ordered.Select(x => new CameraUserStatusDisplayRow
            {
                Name = x.Name,
                Online = x.Online.ToString(CultureInfo.InvariantCulture),
                Total = x.Total.ToString(CultureInfo.InvariantCulture),
                Suffix = ShowSuffix ? suffix : string.Empty,
                Unit = unit,
                CountBrush = (HighlightOffline && x.Online < x.Total) ? OfflineBrush : CountBrush,
                SubtleBrush = SubtleBrush,
                TextBrush = TextBrush,
                FontSize = TextSize,
                BigFontSize = TextSize + 9,
                CardMinWidth = CardMinWidth,
                RowMargin = margin
            }).ToList();
        }

        // ── helpers ──────────────────────────────────────────────────────────
        private struct Triple
        {
            public readonly string Name; public readonly int Online; public readonly int Total;
            public Triple(string name, int online, int total) { Name = name; Online = online; Total = total; }
        }

        public static HashSet<string> NewSet() => new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public static string JoinSet(IEnumerable<string> items) =>
            items == null ? string.Empty : string.Join("\n", items);

        private static HashSet<string> SplitSet(string raw)
        {
            var set = NewSet();
            if (string.IsNullOrEmpty(raw)) return set;
            foreach (var p in raw.Split('\n'))
            {
                var t = p.Trim();
                if (t.Length > 0) set.Add(t);
            }
            return set;
        }

        private static bool IsTrue(string v) => string.Equals(v, "true", StringComparison.OrdinalIgnoreCase);
        private static bool IsFalse(string v) => string.Equals(v, "false", StringComparison.OrdinalIgnoreCase);

        private static double ParseDouble(string v, double fallback) =>
            double.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) && d > 0 ? d : fallback;

        private static Brush Freeze(string hex, string fallback = null)
        {
            var b = TryBrush(hex) ?? TryBrush(fallback) ?? new SolidColorBrush(Colors.DodgerBlue);
            if (b.CanFreeze) b.Freeze();
            return b;
        }

        private static SolidColorBrush TryBrush(string hex)
        {
            if (string.IsNullOrWhiteSpace(hex)) return null;
            try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }
            catch { return null; }
        }
    }
}
