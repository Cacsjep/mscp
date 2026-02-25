using VideoOS.Platform.Client;

namespace Weather.Client
{
    public class WeatherViewItemManager : ViewItemManager
    {
        private const string LocationNamePropertyKey = "LocationName";
        private const string LatitudePropertyKey = "Latitude";
        private const string LongitudePropertyKey = "Longitude";
        private const string RefreshIntervalPropertyKey = "RefreshIntervalMinutes";
        private const string TemperatureUnitPropertyKey = "TemperatureUnit";

        public WeatherViewItemManager()
            : base("WeatherViewItemManager")
        {
        }

        public string LocationName
        {
            get => GetProperty(LocationNamePropertyKey) ?? string.Empty;
            set => SetProperty(LocationNamePropertyKey, value);
        }

        public string Latitude
        {
            get => GetProperty(LatitudePropertyKey) ?? string.Empty;
            set => SetProperty(LatitudePropertyKey, value);
        }

        public string Longitude
        {
            get => GetProperty(LongitudePropertyKey) ?? string.Empty;
            set => SetProperty(LongitudePropertyKey, value);
        }

        public string RefreshIntervalMinutes
        {
            get => GetProperty(RefreshIntervalPropertyKey) ?? "15";
            set => SetProperty(RefreshIntervalPropertyKey, value);
        }

        public string TemperatureUnit
        {
            get => GetProperty(TemperatureUnitPropertyKey) ?? "celsius";
            set => SetProperty(TemperatureUnitPropertyKey, value);
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
            return new WeatherViewItemWpfUserControl(this);
        }

        public override PropertiesWpfUserControl GeneratePropertiesWpfUserControl()
        {
            return new WeatherPropertiesWpfUserControl(this);
        }
    }
}
