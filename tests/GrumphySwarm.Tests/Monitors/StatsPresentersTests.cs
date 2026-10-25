using GrumphySwarm.Core.Monitors;

namespace GrumphySwarm.Tests.Monitors;

public class StatsPresentersTests
{
    [Theory]
    [InlineData(0, "0 B/s")]
    [InlineData(512, "512 B/s")]
    [InlineData(2048, "2 KB/s")]
    [InlineData(386_000, "377 KB/s")]
    [InlineData(1_500_000, "1.4 MB/s")]
    [InlineData(250_000_000, "238.4 MB/s")]
    [InlineData(2_700_000_000, "2.5 GB/s")]
    public void Throughput_formats_compactly(double bytesPerSecond, string expected)
    {
        Assert.Equal(expected, StatsPresenters.FormatBytesPerSecond(bytesPerSecond));
    }

    [Fact]
    public void Uptime_compacts_to_days_hours_minutes()
    {
        Assert.Equal("3d 4h", StatsPresenters.FormatUptime(new TimeSpan(3, 4, 12, 0)));
        Assert.Equal("5h 12m", StatsPresenters.FormatUptime(new TimeSpan(5, 12, 0))); // h, m, s
        Assert.Equal("8m", StatsPresenters.FormatUptime(new TimeSpan(0, 0, 8, 20)));
        Assert.Equal("0m", StatsPresenters.FormatUptime(TimeSpan.Zero));
    }

    [Fact]
    public void Memory_and_percent_formatting()
    {
        Assert.Equal("14.2 / 32.0 GB", StatsPresenters.FormatMemory(14.23, 31.99));
        Assert.Equal("42%", StatsPresenters.FormatPercent(41.6));
        Assert.Equal("—", StatsPresenters.FormatPercent(null));
    }

    [Fact]
    public void Bar_fractions_clamp()
    {
        Assert.Equal(0.5, StatsPresenters.BarFraction(50));
        Assert.Equal(1.0, StatsPresenters.BarFraction(150));
        Assert.Equal(0.0, StatsPresenters.BarFraction(null));
        Assert.Equal(0.0, StatsPresenters.BarFraction(-5));
        Assert.Equal(0.5, StatsPresenters.MemoryFraction(16, 32));
        Assert.Equal(0.0, StatsPresenters.MemoryFraction(16, 0));
    }
}
