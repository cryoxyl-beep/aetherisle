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

    [Fact]
    public void Disk_usage_formats_or_dashes()
    {
        Assert.Equal("412 / 931 GB", StatsPresenters.FormatDisk(411.6, 931.2));
        Assert.Equal("—", StatsPresenters.FormatDisk(null, 931.2));
        Assert.Equal("—", StatsPresenters.FormatDisk(100, null));
    }

    [Fact]
    public void Temperatures_render_with_degree_sign()
    {
        Assert.Equal("29°", StatsPresenters.FormatTemperature(28.7));
        Assert.Equal("0°", StatsPresenters.FormatTemperature(0.2));
        Assert.Equal("—", StatsPresenters.FormatTemperature(null));
    }

    [Fact]
    public void Cpu_names_shorten_to_marketing_name()
    {
        Assert.Equal(
            "Core i7-9750H",
            StatsPresenters.ShortenCpuName("Intel(R) Core(TM) i7-9750H CPU @ 2.60GHz"));
        Assert.Equal(
            "Ryzen 7 5800H",
            StatsPresenters.ShortenCpuName("AMD Ryzen 7 5800H with Radeon Graphics"));
        Assert.Equal(
            "Qualcomm Snapdragon X",
            StatsPresenters.ShortenCpuName("Qualcomm Snapdragon X Elite X1E-84-100"));
    }

    [Fact]
    public void App_usage_fraction_clamps_for_bars()
    {
        Assert.Equal(0.42, new AppUsage("firefox", 42).Fraction);
        Assert.Equal(1.0, new AppUsage("chrome", 250).Fraction);
        Assert.Equal(0.0, new AppUsage("idle", -3).Fraction);
    }
}
