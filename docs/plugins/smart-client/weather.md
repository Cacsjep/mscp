<div class="show-title" markdown>

# Weather

Display live weather conditions directly in XProtect Smart Client view items, powered by the free [Open-Meteo API](https://open-meteo.com/).

## Quick Start

1. In **Setup** mode, drag **Weather** into a view
3. In Properties, search for a city or enter lat/long
4. Switch to **Live** mode, weather data loads automatically

## Configuration

All settings are configured in the Smart Client **Properties** panel (Setup mode).

| Setting | Default | Description |
|---|---|---|
| **Location Name** | *(empty)* | Display label (e.g. "Berlin") |
| **Latitude** | *(empty)* | Geographic latitude |
| **Longitude** | *(empty)* | Geographic longitude |
| **Refresh Interval** | 15 min | How often to update weather data |
| **Temperature Unit** | Celsius | Celsius or Fahrenheit |

Use the **Search** button in Properties to find a city by name. The geocoding API returns up to 5 results to pick from.

## Features

- Live weather conditions with FontAwesome 5 icons
- Temperature, feels-like, humidity, wind speed/direction, cloud cover, pressure
- Day/night aware icons (sun vs moon)
- Auto-refresh on configurable interval
- 28 WMO weather codes mapped to descriptions and icons
- Dark theme matching Smart Client UI

## Weather Data

Powered by [Open-Meteo](https://open-meteo.com/). Free, no API key required, no registration needed.

## Troubleshooting

| Problem | Fix |
|---|---|
| No weather data | Check internet connectivity. Open-Meteo requires outbound HTTPS access. |
| Wrong location | Verify latitude/longitude in Properties. Use the Search button to re-select. |
| Plugin not showing | Check DLLs in `MIPPlugins\Weather\`. Unblock ZIP if manual install. |
