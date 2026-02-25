using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using FontAwesome5;
using VideoOS.Platform;
using VideoOS.Platform.Client;
using VideoOS.Platform.Messaging;

namespace Weather.Client
{
    public partial class WeatherViewItemWpfUserControl : ViewItemWpfUserControl
    {
        private static readonly HttpClient _httpClient = new HttpClient();

        private readonly WeatherViewItemManager _viewItemManager;
        private object _modeChangedReceiver;
        private DispatcherTimer _refreshTimer;
        private bool _hasData;
        private bool _isHorizontalLayout;

        public WeatherViewItemWpfUserControl(WeatherViewItemManager viewItemManager)
        {
            _viewItemManager = viewItemManager;
            InitializeComponent();
        }

        public override void Init()
        {
            _modeChangedReceiver = EnvironmentManager.Instance.RegisterReceiver(
                new MessageReceiver(OnModeChanged),
                new MessageIdFilter(MessageId.System.ModeChangedIndication));

            _refreshTimer = new DispatcherTimer();
            _refreshTimer.Tick += (s, e) => FetchWeather();

            // Initialize vertical layout
            ApplyWeatherLayout();

            ApplyMode(EnvironmentManager.Instance.Mode);
        }

        public override void Close()
        {
            if (_modeChangedReceiver != null)
            {
                EnvironmentManager.Instance.UnRegisterReceiver(_modeChangedReceiver);
                _modeChangedReceiver = null;
            }

            StopTimer();
        }

        #region Mode handling

        private void ApplyMode(Mode mode)
        {
            if (mode == Mode.ClientSetup)
            {
                StopTimer();
                weatherDisplay.Visibility = Visibility.Collapsed;
                loadingOverlay.Visibility = Visibility.Collapsed;
                errorOverlay.Visibility = Visibility.Collapsed;
                setupOverlay.Visibility = Visibility.Visible;
                UpdateSetupInfo();
            }
            else
            {
                setupOverlay.Visibility = Visibility.Collapsed;

                if (HasLocation())
                {
                    StartTimer();
                    if (!_hasData)
                    {
                        ShowLoading();
                        FetchWeather();
                    }
                    else
                    {
                        weatherDisplay.Visibility = Visibility.Visible;
                    }
                }
                else
                {
                    ShowError("Location not configured. Set it in the Properties panel (Setup mode).");
                }
            }
        }

        private bool HasLocation()
        {
            return !string.IsNullOrWhiteSpace(_viewItemManager.Latitude)
                && !string.IsNullOrWhiteSpace(_viewItemManager.Longitude);
        }

        #endregion

        #region Timer

        private void StartTimer()
        {
            if (int.TryParse(_viewItemManager.RefreshIntervalMinutes, out var minutes) && minutes > 0)
                _refreshTimer.Interval = TimeSpan.FromMinutes(minutes);
            else
                _refreshTimer.Interval = TimeSpan.FromMinutes(15);

            _refreshTimer.Start();
        }

        private void StopTimer()
        {
            _refreshTimer?.Stop();
        }

        #endregion

        #region Weather fetching

        private async void FetchWeather()
        {
            if (!HasLocation())
            {
                ShowError("Location not configured.");
                return;
            }

            var lat = _viewItemManager.Latitude.Replace(',', '.');
            var lon = _viewItemManager.Longitude.Replace(',', '.');
            var unit = _viewItemManager.TemperatureUnit;

            var url = $"https://api.open-meteo.com/v1/forecast?latitude={lat}&longitude={lon}"
                + $"&current=temperature_2m,relative_humidity_2m,apparent_temperature,is_day,weather_code,cloud_cover,pressure_msl,wind_speed_10m,wind_direction_10m"
                + $"&temperature_unit={unit}&timezone=auto";

            try
            {
                var json = await _httpClient.GetStringAsync(url);
                Dispatcher.Invoke(() => ParseAndDisplay(json));
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => ShowError($"Failed to fetch weather: {ex.Message}"));
            }
        }

        private void ParseAndDisplay(string json)
        {
            try
            {
                using (var doc = JsonDocument.Parse(json))
                {
                    if (doc.RootElement.TryGetProperty("error", out _))
                    {
                        var reason = doc.RootElement.TryGetProperty("reason", out var r) ? r.GetString() : "Unknown API error";
                        ShowError(reason);
                        return;
                    }

                    var current = doc.RootElement.GetProperty("current");
                    var units = doc.RootElement.GetProperty("current_units");

                    var temp = current.GetProperty("temperature_2m").GetDouble();
                    var tempUnit = units.GetProperty("temperature_2m").GetString();
                    var feelsLike = current.GetProperty("apparent_temperature").GetDouble();
                    var humidity = current.GetProperty("relative_humidity_2m").GetInt32();
                    var weatherCode = current.GetProperty("weather_code").GetInt32();
                    var isDay = current.GetProperty("is_day").GetInt32() == 1;
                    var cloudCover = current.GetProperty("cloud_cover").GetInt32();
                    var pressure = current.GetProperty("pressure_msl").GetDouble();
                    var windSpeed = current.GetProperty("wind_speed_10m").GetDouble();
                    var windDir = current.GetProperty("wind_direction_10m").GetDouble();
                    var windUnit = units.GetProperty("wind_speed_10m").GetString();

                    var wmo = GetWmoInfo(weatherCode, isDay);

                    // Main display
                    weatherIcon.Icon = wmo.Icon;
                    weatherIcon.Foreground = new SolidColorBrush(wmo.Color);
                    temperatureText.Text = $"{temp:F1}{tempUnit}";
                    descriptionText.Text = wmo.Description;
                    feelsLikeText.Text = $"Feels like {feelsLike:F1}{tempUnit}";

                    // Info rows
                    humidityText.Text = $"{humidity}%";
                    windText.Text = $"{windSpeed:F0} {windUnit} {DegreesToCompass(windDir)}";
                    cloudText.Text = $"{cloudCover}%";
                    pressureText.Text = $"{pressure:F0} hPa";

                    // Location and timestamp
                    var name = _viewItemManager.LocationName;
                    locationText.Text = string.IsNullOrWhiteSpace(name) ? $"{_viewItemManager.Latitude}, {_viewItemManager.Longitude}" : name;
                    updatedText.Text = $"Updated {DateTime.Now:HH:mm}";

                    _hasData = true;
                    weatherDisplay.Visibility = Visibility.Visible;
                    loadingOverlay.Visibility = Visibility.Collapsed;
                    errorOverlay.Visibility = Visibility.Collapsed;
                    setupOverlay.Visibility = Visibility.Collapsed;
                }
            }
            catch (Exception ex)
            {
                ShowError($"Failed to parse weather data: {ex.Message}");
            }
        }

        #endregion

        #region WMO Weather Codes

        private struct WmoInfo
        {
            public string Description;
            public EFontAwesomeIcon Icon;
            public Color Color;
        }

        private static WmoInfo GetWmoInfo(int code, bool isDay)
        {
            switch (code)
            {
                case 0:
                    return new WmoInfo
                    {
                        Description = "Clear sky",
                        Icon = isDay ? EFontAwesomeIcon.Solid_Sun : EFontAwesomeIcon.Solid_Moon,
                        Color = isDay ? ColorFromHex("#FFD700") : ColorFromHex("#B0C4DE")
                    };
                case 1:
                    return new WmoInfo
                    {
                        Description = "Mainly clear",
                        Icon = isDay ? EFontAwesomeIcon.Solid_Sun : EFontAwesomeIcon.Solid_Moon,
                        Color = isDay ? ColorFromHex("#FFD700") : ColorFromHex("#B0C4DE")
                    };
                case 2:
                    return new WmoInfo
                    {
                        Description = "Partly cloudy",
                        Icon = isDay ? EFontAwesomeIcon.Solid_CloudSun : EFontAwesomeIcon.Solid_CloudMoon,
                        Color = ColorFromHex("#87CEEB")
                    };
                case 3:
                    return new WmoInfo
                    {
                        Description = "Overcast",
                        Icon = EFontAwesomeIcon.Solid_Cloud,
                        Color = ColorFromHex("#A0A0A0")
                    };
                case 45:
                case 48:
                    return new WmoInfo
                    {
                        Description = code == 45 ? "Fog" : "Depositing rime fog",
                        Icon = EFontAwesomeIcon.Solid_Smog,
                        Color = ColorFromHex("#A0A0A0")
                    };
                case 51:
                    return new WmoInfo { Description = "Light drizzle", Icon = EFontAwesomeIcon.Solid_CloudRain, Color = ColorFromHex("#6B9BD2") };
                case 53:
                    return new WmoInfo { Description = "Moderate drizzle", Icon = EFontAwesomeIcon.Solid_CloudRain, Color = ColorFromHex("#6B9BD2") };
                case 55:
                    return new WmoInfo { Description = "Dense drizzle", Icon = EFontAwesomeIcon.Solid_CloudRain, Color = ColorFromHex("#6B9BD2") };
                case 56:
                    return new WmoInfo { Description = "Light freezing drizzle", Icon = EFontAwesomeIcon.Solid_CloudRain, Color = ColorFromHex("#A0D2F0") };
                case 57:
                    return new WmoInfo { Description = "Dense freezing drizzle", Icon = EFontAwesomeIcon.Solid_CloudRain, Color = ColorFromHex("#A0D2F0") };
                case 61:
                    return new WmoInfo { Description = "Slight rain", Icon = EFontAwesomeIcon.Solid_CloudShowersHeavy, Color = ColorFromHex("#4A89C8") };
                case 63:
                    return new WmoInfo { Description = "Moderate rain", Icon = EFontAwesomeIcon.Solid_CloudShowersHeavy, Color = ColorFromHex("#4A89C8") };
                case 65:
                    return new WmoInfo { Description = "Heavy rain", Icon = EFontAwesomeIcon.Solid_CloudShowersHeavy, Color = ColorFromHex("#4A89C8") };
                case 66:
                    return new WmoInfo { Description = "Light freezing rain", Icon = EFontAwesomeIcon.Solid_CloudShowersHeavy, Color = ColorFromHex("#A0D2F0") };
                case 67:
                    return new WmoInfo { Description = "Heavy freezing rain", Icon = EFontAwesomeIcon.Solid_CloudShowersHeavy, Color = ColorFromHex("#A0D2F0") };
                case 71:
                    return new WmoInfo { Description = "Slight snowfall", Icon = EFontAwesomeIcon.Solid_Snowflake, Color = ColorFromHex("#E0E8FF") };
                case 73:
                    return new WmoInfo { Description = "Moderate snowfall", Icon = EFontAwesomeIcon.Solid_Snowflake, Color = ColorFromHex("#E0E8FF") };
                case 75:
                    return new WmoInfo { Description = "Heavy snowfall", Icon = EFontAwesomeIcon.Solid_Snowflake, Color = ColorFromHex("#E0E8FF") };
                case 77:
                    return new WmoInfo { Description = "Snow grains", Icon = EFontAwesomeIcon.Solid_Snowflake, Color = ColorFromHex("#E0E8FF") };
                case 80:
                    return new WmoInfo
                    {
                        Description = "Slight rain showers",
                        Icon = isDay ? EFontAwesomeIcon.Solid_CloudSunRain : EFontAwesomeIcon.Solid_CloudMoonRain,
                        Color = ColorFromHex("#5B9BD5")
                    };
                case 81:
                    return new WmoInfo
                    {
                        Description = "Moderate rain showers",
                        Icon = isDay ? EFontAwesomeIcon.Solid_CloudSunRain : EFontAwesomeIcon.Solid_CloudMoonRain,
                        Color = ColorFromHex("#5B9BD5")
                    };
                case 82:
                    return new WmoInfo
                    {
                        Description = "Violent rain showers",
                        Icon = isDay ? EFontAwesomeIcon.Solid_CloudSunRain : EFontAwesomeIcon.Solid_CloudMoonRain,
                        Color = ColorFromHex("#5B9BD5")
                    };
                case 85:
                    return new WmoInfo { Description = "Slight snow showers", Icon = EFontAwesomeIcon.Solid_Snowflake, Color = ColorFromHex("#E0E8FF") };
                case 86:
                    return new WmoInfo { Description = "Heavy snow showers", Icon = EFontAwesomeIcon.Solid_Snowflake, Color = ColorFromHex("#E0E8FF") };
                case 95:
                    return new WmoInfo { Description = "Thunderstorm", Icon = EFontAwesomeIcon.Solid_Bolt, Color = ColorFromHex("#FFD700") };
                case 96:
                    return new WmoInfo { Description = "Thunderstorm with slight hail", Icon = EFontAwesomeIcon.Solid_Bolt, Color = ColorFromHex("#FFD700") };
                case 99:
                    return new WmoInfo { Description = "Thunderstorm with heavy hail", Icon = EFontAwesomeIcon.Solid_Bolt, Color = ColorFromHex("#FFD700") };
                default:
                    return new WmoInfo { Description = "Unknown", Icon = EFontAwesomeIcon.Solid_Cloud, Color = ColorFromHex("#A0A0A0") };
            }
        }

        private static Color ColorFromHex(string hex)
        {
            return (Color)ColorConverter.ConvertFromString(hex);
        }

        #endregion

        #region Responsive Layout

        private void OnSizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateResponsiveLayout(e.NewSize.Width, e.NewSize.Height);
        }

        private void UpdateResponsiveLayout(double width, double height)
        {
            if (width <= 0 || height <= 0)
                return;

            // Determine layout mode: horizontal when wide, vertical when narrow/square
            bool useHorizontal = width > height * 1.4 && width > 400;

            if (useHorizontal != _isHorizontalLayout)
            {
                _isHorizontalLayout = useHorizontal;
                ApplyWeatherLayout();
            }

            // Calculate scale based on layout mode
            double scale;
            if (_isHorizontalLayout)
            {
                // Horizontal: content is ~420px wide, ~220px tall at scale 1
                scale = Math.Min(width / 480.0, height / 260.0);
            }
            else
            {
                // Vertical: content is ~300px wide, ~340px tall at scale 1
                scale = Math.Min(width / 320.0, height / 360.0);
            }
            scale = Math.Max(0.5, Math.Min(2.0, scale * 0.9));

            // Apply scale to all overlays
            weatherScale.ScaleX = weatherScale.ScaleY = scale;
            loadingScale.ScaleX = loadingScale.ScaleY = scale;
            errorScale.ScaleX = errorScale.ScaleY = scale;
            setupScale.ScaleX = setupScale.ScaleY = scale;

            // Hide secondary elements when too small to read
            var minDim = Math.Min(width, height);
            feelsLikeText.Visibility = minDim > 200 ? Visibility.Visible : Visibility.Collapsed;
            infoGrid.Visibility = minDim > 260 ? Visibility.Visible : Visibility.Collapsed;
            updatedText.Visibility = minDim > 200 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ApplyWeatherLayout()
        {
            weatherContent.RowDefinitions.Clear();
            weatherContent.ColumnDefinitions.Clear();

            if (_isHorizontalLayout)
            {
                // Horizontal: icon left, details right
                weatherContent.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = System.Windows.GridLength.Auto });
                weatherContent.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = System.Windows.GridLength.Auto });

                Grid.SetRow(weatherIcon, 0);
                Grid.SetColumn(weatherIcon, 0);
                Grid.SetRow(detailsPanel, 0);
                Grid.SetColumn(detailsPanel, 1);

                weatherIcon.Margin = new Thickness(0, 0, 24, 0);
                weatherIcon.VerticalAlignment = VerticalAlignment.Center;

                temperatureText.HorizontalAlignment = HorizontalAlignment.Left;
                descriptionText.HorizontalAlignment = HorizontalAlignment.Left;
                feelsLikeText.HorizontalAlignment = HorizontalAlignment.Left;
                infoGrid.HorizontalAlignment = HorizontalAlignment.Left;
                locationText.HorizontalAlignment = HorizontalAlignment.Left;
                updatedText.HorizontalAlignment = HorizontalAlignment.Left;
            }
            else
            {
                // Vertical: icon top, details below
                weatherContent.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto });
                weatherContent.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto });

                Grid.SetRow(weatherIcon, 0);
                Grid.SetColumn(weatherIcon, 0);
                Grid.SetRow(detailsPanel, 1);
                Grid.SetColumn(detailsPanel, 0);

                weatherIcon.Margin = new Thickness(0, 0, 0, 8);
                weatherIcon.VerticalAlignment = VerticalAlignment.Bottom;

                temperatureText.HorizontalAlignment = HorizontalAlignment.Center;
                descriptionText.HorizontalAlignment = HorizontalAlignment.Center;
                feelsLikeText.HorizontalAlignment = HorizontalAlignment.Center;
                infoGrid.HorizontalAlignment = HorizontalAlignment.Center;
                locationText.HorizontalAlignment = HorizontalAlignment.Center;
                updatedText.HorizontalAlignment = HorizontalAlignment.Center;
            }
        }

        #endregion

        #region Helpers

        private static string DegreesToCompass(double degrees)
        {
            var dirs = new[] { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
            var index = (int)Math.Round(degrees / 45.0) % 8;
            return dirs[index];
        }

        private void ShowLoading()
        {
            var name = _viewItemManager.LocationName;
            loadingNameText.Text = string.IsNullOrWhiteSpace(name) ? "Weather" : name;
            weatherDisplay.Visibility = Visibility.Collapsed;
            loadingOverlay.Visibility = Visibility.Visible;
            errorOverlay.Visibility = Visibility.Collapsed;
            setupOverlay.Visibility = Visibility.Collapsed;
        }

        private void ShowError(string message)
        {
            errorText.Text = message;
            weatherDisplay.Visibility = Visibility.Collapsed;
            loadingOverlay.Visibility = Visibility.Collapsed;
            errorOverlay.Visibility = Visibility.Visible;
            setupOverlay.Visibility = Visibility.Collapsed;
        }

        private void UpdateSetupInfo()
        {
            var name = _viewItemManager.LocationName;
            setupNameText.Text = string.IsNullOrWhiteSpace(name) ? "Weather" : name;

            var lat = _viewItemManager.Latitude;
            var lon = _viewItemManager.Longitude;
            if (!string.IsNullOrWhiteSpace(lat) && !string.IsNullOrWhiteSpace(lon))
                setupInfoText.Text = $"{lat}, {lon}";
            else
                setupInfoText.Text = "No location configured";
        }

        #endregion

        #region Smart Client Events

        private object OnModeChanged(Message message, FQID destination, FQID sender)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                ApplyMode((Mode)message.Data);
            }));
            return null;
        }

        private void OnMouseLeftUp(object sender, MouseButtonEventArgs e)
        {
            FireClickEvent();
        }

        private void OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            FireDoubleClickEvent();
        }

        #endregion

        public override bool Maximizable => true;

        public override bool Selectable => true;

        public override bool ShowToolbar => false;
    }
}
