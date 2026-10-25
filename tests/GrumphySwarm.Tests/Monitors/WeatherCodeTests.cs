using GrumphySwarm.Core.Monitors;

namespace GrumphySwarm.Tests.Monitors;

public class WeatherCodeTests
{
    [Theory]
    [InlineData(0, "Clear sky", true)]
    [InlineData(1, "Mainly clear", true)]
    [InlineData(2, "Partly cloudy", false)]
    [InlineData(3, "Overcast", false)]
    [InlineData(45, "Foggy", false)]
    [InlineData(63, "Rain", false)]
    [InlineData(71, "Snow", false)]
    [InlineData(95, "Thunderstorm", false)]
    public void Known_codes_map_to_labels(int code, string expectedLabel, bool expectedClear)
    {
        var (label, isClear) = WeatherCodes.Map(code);
        Assert.Equal(expectedLabel, label);
        Assert.Equal(expectedClear, isClear);
    }

    [Fact]
    public void Unknown_codes_fall_back_to_cloudy()
    {
        var (label, isClear) = WeatherCodes.Map(42);
        Assert.Equal("Cloudy", label);
        Assert.False(isClear);
    }

    [Theory]
    [InlineData(0, true, WeatherIconKind.Clear)]
    [InlineData(0, false, WeatherIconKind.ClearNight)]
    [InlineData(1, false, WeatherIconKind.ClearNight)]
    [InlineData(2, true, WeatherIconKind.PartlyCloudy)]
    [InlineData(3, true, WeatherIconKind.Cloudy)]
    [InlineData(45, true, WeatherIconKind.Fog)]
    [InlineData(53, true, WeatherIconKind.Drizzle)]
    [InlineData(63, true, WeatherIconKind.Rain)]
    [InlineData(81, false, WeatherIconKind.Rain)]
    [InlineData(73, true, WeatherIconKind.Snow)]
    [InlineData(95, true, WeatherIconKind.Thunder)]
    [InlineData(42, true, WeatherIconKind.Cloudy)]
    public void Icon_kinds_follow_the_weather_code(int code, bool isDay, WeatherIconKind expected)
    {
        Assert.Equal(expected, WeatherCodes.IconKind(code, isDay));
    }
}
