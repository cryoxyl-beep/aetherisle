using GrumphySwarm.Core.Monitors;
using Microsoft.Extensions.Logging;
using Windows.Devices.Power;
using BatteryStatus = Windows.System.Power.BatteryStatus;

namespace GrumphySwarm.App.Services.Power;

/// <summary>
/// Live system battery monitoring via the Windows.Devices.Power aggregate
/// battery (same source as the taskbar tray battery). Event-driven through
/// ReportUpdated — no polling. Machines without a battery (desktops) simply
/// report IsSupported=false and stay quiet.
/// </summary>
public sealed class SystemBatteryMonitor : ISystemBatteryMonitor
{
    private readonly ILogger<SystemBatteryMonitor> _logger;
    private Battery? _battery;
    private bool _disposed;

    public SystemBatteryMonitor(ILogger<SystemBatteryMonitor> logger)
    {
        _logger = logger;
    }

    public bool IsSupported { get; private set; }

    public SystemBatteryState? Current { get; private set; }

    public event Action<SystemBatteryState>? BatteryChanged;

    public void Start()
    {
        if (_battery is not null)
        {
            return;
        }

        try
        {
            _battery = Battery.AggregateBattery;
            var first = ReadReport();
            if (first is null)
            {
                _logger.LogInformation("Battery: no system battery present — battery display hidden.");
                _battery = null;
                return;
            }

            _battery.ReportUpdated += OnReportUpdated;
            IsSupported = true;
            Publish(first);
            _logger.LogInformation(
                "Battery: system battery monitoring active ({Percent}%, charging: {Charging}).",
                first.Percent,
                first.IsCharging);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Battery: aggregate battery unavailable — battery display hidden.");
            _battery = null;
        }
    }

    private void OnReportUpdated(Battery sender, object args)
    {
        var state = ReadReport();
        if (state is not null)
        {
            Publish(state);
        }
    }

    private SystemBatteryState? ReadReport()
    {
        try
        {
            var report = _battery?.GetReport();
            if (report?.RemainingCapacityInMilliwattHours is not { } remaining
                || report.FullChargeCapacityInMilliwattHours is not { } full
                || full <= 0)
            {
                return null;
            }

            var percent = (int)Math.Clamp(Math.Round(remaining * 100.0 / full), 0, 100);
            var charging = report.Status == BatteryStatus.Charging;

            return new SystemBatteryState(percent, charging, DateTimeOffset.Now);
        }
        catch
        {
            return null;
        }
    }

    private void Publish(SystemBatteryState state)
    {
        // Suppress no-op publishes (report events fire often).
        if (Current is { } last && last.Percent == state.Percent && last.IsCharging == state.IsCharging)
        {
            return;
        }

        Current = state;
        BatteryChanged?.Invoke(state);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_battery is not null)
        {
            try
            {
                _battery.ReportUpdated -= OnReportUpdated;
            }
            catch
            {
                // Best effort during shutdown.
            }

            _battery = null;
        }
    }
}
