using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using VideoOS.Platform.Client;

namespace Weather.Client
{
    public partial class WeatherPropertiesWpfUserControl : PropertiesWpfUserControl
    {
        private static readonly HttpClient _httpClient = new HttpClient();
        private readonly WeatherViewItemManager _viewItemManager;
        private List<GeoResult> _searchResults = new List<GeoResult>();

        public WeatherPropertiesWpfUserControl(WeatherViewItemManager viewItemManager)
        {
            _viewItemManager = viewItemManager;
            InitializeComponent();
        }

        public override void Init()
        {
            locationNameTextBox.Text = _viewItemManager.LocationName;
            latitudeTextBox.Text = _viewItemManager.Latitude;
            longitudeTextBox.Text = _viewItemManager.Longitude;
            refreshTextBox.Text = _viewItemManager.RefreshIntervalMinutes;

            var unit = _viewItemManager.TemperatureUnit;
            foreach (ComboBoxItem item in unitComboBox.Items)
            {
                if ((string)item.Tag == unit)
                {
                    unitComboBox.SelectedItem = item;
                    break;
                }
            }
            if (unitComboBox.SelectedItem == null)
                unitComboBox.SelectedIndex = 0;
        }

        public override void Close()
        {
            _viewItemManager.LocationName = locationNameTextBox.Text.Trim();
            _viewItemManager.Latitude = latitudeTextBox.Text.Trim();
            _viewItemManager.Longitude = longitudeTextBox.Text.Trim();
            _viewItemManager.RefreshIntervalMinutes = refreshTextBox.Text.Trim();

            var selectedItem = unitComboBox.SelectedItem as ComboBoxItem;
            _viewItemManager.TemperatureUnit = selectedItem != null ? (string)selectedItem.Tag : "celsius";

            _viewItemManager.Save();
        }

        private async void SearchButton_Click(object sender, RoutedEventArgs e)
        {
            var query = searchTextBox.Text.Trim();
            if (string.IsNullOrEmpty(query))
                return;

            searchButton.IsEnabled = false;
            searchButton.Content = "...";
            _searchResults.Clear();
            resultsListBox.Items.Clear();

            try
            {
                var url = $"https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(query)}&count=5&language=en&format=json";
                var json = await _httpClient.GetStringAsync(url);
                using (var doc = JsonDocument.Parse(json))
                {
                    if (doc.RootElement.TryGetProperty("results", out var results))
                    {
                        foreach (var item in results.EnumerateArray())
                        {
                            var geo = new GeoResult
                            {
                                Name = item.TryGetProperty("name", out var n) ? n.GetString() : "",
                                Country = item.TryGetProperty("country", out var c) ? c.GetString() : "",
                                Admin1 = item.TryGetProperty("admin1", out var a) ? a.GetString() : "",
                                Latitude = item.TryGetProperty("latitude", out var lat) ? lat.GetDouble().ToString("F4", System.Globalization.CultureInfo.InvariantCulture) : "",
                                Longitude = item.TryGetProperty("longitude", out var lon) ? lon.GetDouble().ToString("F4", System.Globalization.CultureInfo.InvariantCulture) : ""
                            };
                            _searchResults.Add(geo);

                            var display = string.IsNullOrEmpty(geo.Admin1)
                                ? $"{geo.Name}, {geo.Country}"
                                : $"{geo.Name}, {geo.Admin1}, {geo.Country}";
                            resultsListBox.Items.Add(display);
                        }
                    }
                }

                resultsListBox.Visibility = _searchResults.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            catch
            {
                resultsListBox.Visibility = Visibility.Collapsed;
            }
            finally
            {
                searchButton.IsEnabled = true;
                searchButton.Content = "Search";
            }
        }

        private void ResultsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var index = resultsListBox.SelectedIndex;
            if (index < 0 || index >= _searchResults.Count)
                return;

            var geo = _searchResults[index];
            locationNameTextBox.Text = geo.Name;
            latitudeTextBox.Text = geo.Latitude;
            longitudeTextBox.Text = geo.Longitude;
            resultsListBox.Visibility = Visibility.Collapsed;
        }

        private class GeoResult
        {
            public string Name { get; set; }
            public string Country { get; set; }
            public string Admin1 { get; set; }
            public string Latitude { get; set; }
            public string Longitude { get; set; }
        }
    }
}
