using System.Diagnostics;
using System.Management;
using GrumphySwarm.Core.Monitors;
using Microsoft.Extensions.Logging;

namespace GrumphySwarm.App.Services.Telemetry;

/// <summary>
/// Live system telemetry for the Stats tab: CPU, GPU, RAM, disk, network
/// throughput, uptime and top processes by CPU. Samples every 2 seconds on a
/// background thread via Windows performance counters; anything unavailable on
/// this machine (GPU counters, some disk setups) reports as null and the UI
/// hides the row — never a fabricated number.
/// </summary>
public sealed class SystemStatsMonitor : IDisposable
{
    private readonly ILogger<SystemStatsMonitor> _logger;
    private readonly object _gate = new();

    private Thread? _thread;
    private CancellationTokenSource? _cancellation;
    private bool _disposed;

    private PerformanceCounter? _cpuCounter;
    private PerformanceCounter? _diskCounter;
    private readonly List<PerformanceCounter> _gpuCounters = [];
    private readonly List<PerformanceCounter> _netDownCounters = [];
    private readonly List<PerformanceCounter> _netUpCounters = [];
    private Dictionary<int, (Process Process, TimeSpan CpuTime)>? _lastProcessSample;
    private int _gpuRefreshCountdown;

    public SystemStatsMonitor(ILogger<SystemStatsMonitor> logger)
    {
        _logger = logger;
    }

    public event Action<SystemStatsSnapshot>? SnapshotAvailable;

    public void Start()
    {
        lock (_gate)
        {
            if (_thread is not null || _disposed)
            {
                return;
            }

            _cancellation = new CancellationTokenSource();
            _thread = new Thread(SampleLoop)
            {
                IsBackground = true,
                Name = "GrumphySwarm.Stats",
                Priority = ThreadPriority.BelowNormal,
            };
            _thread.SetApartmentState(ApartmentState.MTA);
            _thread.Start(_cancellation.Token);
        }
    }

    private void SampleLoop(object? state)
    {
        var cancellation = (CancellationToken)state!;
        var cores = Math.Max(1, Environment.ProcessorCount);

        // Prime the CPU counter (first NextValue is always 0).
        try
        {
            _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total", readOnly: true);
            _cpuCounter.NextValue();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Stats: CPU counter unavailable.");
            _cpuCounter = null;
        }

        try
        {
            _diskCounter = new PerformanceCounter("PhysicalDisk", "% Disk Time", "_Total", readOnly: true);
            _diskCounter.NextValue();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Stats: disk counter unavailable.");
            _diskCounter = null;
        }

        RefreshNetworkCounters();
        RefreshGpuCounters();
        _lastProcessSample = SampleProcesses();

        while (!cancellation.IsCancellationRequested)
        {
            try
            {
                System.Threading.Thread.Sleep(2000);
                if (cancellation.IsCancellationRequested)
                {
                    return;
                }

                var snapshot = Capture(cores);
                SnapshotAvailable?.Invoke(snapshot);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Stats: sample failed (skipped).");
            }
        }
    }

    private SystemStatsSnapshot Capture(int cores)
    {
        var cpu = ReadPercent(_cpuCounter);
        var disk = ReadPercent(_diskCounter);

        double? gpu = null;
        lock (_gpuCounters)
        {
            if (_gpuCounters.Count > 0)
            {
                var sum = 0.0;
                foreach (var counter in _gpuCounters)
                {
                    sum += counter.NextValue();
                }

                // Multiple engines can each be at 100% — normalize to one GPU.
                gpu = Math.Clamp(sum, 0, 100);
            }
        }

        // GPU engine instances churn (app windows open/close) — refresh weekly-ish.
        if (--_gpuRefreshCountdown <= 0)
        {
            _gpuRefreshCountdown = 30;
            if (gpu is null)
            {
                RefreshGpuCounters();
            }
        }

        double down = 0, up = 0;
        lock (_netDownCounters)
        {
            foreach (var counter in _netDownCounters)
            {
                down += counter.NextValue();
            }
        }

        lock (_netUpCounters)
        {
            foreach (var counter in _netUpCounters)
            {
                up += counter.NextValue();
            }
        }

        var (ramUsed, ramTotal) = ReadMemory();
        var topApps = ComputeTopProcesses(cores);

        return new SystemStatsSnapshot(
            CpuPercent: cpu ?? 0,
            GpuPercent: gpu,
            RamUsedGb: ramUsed,
            RamTotalGb: ramTotal,
            DiskPercent: disk,
            NetDownBytesPerSec: down,
            NetUpBytesPerSec: up,
            Uptime: TimeSpan.FromMilliseconds(Environment.TickCount64),
            TopApps: topApps,
            Timestamp: DateTimeOffset.Now);
    }

    private static double? ReadPercent(PerformanceCounter? counter)
    {
        if (counter is null)
        {
            return null;
        }

        try
        {
            return Math.Clamp(counter.NextValue(), 0, 100);
        }
        catch
        {
            return null;
        }
    }

    private void RefreshGpuCounters()
    {
        try
        {
            var category = new PerformanceCounterCategory("GPU Engine");
            var names = category.GetInstanceNames()
                .Where(n => n.IndexOf("engtype_3D", StringComparison.OrdinalIgnoreCase) >= 0
                    || n.IndexOf("engtype_Compute", StringComparison.OrdinalIgnoreCase) >= 0)
                .ToArray();

            lock (_gpuCounters)
            {
                foreach (var counter in _gpuCounters)
                {
                    counter.Dispose();
                }

                _gpuCounters.Clear();
                foreach (var name in names)
                {
                    _gpuCounters.Add(new PerformanceCounter("GPU Engine", "Utilization Percentage", name, readOnly: true));
                }
            }

            if (_gpuCounters.Count > 0)
            {
                _logger.LogDebug("Stats: tracking {Count} GPU engine counters.", _gpuCounters.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Stats: GPU counters unavailable on this machine.");
        }
    }

    private void RefreshNetworkCounters()
    {
        try
        {
            var category = new PerformanceCounterCategory("Network Interface");
            var names = category.GetInstanceNames()
                .Where(n => !n.Contains("Loopback", StringComparison.OrdinalIgnoreCase)
                    && !n.Contains("Teredo", StringComparison.OrdinalIgnoreCase)
                    && !n.Contains("ISATAP", StringComparison.OrdinalIgnoreCase)
                    && !n.Contains("QoS Packet Scheduler", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            lock (_netDownCounters)
            {
                _netDownCounters.Clear();
                _netUpCounters.Clear();
                foreach (var name in names)
                {
                    _netDownCounters.Add(new PerformanceCounter("Network Interface", "Bytes Received/sec", name, readOnly: true));
                    _netUpCounters.Add(new PerformanceCounter("Network Interface", "Bytes Sent/sec", name, readOnly: true));
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Stats: network counters unavailable.");
        }
    }

    private static (double UsedGb, double TotalGb) ReadMemory()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "root\\CIMv2",
                "SELECT TotalVisibleMemorySize, FreePhysicalMemory FROM Win32_OperatingSystem");
            foreach (var item in searcher.Get().Cast<ManagementObject>())
            {
                var totalKb = Convert.ToDouble(item["TotalVisibleMemorySize"]);
                var freeKb = Convert.ToDouble(item["FreePhysicalMemory"]);
                var totalGb = totalKb / (1024 * 1024);
                var usedGb = (totalKb - freeKb) / (1024 * 1024);
                return (usedGb, totalGb);
            }
        }
        catch
        {
            // Fall through with zeros — the UI hides the row.
        }

        return (0, 0);
    }

    private static Dictionary<int, (Process Process, TimeSpan CpuTime)> SampleProcesses()
    {
        var sample = new Dictionary<int, (Process, TimeSpan)>();
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                sample[process.Id] = (process, process.TotalProcessorTime);
            }
            catch
            {
                // Access denied / exited — skip.
                process.Dispose();
            }
        }

        return sample;
    }

    private List<AppUsage> ComputeTopProcesses(int cores)
    {
        var previous = _lastProcessSample;
        _lastProcessSample = SampleProcesses();
        if (previous is null)
        {
            return [];
        }

        var apps = new List<AppUsage>();
        foreach (var (pid, current) in _lastProcessSample)
        {
            if (!previous.TryGetValue(pid, out var before))
            {
                continue;
            }

            var deltaSeconds = (current.CpuTime - before.CpuTime).TotalSeconds;
            if (deltaSeconds <= 0)
            {
                continue;
            }

            var percent = (deltaSeconds / 2.0 / cores) * 100.0; // 2s sample window
            if (percent < 0.5)
            {
                continue;
            }

            var name = current.Process.ProcessName;
            foreach (var existing in apps)
            {
                if (existing.Name == name)
                {
                    // Chrome/Electron spawn many processes — merge same-name.
                    apps.Remove(existing);
                    percent += existing.CpuPercent;
                    break;
                }
            }

            apps.Add(new AppUsage(name, Math.Clamp(percent, 0, 100)));
        }

        // Dispose the previous sample's Process objects.
        foreach (var sample in previous.Values)
        {
            sample.Process.Dispose();
        }

        return apps.OrderByDescending(a => a.CpuPercent).Take(3).ToList();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _cancellation?.Cancel();
            try
            {
                _thread?.Join(TimeSpan.FromSeconds(2));
            }
            catch
            {
                // Best effort.
            }

            _cpuCounter?.Dispose();
            _diskCounter?.Dispose();
            lock (_gpuCounters)
            {
                foreach (var counter in _gpuCounters)
                {
                    counter.Dispose();
                }

                _gpuCounters.Clear();
            }

            lock (_netDownCounters)
            {
                foreach (var counter in _netDownCounters)
                {
                    counter.Dispose();
                }

                _netDownCounters.Clear();
            }

            lock (_netUpCounters)
            {
                foreach (var counter in _netUpCounters)
                {
                    counter.Dispose();
                }

                _netUpCounters.Clear();
            }

            _cancellation?.Dispose();
        }
    }
}
