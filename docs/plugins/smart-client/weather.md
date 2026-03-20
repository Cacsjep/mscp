<div class="show-title" markdown>

# Weather

Display live weather conditions directly in XProtect Smart Client view items, powered by the free [Open-Meteo API](https://open-meteo.com/).

## Quick Start

1. In **Setup** mode, drag **Weather** into a view
3. In Properties, search for a city or enter lat/long
4. Switch to **Live** mode, weather data loads automatically

<video controls width="100%">
  <source src="../vids/weather_usage.mp4" type="video/mp4">
</video>

## Configuration

All settings are configured in the Smart Client **Properties** panel (Setup mode).

| Setting | Default | Description |
|---|---|---|
| **Location Name** | *(empty)* | Display label (e.g. "Berlin") |
| **Latitude** | *(empty)* | Geographic latitude |
| **Longitude** | *(empty)* | Geographic longitude |
| **Refresh Interval** | 15 min | How often to update weather data |
| **Temperature Unit** | Celsius | Celsius or Fahrenheit |
| **Wind Speed Unit** | km/h | km/h, mph, m/s, or Knots |
| **Pressure Unit** | hPa | hPa or Inches of Mercury (inHg) |
| **Show Hourly Forecast** | Enabled | Toggle the 12-hour hourly forecast strip |
| **Show Daily Forecast** | Enabled | Toggle the 7-day daily forecast strip |

Use the **Search** button in Properties to find a city by name. The geocoding API returns up to 5 results to pick from.

## Forecast

When enabled, forecasts appear below the current conditions:

- **Hourly** — next 12 hours, showing time, weather icon, and temperature
- **Daily** — next 7 days, showing day name, weather icon, high and low temperature

Forecast sections automatically hide when the view item is too small and reappear when there is enough space.

## Weather Data

Powered by [Open-Meteo](https://open-meteo.com/). Free, no API key required, no registration needed.

## Troubleshooting

| Problem | Fix |
|---|---|
| No weather data | Check internet connectivity. Open-Meteo requires outbound HTTPS access. |
| Wrong location | Verify latitude/longitude in Properties. Use the Search button to re-select. |
| Plugin not showing | Check DLLs in `MIPPlugins\Weather\`. Unblock ZIP if manual install. |
