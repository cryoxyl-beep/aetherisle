namespace GrumphySwarm.Core.Monitors;

/// <summary>One top-app row: process name and its CPU share.</summary>
/// <param name="Name">Process name (merged across instances).</param>
/// <param name="CpuPercent">CPU share 0..100 across all cores.</param>
public sealed record AppUsage(string Name, double CpuPercent)
{
    /// <summary>0..1 fraction for bar widths.</summary>
    public double Fraction => Math.Clamp(CpuPercent, 0, 100) / 100.0;
}

/// <summary>
/// One-time machine identity for the stats view (CPU/GPU names, clock).
/// Empty strings mean "not readable" — the UI falls back to plain labels.
/// </summary>
public sealed record MachineInfo(
    string CpuName,
    string CpuClockText,
    string GpuName,
    int LogicalProcessorCount,
    DateTimeOffset Timestamp);

/// <summary>
/// A live system stats snapshot. Null fields mean "not measurable on this
/// machine" — the UI hides those rows instead of inventing numbers.
/// </summary>
/// <param name="CpuPercent">Total CPU utilization 0..100.</param>
/// <param name="GpuPercent">GPU engine utilization 0..100 (null when no counter exists).</param>
/// <param name="RamUsedGb">Committed RAM in use.</param>
/// <param name="RamTotalGb">Installed RAM.</param>
/// <param name="DiskPercent">Physical disk active time 0..100 (null when unavailable).</param>
/// <param name="DiskUsedGb">Used space on the C: volume (null when unreadable).</param>
/// <param name="DiskTotalGb">Total size of the C: volume (null when unreadable).</param>
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
    double? DiskUsedGb,
    double? DiskTotalGb,
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

    /// <summary>Disk line: "412 / 931 GB"; "—" when unreadable.</summary>
    public static string FormatDisk(double? usedGb, double? totalGb)
        => usedGb is { } used && totalGb is { } total && total > 0
            ? $"{Math.Round(used):0} / {Math.Round(total):0} GB"
            : "—";

    /// <summary>Temperature with degree sign: "29°"; "—" when unavailable.</summary>
    public static string FormatTemperature(double? celsius)
        => celsius is null ? "—" : $"{Math.Round(celsius.Value):0}°";

    /// <summary>
    /// "Intel(R) Core(TM) i7-9750H CPU @ 2.60GHz" → "Core i7-9750H".
    /// Falls back to the trimmed original when the pattern doesn't match.
    /// </summary>
    public static string ShortenCpuName(string rawName)
    {
        var name = rawName.Replace("(R)", string.Empty).Replace("(TM)", string.Empty).Replace("(tm)", string.Empty);
        name = name.Replace(" CPU", string.Empty, StringComparison.OrdinalIgnoreCase);
        name = System.Text.RegularExpressions.Regex.Replace(name, @"\s*@.*$", string.Empty);
        name = System.Text.RegularExpressions.Regex.Replace(name, @"\s+", " ").Trim();

        if (name.Contains("Core", StringComparison.OrdinalIgnoreCase))
        {
            var match = System.Text.RegularExpressions.Regex.Match(
                name,
                @"Core\s+\S+(?:\s+\S+)?",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (match.Success)
            {
                return match.Value;
            }
        }

        if (name.Contains("Ryzen", StringComparison.OrdinalIgnoreCase))
        {
            var match = System.Text.RegularExpressions.Regex.Match(
                name,
                @"Ryzen\s+\S+\s+\S+",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (match.Success)
            {
                return match.Value;
            }
        }

        return name.Length > 22 ? name[..22].Trim() : name;
    }
}
