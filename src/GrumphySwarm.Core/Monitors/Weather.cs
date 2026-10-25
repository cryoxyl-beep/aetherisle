namespace GrumphySwarm.Core.Monitors;

/// <summary>One live weather observation (all values real; nothing fabricated).</summary>
/// <param name="City">Geo-located city name (e.g. "Bengaluru").</param>
/// <param name="TempC">Current temperature in Celsius.</param>
/// <param name="HighC">Today's forecast maximum.</param>
/// <param name="LowC">Today's forecast minimum.</param>
/// <param name="WeatherCode">WMO weather code from the provider.</param>
/// <param name="IsDay">True while the location is in daylight (provider-reported).</param>
/// <param name="Timestamp">When the observation was fetched.</param>
public sealed record WeatherState(
    string City,
    double TempC,
    double HighC,
    double LowC,
    int WeatherCode,
    bool IsDay,
    DateTimeOffset Timestamp);

/// <summary>Animated weather-icon kinds (amCharts-style visual states).</summary>
public enum WeatherIconKind
{
    Unknown,
    Clear,
    ClearNight,
    PartlyCloudy,
    Cloudy,
    Fog,
    Drizzle,
    Rain,
    Snow,
    Thunder,
}

/// <summary>Pure WMO weather-code → label mapping (unit-tested).</summary>
public static class WeatherCodes
{
    /// <summary>Maps a WMO code plus day/night flag to an animated icon kind.</summary>
    public static WeatherIconKind IconKind(int code, bool isDay) => code switch
    {
        0 or 1 => isDay ? WeatherIconKind.Clear : WeatherIconKind.ClearNight,
        2 => WeatherIconKind.PartlyCloudy,
        3 => WeatherIconKind.Cloudy,
        45 or 48 => WeatherIconKind.Fog,
        51 or 53 or 55 or 56 or 57 => WeatherIconKind.Drizzle,
        61 or 63 or 65 or 66 or 67 or 80 or 81 or 82 => WeatherIconKind.Rain,
        71 or 73 or 75 or 77 or 85 or 86 => WeatherIconKind.Snow,
        95 or 96 or 99 => WeatherIconKind.Thunder,
        _ => WeatherIconKind.Cloudy,
    };

    /// <summary>
    /// Maps a WMO weather code (open-meteo style) to a short human label.
    /// <paramref name="isClear"/> is true for sun-dominated conditions (the UI
    /// swaps the cloud shape for a sun glyph).
    /// </summary>
    public static (string Label, bool IsClear) Map(int code) => code switch
    {
        0 => ("Clear sky", true),
        1 => ("Mainly clear", true),
        2 => ("Partly cloudy", false),
        3 => ("Overcast", false),
        45 or 48 => ("Foggy", false),
        51 or 53 or 55 => ("Drizzle", false),
        56 or 57 => ("Freezing drizzle", false),
        61 => ("Light rain", false),
        63 => ("Rain", false),
        65 => ("Heavy rain", false),
        66 or 67 => ("Freezing rain", false),
        71 or 73 or 75 or 77 => ("Snow", false),
        80 or 81 => ("Rain showers", false),
        82 => ("Violent showers", false),
        85 or 86 => ("Snow showers", false),
        95 => ("Thunderstorm", false),
        96 or 99 => ("Thunderstorm, hail", false),
        _ => ("Cloudy", false),
    };
}
