namespace GrumphySwarm.Core.Monitors;

/// <summary>One live weather observation (all values real; nothing fabricated).</summary>
/// <param name="City">Geo-located city name (e.g. "Bengaluru").</param>
/// <param name="TempC">Current temperature in Celsius.</param>
/// <param name="HighC">Today's forecast maximum.</param>
/// <param name="LowC">Today's forecast minimum.</param>
/// <param name="WeatherCode">WMO weather code from the provider.</param>
/// <param name="Timestamp">When the observation was fetched.</param>
public sealed record WeatherState(
    string City,
    double TempC,
    double HighC,
    double LowC,
    int WeatherCode,
    DateTimeOffset Timestamp);

/// <summary>Pure WMO weather-code → label mapping (unit-tested).</summary>
public static class WeatherCodes
{
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
