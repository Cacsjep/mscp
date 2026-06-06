using VideoOS.Platform.Client;

namespace SystemStatus.Client
{
    /// <summary>
    /// Per-instance manager for the Folder &amp; Role view item. Holds the display settings edited in the
    /// configuration window (selection, prefix, sections, styling) and generates the display + properties
    /// controls. All settings are persisted as strings; <see cref="CameraUserStatusSettings"/> parses them.
    /// </summary>
    public class CameraUserStatusViewItemManager : ViewItemManager
    {
        private const string ShowServerPrefixKey = "ShowServerPrefix";
        private const string IndividualSelectionKey = "IndividualSelection";
        private const string SelectedFoldersKey = "SelectedFolders";
        private const string SelectedRolesKey = "SelectedRoles";
        private const string TextSizeKey = "TextSize";
        private const string CountColorKey = "CountColor";
        private const string HighlightOfflineKey = "HighlightOffline";
        private const string OfflineColorKey = "OfflineColor";
        private const string DisplayModeKey = "DisplayMode";
        private const string RenderModeKey = "RenderMode";
        private const string CardMinWidthKey = "CardMinWidth";
        private const string FoldersFirstKey = "FoldersFirst";
        private const string ShowSuffixKey = "ShowSuffix";
        private const string DensityKey = "Density";
        private const string SortByKey = "SortBy";

        public CameraUserStatusViewItemManager() : base("CameraUserStatusViewItemManager") { }

        /// <summary>"true"/"false" - when false the leading "server / " segment is dropped from folders.</summary>
        public string ShowServerPrefix
        {
            get => GetProperty(ShowServerPrefixKey) ?? "true";
            set => SetProperty(ShowServerPrefixKey, value);
        }

        /// <summary>"true"/"false" - when true only the folders/roles in the selection lists are shown.</summary>
        public string IndividualSelection
        {
            get => GetProperty(IndividualSelectionKey) ?? "false";
            set => SetProperty(IndividualSelectionKey, value);
        }

        /// <summary>Newline-joined folder labels to show when <see cref="IndividualSelection"/> is on.</summary>
        public string SelectedFolders
        {
            get => GetProperty(SelectedFoldersKey) ?? string.Empty;
            set => SetProperty(SelectedFoldersKey, value);
        }

        /// <summary>Newline-joined role names to show when <see cref="IndividualSelection"/> is on.</summary>
        public string SelectedRoles
        {
            get => GetProperty(SelectedRolesKey) ?? string.Empty;
            set => SetProperty(SelectedRolesKey, value);
        }

        public string TextSize
        {
            get => GetProperty(TextSizeKey) ?? "13";
            set => SetProperty(TextSizeKey, value);
        }

        public string CountColor
        {
            get => GetProperty(CountColorKey) ?? "#FF2A8FE0";
            set => SetProperty(CountColorKey, value);
        }

        /// <summary>"true"/"false" - color the count with <see cref="OfflineColor"/> when online &lt; total.</summary>
        public string HighlightOffline
        {
            get => GetProperty(HighlightOfflineKey) ?? "false";
            set => SetProperty(HighlightOfflineKey, value);
        }

        public string OfflineColor
        {
            get => GetProperty(OfflineColorKey) ?? "#FFE0902A";
            set => SetProperty(OfflineColorKey, value);
        }

        /// <summary>All | Cameras | Roles - which sections to show.</summary>
        public string DisplayMode
        {
            get => GetProperty(DisplayModeKey) ?? "All";
            set => SetProperty(DisplayModeKey, value);
        }

        /// <summary>List | Dashboard - rows, or rectangular dashboard cards.</summary>
        public string RenderMode
        {
            get => GetProperty(RenderModeKey) ?? "List";
            set => SetProperty(RenderModeKey, value);
        }

        /// <summary>Dashboard card minimum width (px); cards grow past it to fit names.</summary>
        public string CardMinWidth
        {
            get => GetProperty(CardMinWidthKey) ?? "150";
            set => SetProperty(CardMinWidthKey, value);
        }

        /// <summary>"true"/"false" - folders section on top (otherwise roles on top).</summary>
        public string FoldersFirst
        {
            get => GetProperty(FoldersFirstKey) ?? "true";
            set => SetProperty(FoldersFirstKey, value);
        }

        /// <summary>"true"/"false" - show the "Devices"/"Users" suffix words after the count.</summary>
        public string ShowSuffix
        {
            get => GetProperty(ShowSuffixKey) ?? "true";
            set => SetProperty(ShowSuffixKey, value);
        }

        /// <summary>Comfortable | Compact - row spacing.</summary>
        public string Density
        {
            get => GetProperty(DensityKey) ?? "Comfortable";
            set => SetProperty(DensityKey, value);
        }

        /// <summary>Name | Offline - sort alphabetically, or most-offline first.</summary>
        public string SortBy
        {
            get => GetProperty(SortByKey) ?? "Name";
            set => SetProperty(SortByKey, value);
        }

        public void Save() => SaveProperties();

        public override ViewItemWpfUserControl GenerateViewItemWpfUserControl()
        {
            return new CameraUserStatusViewItemWpfUserControl(this);
        }

        public override PropertiesWpfUserControl GeneratePropertiesWpfUserControl()
        {
            return new CameraUserStatusPropertiesWpfUserControl(this);
        }
    }
}
