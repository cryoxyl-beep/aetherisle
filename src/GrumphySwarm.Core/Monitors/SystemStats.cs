namespace GrumphySwarm.Core.Monitors;

/// <summary>One top-app row: process name and its CPU share.</summary>
public sealed record AppUsage(string Name, double CpuPercent);

/// <summary>
/// A live system stats snapshot. Null fields mean "not measurable on this
/// machine" — the UI hides those rows instead of inventing numbers.
/// </summary>
/// <param name="CpuPercent">Total CPU utilization 0..100.</param>
/// <param name="GpuPercent">GPU engine utilization 0..100 (null when no counter exists).</param>
/// <param name="RamUsedGb">Committed RAM in use.</param>
/// <param name="RamTotalGb">Installed RAM.</param>
/// <param name="DiskPercent">Physical disk active time 0..100 (null when unavailable).</param>
/// <param name="NetDownBytesPerSec">Aggregate download throughput.</param>
/// <param name="NetUpBytesPerSec">Aggregate upload throughput.</param>
/// <param name="Uptime">Time since boot.</param>
/// <param name="TopApps">Top processes by CPU.</param>
/// <param name="Timestamp">When the snapshot was taken.</param>
public sealed record SystemStatsSnapshot(
    double CpuPercent,
    double? GpuPercent,
    double RamUsedGb,
    double RamTotalGb,
    double? DiskPercent,
    double NetDownBytesPerSec,
    double NetUpBytesPerSec,
    TimeSpan Uptime,
    IReadOnlyList<AppUsage> TopApps,
    DateTimeOffset Timestamp);

/// <summary>Formatting helpers for the stats view (pure, unit-tested).</summary>
public static class StatsPresenters
{
    /// <summary>Compact human throughput: "1.2 MB/s", "356 KB/s", "0 B/s".</summary>
    public static string FormatBytesPerSecond(double bytesPerSecond)
    {
        if (bytesPerSecond < 1024)
        {
            return $"{bytesPerSecond:0} B/s";
        }

        if (bytesPerSecond < 1024 * 1024)
        {
            return $"{bytesPerSecond / 1024:0.#} KB/s";
        }

        if (bytesPerSecond < 1024 * 1024 * 1024)
        {
            return $"{bytesPerSecond / (1024 * 1024):0.#} MB/s";
        }

        return $"{bytesPerSecond / (1024 * 1024 * 1024):0.#} GB/s";
    }

    /// <summary>Uptime like "3d 4h" / "5h 12m" / "8m".</summary>
    public static string FormatUptime(TimeSpan uptime)
    {
        if (uptime.TotalDays >= 1)
        {
            return $"{(int)uptime.TotalDays}d {uptime.Hours}h";
        }

        if (uptime.TotalHours >= 1)
        {
            return $"{(int)uptime.TotalHours}h {uptime.Minutes}m";
        }

        return $"{Math.Max(0, (int)uptime.TotalMinutes)}m";
    }

    /// <summary>RAM line: "14.2 / 32.0 GB".</summary>
    public static string FormatMemory(double usedGb, double totalGb)
        => $"{usedGb:0.0} / {totalGb:0.0} GB";

    /// <summary>Percent text: "42%".</summary>
    public static string FormatPercent(double? percent)
        => percent is null ? "—" : $"{Math.Round(percent.Value):0}%";

    /// <summary>0..100 clamped fraction for bar widths.</summary>
    public static double BarFraction(double? percent)
        => percent is null ? 0 : Math.Clamp(percent.Value, 0, 100) / 100.0;

    /// <summary>RAM bar fraction from used/total.</summary>
    public static double MemoryFraction(double usedGb, double totalGb)
        => totalGb <= 0 ? 0 : Math.Clamp(usedGb / totalGb, 0, 1);
}
