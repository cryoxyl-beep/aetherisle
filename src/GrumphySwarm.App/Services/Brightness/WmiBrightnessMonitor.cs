using System.Management;
using System.Runtime.InteropServices;
using GrumphySwarm.Core.Monitors;
using Microsoft.Extensions.Logging;

namespace GrumphySwarm.App.Services.Brightness;

/// <summary>
/// Display brightness monitoring.
///
/// Strategy, in order:
///   1. WMI (root\\wmi) — internal panels typically publish WmiMonitorBrightness
///      and the event-driven WmiMonitorBrightnessEvent. This is the good path:
///      real notifications, no polling.
///   2. DDC/CI over the monitor configuration API — many external monitors
///      expose VCP code 0x10. Reads are blocking I2C calls, so this path uses a
///      cancellable 4-second background poll and disables itself after repeated
///      failures.
///   3. Neither works → IsSupported = false, logged once, UI stays quiet.
///
/// No value is ever fabricated; unsupported stays unsupported.
/// </summary>
public sealed class WmiBrightnessMonitor : IBrightnessMonitor
{
    private readonly ILogger<WmiBrightnessMonitor> _logger;
    private readonly object _gate = new();

    private ManagementEventWatcher? _watcher;
    private CancellationTokenSource? _ddcCancellationToken;
    private Task? _ddcLoop;
    private DdcBrightnessReader? _ddcReader;
    private int _ddcConsecutiveFailures;
    private BrightnessState? _lastPublished;
    private bool _disposed;

    public WmiBrightnessMonitor(ILogger<WmiBrightnessMonitor> logger)
    {
        _logger = logger;
    }

    public bool IsSupported { get; private set; }

    public string SupportDescription { get; private set; } = "not started";

    public BrightnessState? Current { get; private set; }

    public event Action<BrightnessState>? BrightnessChanged;

    public void Start()
    {
        lock (_gate)
        {
            if (_disposed || IsSupported)
            {
                return;
            }

            if (TryStartWmi())
            {
                IsSupported = true;
                SupportDescription = "WMI events (internal panel)";
                _logger.LogInformation("Brightness: WMI brightness monitoring active (event-driven).");
                return;
            }

            if (TryStartDdc())
            {
                IsSupported = true;
                SupportDescription = "DDC/CI poll (external monitor)";
                _logger.LogInformation("Brightness: DDC/CI fallback active (4s low-frequency poll).");
                return;
            }

            SupportDescription = "unsupported on this hardware/driver";
            _logger.LogInformation("Brightness: no usable interface (WMI class absent, DDC/CI unavailable) — monitoring disabled. This is expected on many desktop monitors.");
        }
    }

    // ------------------------------------------------------------------ WMI

    private bool TryStartWmi()
    {
        try
        {
            var scope = new ManagementScope(@"root\wmi");
            scope.Connect();

            // Initial read; some machines implement the event but not the read,
            // in which case we still accept the event path.
            int? initial = null;
            try
            {
                using var searcher = new ManagementObjectSearcher(scope, new SelectQuery("SELECT CurrentBrightness FROM WmiMonitorBrightness"));
                foreach (var obj in searcher.Get().Cast<ManagementObject>())
                {
                    initial = Convert.ToInt32(obj["CurrentBrightness"]);
                    break;
                }
            }
            catch (ManagementException ex)
            {
                _logger.LogDebug(ex, "Brightness: WmiMonitorBrightness read failed, trying the event class anyway.");
            }

            try
            {
                _watcher = new ManagementEventWatcher(scope, new WqlEventQuery("SELECT * FROM WmiMonitorBrightnessEvent"));
                _watcher.EventArrived += OnWmiEventArrived;
                _watcher.Start();
            }
            catch (ManagementException ex)
            {
                _logger.LogDebug(ex, "Brightness: WmiMonitorBrightnessEvent not available.");
                return false;
            }

            if (initial is { } percent)
            {
                Publish(new BrightnessState(percent, "Wmi", DateTimeOffset.Now));
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Brightness: WMI probe failed.");
            return false;
        }
    }

    private void OnWmiEventArrived(object sender, EventArrivedEventArgs e)
    {
        try
        {
            if (e.NewEvent["NewBrightness"] is { } raw)
            {
                Publish(new BrightnessState(Convert.ToInt32(raw), "WmiEvent", DateTimeOffset.Now));
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Brightness: malformed WMI event ignored.");
        }
    }

    // ------------------------------------------------------------------ DDC/CI

    private bool TryStartDdc()
    {
        _ddcReader = new DdcBrightnessReader();
        if (!_ddcReader.TryOpenPrimary())
        {
            return false;
        }

        _ddcCancellationToken = new CancellationTokenSource();
        _ddcLoop = Task.Run(() => DdcPollLoop(_ddcCancellationToken.Token));
        return true;
    }

    private async Task DdcPollLoop(CancellationToken cancellation)
    {
        while (!cancellation.IsCancellationRequested)
        {
            var read = _ddcReader?.TryReadPercent();
            if (read is { } percent)
            {
                _ddcConsecutiveFailures = 0;
                Publish(new BrightnessState(percent, "Ddc", DateTimeOffset.Now));
            }
            else
            {
                _ddcConsecutiveFailures++;
                if (_ddcConsecutiveFailures >= 3)
                {
                    _logger.LogInformation("Brightness: DDC/CI stopped responding — fallback disabled until restart.");
                    return;
                }
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(4), cancellation).ConfigureAwait(false);
            }
            catch (TaskCanceledException)
            {
                return;
            }
        }
    }

    private void Publish(BrightnessState state)
    {
        if (_lastPublished is { } last && last.Percent == state.Percent)
        {
            return;
        }

        _lastPublished = state;
        Current = state;
        BrightnessChanged?.Invoke(state);
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

            if (_watcher is not null)
            {
                try
                {
                    _watcher.Stop();
                    _watcher.EventArrived -= OnWmiEventArrived;
                    _watcher.Dispose();
                }
                catch
                {
                    // Best effort during shutdown.
                }

                _watcher = null;
            }

            _ddcCancellationToken?.Cancel();
            _ddcCancellationToken?.Dispose();
            _ddcReader?.Dispose();
        }
    }
}

/// <summary>
/// Minimal DDC/CI brightness reader (VCP 0x10) for the primary monitor via the
/// Windows Monitor Configuration API. Reads are blocking and may be slow —
/// only ever call from a background thread.
/// </summary>
internal sealed class DdcBrightnessReader : IDisposable
{
    private IntPtr _physicalMonitorHandle;

    public bool TryOpenPrimary()
    {
        try
        {
            var monitor = NativeMethods.MonitorFromPoint(
                new NativeMethods.POINT { X = 0, Y = 0 },
                NativeMethods.MONITOR_DEFAULTTOPRIMARY);

            if (NativeMethods.GetNumberOfPhysicalMonitorsFromHMONITOR(monitor, out var count) == 0 || count == 0)
            {
                return false;
            }

            var handles = new NativeMethods.PHYSICAL_MONITOR[1];
            if (NativeMethods.GetPhysicalMonitorsFromHMONITOR(monitor, 1, handles) == 0)
            {
                return false;
            }

            _physicalMonitorHandle = handles[0].Handle;
            return _physicalMonitorHandle != IntPtr.Zero;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Brightness percent 0..100, or null when the read fails.</summary>
    public int? TryReadPercent()
    {
        if (_physicalMonitorHandle == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            if (NativeMethods.GetVCPFeatureAndVCPFeatureReply(
                    _physicalMonitorHandle,
                    BrightnessVcpCode,
                    out _,
                    out var current,
                    out var maximum)
                && maximum > 0)
            {
                var percent = (int)Math.Round(current * 100.0 / maximum);
                return Math.Clamp(percent, 0, 100);
            }
        }
        catch
        {
            // DDC timeouts happen; treated as a failed read by the caller.
        }

        return null;
    }

    private const byte BrightnessVcpCode = 0x10;

    public void Dispose()
    {
        if (_physicalMonitorHandle != IntPtr.Zero)
        {
            try
            {
                NativeMethods.DestroyPhysicalMonitor(_physicalMonitorHandle);
            }
            catch
            {
                // Best effort.
            }

            _physicalMonitorHandle = IntPtr.Zero;
        }
    }

    private static class NativeMethods
    {
        internal const uint MONITOR_DEFAULTTOPRIMARY = 1;

        [StructLayout(LayoutKind.Sequential)]
        internal struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct PHYSICAL_MONITOR
        {
            public IntPtr Handle;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string Description;
        }

        [DllImport("user32.dll")]
        internal static extern IntPtr MonitorFromPoint(POINT pt, uint flags);

        [DllImport("dxva2.dll")]
        internal static extern int GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr monitor, out uint count);

        [DllImport("dxva2.dll", CharSet = CharSet.Unicode)]
        internal static extern int GetPhysicalMonitorsFromHMONITOR(IntPtr monitor, uint count, [Out] PHYSICAL_MONITOR[] monitors);

        [DllImport("dxva2.dll")]
        internal static extern int DestroyPhysicalMonitor(IntPtr monitor);

        [DllImport("dxva2.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetVCPFeatureAndVCPFeatureReply(
            IntPtr monitor,
            byte code,
            out byte type,
            out uint current,
            out uint maximum);
    }
}
