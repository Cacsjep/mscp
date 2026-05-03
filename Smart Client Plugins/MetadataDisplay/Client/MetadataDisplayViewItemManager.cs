using VideoOS.Platform.Client;

namespace MetadataDisplay.Client
{
    public class MetadataDisplayViewItemManager : ViewItemManager
    {
        // Channel
        private const string MetadataIdKey = "MetadataId";
        private const string MetadataNameKey = "MetadataName";

        // Extraction
        private const string TopicKey = "Topic";
        private const string TopicMatchModeKey = "TopicMatchMode";
        private const string SourceFiltersKey = "SourceFilters";
        private const string DataKeyKey = "DataKey";

        // Render type
        private const string RenderTypeKey = "RenderType";
        private const string TitleKey = "Title";
        private const string ShowTitleKey = "ShowTitle";
        private const string TitlePositionKey = "TitlePosition";
        private const string TitleFontSizeKey = "TitleFontSize";
        private const string TitleColorKey = "TitleColor";
        private const string UnitKey = "Unit";

        // Lamp
        private const string LampMapKey = "LampMap";

        // Number / Gauge thresholds
        private const string NumMinKey = "NumMin";
        private const string NumMaxKey = "NumMax";
        private const string NumDirectionKey = "NumDirection";
        private const string ColorOkKey = "ColorOk";
        private const string ColorWarnKey = "ColorWarn";
        private const string ColorBadKey = "ColorBad";

        // Gauge
        private const string GaugeRangeMinKey = "GaugeRangeMin";
        private const string GaugeRangeMaxKey = "GaugeRangeMax";
        private const string GaugeStyleKey = "GaugeStyle";
        private const string GaugeShowValueKey = "GaugeShowValue";
        private const string GaugeValueFontSizeKey = "GaugeValueFontSize";

        // Stale
        private const string StaleSecondsKey = "StaleSeconds";

        public MetadataDisplayViewItemManager()
            : base("MetadataDisplayViewItemManager")
        {
        }

        public string MetadataId
        {
            get => GetProperty(MetadataIdKey) ?? string.Empty;
            set => SetProperty(MetadataIdKey, value);
        }

        public string MetadataName
        {
            get => GetProperty(MetadataNameKey) ?? string.Empty;
            set => SetProperty(MetadataNameKey, value);
        }

        public string Topic
        {
            get => GetProperty(TopicKey) ?? string.Empty;
            set => SetProperty(TopicKey, value);
        }

        // Contains | Exact | EndsWith
        public string TopicMatchMode
        {
            get => GetProperty(TopicMatchModeKey) ?? "Exact";
            set => SetProperty(TopicMatchModeKey, value);
        }

        // Semicolon-joined: name1=val1;name2=val2
        public string SourceFilters
        {
            get => GetProperty(SourceFiltersKey) ?? string.Empty;
            set => SetProperty(SourceFiltersKey, value);
        }

        public string DataKey
        {
            get => GetProperty(DataKeyKey) ?? string.Empty;
            set => SetProperty(DataKeyKey, value);
        }

        // Lamp | Number | Gauge | Text
        public string RenderType
        {
            get => GetProperty(RenderTypeKey) ?? "Lamp";
            set => SetProperty(RenderTypeKey, value);
        }

        public string Title
        {
            get => GetProperty(TitleKey) ?? string.Empty;
            set => SetProperty(TitleKey, value);
        }

        // "true" / "false"
        public string ShowTitle
        {
            get => GetProperty(ShowTitleKey) ?? "true";
            set => SetProperty(ShowTitleKey, value);
        }

        // Left | Center | Right
        public string TitlePosition
        {
            get => GetProperty(TitlePositionKey) ?? "Center";
            set => SetProperty(TitlePositionKey, value);
        }

        public string TitleFontSize
        {
            get => GetProperty(TitleFontSizeKey) ?? "14";
            set => SetProperty(TitleFontSizeKey, value);
        }

        public string TitleColor
        {
            get => GetProperty(TitleColorKey) ?? "#FFCFD7DA";
            set => SetProperty(TitleColorKey, value);
        }

        public string Unit
        {
            get => GetProperty(UnitKey) ?? string.Empty;
            set => SetProperty(UnitKey, value);
        }

        // Pipe-separated rows: value=label:#RRGGBB|value=label:#RRGGBB
        public string LampMap
        {
            get => GetProperty(LampMapKey) ?? "0=Off:#777777|1=On:#3CB371";
            set => SetProperty(LampMapKey, value);
        }

        public string NumMin
        {
            get => GetProperty(NumMinKey) ?? string.Empty;
            set => SetProperty(NumMinKey, value);
        }

        public string NumMax
        {
            get => GetProperty(NumMaxKey) ?? string.Empty;
            set => SetProperty(NumMaxKey, value);
        }

        // HighIsBad | LowIsBad
        public string NumDirection
        {
            get => GetProperty(NumDirectionKey) ?? "HighIsBad";
            set => SetProperty(NumDirectionKey, value);
        }

        public string ColorOk
        {
            get => GetProperty(ColorOkKey) ?? "#3CB371";
            set => SetProperty(ColorOkKey, value);
        }

        public string ColorWarn
        {
            get => GetProperty(ColorWarnKey) ?? "#E69500";
            set => SetProperty(ColorWarnKey, value);
        }

        public string ColorBad
        {
            get => GetProperty(ColorBadKey) ?? "#D8392C";
            set => SetProperty(ColorBadKey, value);
        }

        public string GaugeRangeMin
        {
            get => GetProperty(GaugeRangeMinKey) ?? "0";
            set => SetProperty(GaugeRangeMinKey, value);
        }

        public string GaugeRangeMax
        {
            get => GetProperty(GaugeRangeMaxKey) ?? "100";
            set => SetProperty(GaugeRangeMaxKey, value);
        }

        // Arc180 | Arc270 | Bar
        public string GaugeStyle
        {
            get => GetProperty(GaugeStyleKey) ?? "Arc180";
            set => SetProperty(GaugeStyleKey, value);
        }

        // "true" / "false"
        public string GaugeShowValue
        {
            get => GetProperty(GaugeShowValueKey) ?? "true";
            set => SetProperty(GaugeShowValueKey, value);
        }

        public string GaugeValueFontSize
        {
            get => GetProperty(GaugeValueFontSizeKey) ?? "34";
            set => SetProperty(GaugeValueFontSizeKey, value);
        }

        public string StaleSeconds
        {
            get => GetProperty(StaleSecondsKey) ?? "0";
            set => SetProperty(StaleSecondsKey, value);
        }

        public void Save()
        {
            SaveProperties();
        }

        public override void PropertiesLoaded()
        {
        }

        public override ViewItemWpfUserControl GenerateViewItemWpfUserControl()
        {
            return new MetadataDisplayViewItemWpfUserControl(this);
        }

        public override PropertiesWpfUserControl GeneratePropertiesWpfUserControl()
        {
            return new MetadataDisplayPropertiesWpfUserControl(this);
        }
    }
}
