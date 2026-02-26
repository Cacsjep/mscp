# Weather

Display live weather conditions directly in XProtect™ Smart Client view items, powered by the free [Open-Meteo API](https://open-meteo.com/).

> [!IMPORTANT]
> Not affiliated with or supported by Milestone Systems. XProtect™ is a trademark of Milestone Systems A/S.

## Quick Start

1. Download the installer from [Releases](https://github.com/Cacsjep/mscp/releases)
2. **Setup** mode: drag **Weather** into a view
3. In Properties, search for a city or enter lat/long
4. **Live** mode: weather data loads automatically

**Requires:** XProtect™ Smart Client (Professional+, Expert, Corporate, or Essential+)

## Installation

### Installer (Recommended)

Download `MSCPlugins-vX.X-Setup.exe` from [Releases](https://github.com/Cacsjep/mscp/releases) and run as **Administrator**. Select **Weather Plugin** in the component list.

### Manual (ZIP)

1. Download `Weather-vX.X.zip` from [Releases](https://github.com/Cacsjep/mscp/releases)
2. **Unblock** it first: right-click -> Properties -> Unblock
3. Extract to `C:\Program Files\Milestone\MIPPlugins\Weather\`
4. Restart the Smart Client

## Configuration

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

Powered by [Open-Meteo](https://open-meteo.com/) -- free, no API key required, no registration needed.
