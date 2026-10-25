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
}
