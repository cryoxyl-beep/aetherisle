using System.Windows;
using GrumphySwarm.App.Logging;
using GrumphySwarm.App.Services;
using GrumphySwarm.App.ViewModels;
using GrumphySwarm.App.Views;
using GrumphySwarm.Core.Settings;
using GrumphySwarm.Core.State;
using GrumphySwarm.Core.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Windows.Threading;

namespace GrumphySwarm.App;

/// <summary>
/// Composition root: exception hooks, single-instance guard, DI container,
/// notch window and tray icon. The app runs until the user exits via the tray
/// (ShutdownMode is explicit), so no console or main window lifecycle is needed.
/// </summary>
public partial class App : Application
{
    private FileLoggerProvider _loggerProvider = null!;
    private ServiceProvider _services = null!;
    private SingleInstanceGuard _singleInstance = null!;
    private NotchWindow? _notchWindow;
    private TrayIconService? _trayService;
    private JsonSettingsStore _settingsStore = null!;
    private NotchSettings _settings = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Logging first so even early failures are on disk.
        _loggerProvider = new FileLoggerProvider();

        // Diagnostic mode: dump every candidate Bluetooth battery source to the
        // log and exit. Usage: GrumphySwarm.exe --bt-diagnostic
        if (e.Args.Contains("--bt-diagnostic", StringComparer.OrdinalIgnoreCase))
        {
            RunBluetoothDiagnostic();
            Shutdown(0);
            return;
        }

        // Global exception sinks: nothing should ever terminate the notch silently.
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        SessionEnding += (_, _) => Shutdown(0);

        // Single instance: a second launch signals the running one and exits.
        _singleInstance = new SingleInstanceGuard(
            mutexName: @"Local\GrumphySwarm.SingleInstance",
            activateEventName: @"Local\GrumphySwarm.Activate");
        if (!_singleInstance.TryAcquire())
        {
            _loggerProvider.CreateLogger("GrumphySwarm.App")
                .LogInformation("Another instance is already running — signalled it to expand and exiting.");
            _loggerProvider.Dispose();
            Shutdown(0);
            return;
        }

        _settingsStore = new JsonSettingsStore();
        _settings = _settingsStore.Load();

        var services = new ServiceCollection();
        services.AddLogging(builder => builder
            .AddProvider(_loggerProvider)
            .SetMinimumLevel(LogLevel.Information));
        services.AddSingleton(_settingsStore);
        services.AddSingleton<Core.Settings.ISettingsStore>(_settingsStore);
        services.AddSingleton(_settings);
        services.AddSingleton(sp => new NotchStateMachine(sp.GetRequiredService<NotchSettings>()));
        services.AddSingleton<Core.Messaging.NotchStatusBus>();
        services.AddSingleton<Core.Agents.IAgentProfileStore>(sp => new Core.Agents.JsonAgentProfileStore());
        services.AddSingleton<Core.Monitors.IAudioVolumeMonitor, Services.Audio.CoreAudioVolumeMonitor>();
        services.AddSingleton<Core.Monitors.IBrightnessMonitor, Services.Brightness.WmiBrightnessMonitor>();
        services.AddSingleton<Core.Monitors.IMediaMonitor, Services.Media.WindowsMediaMonitor>();
        services.AddSingleton<Core.Monitors.IBluetoothBatteryMonitor, Services.Bluetooth.BluetoothBatteryMonitor>();
        services.AddSingleton<Core.Monitors.ISystemBatteryMonitor, Services.Power.SystemBatteryMonitor>();
        services.AddSingleton<Services.Audio.AudioSpectrumMonitor>();
        services.AddSingleton<Services.Telemetry.SystemStatsMonitor>();
        services.AddSingleton<ViewModels.AgentsViewModel>();
        services.AddSingleton<ViewModels.NotchViewModel>();
        services.AddSingleton<TrayIconService>();
        _services = services.BuildServiceProvider();

        var logger = _services.GetRequiredService<ILogger<App>>();
        logger.LogInformation(
            "Grumphy Swarm starting — pid {Pid}, .NET {DotNet}, OS {Os}, 64-bit: {Is64Bit}.",
            Environment.ProcessId,
            Environment.Version,
            Environment.OSVersion,
            Environment.Is64BitProcess);
        logger.LogInformation(
            "Settings from {Path} (collapse delay {Delay} ms, reduced motion {ReducedMotion}).",
            _settingsStore.FilePath,
            _settings.CollapseDelayMs,
            _settings.ReducedMotion);

        _singleInstance.ActivationRequested += () => Dispatcher.BeginInvoke(() =>
        {
            logger.LogInformation("Activation requested by a second instance — expanding notch.");
            _notchWindow?.ViewModel.RequestExpandFromTray();
        });

        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        try
        {
            _notchWindow = new NotchWindow(
                _services.GetRequiredService<ViewModels.NotchViewModel>(),
                _services.GetRequiredService<ILogger<NotchWindow>>());
            _notchWindow.Show();

            // System monitors: start after the window exists; the first event of
            // each kind only seeds state (no transient overlay flash at launch).
            var audioMonitor = _services.GetRequiredService<Core.Monitors.IAudioVolumeMonitor>();
            var brightnessMonitor = _services.GetRequiredService<Core.Monitors.IBrightnessMonitor>();
            var mediaMonitor = _services.GetRequiredService<Core.Monitors.IMediaMonitor>();
            var bluetoothMonitor = _services.GetRequiredService<Core.Monitors.IBluetoothBatteryMonitor>();
            var spectrumMonitor = _services.GetRequiredService<Services.Audio.AudioSpectrumMonitor>();
            audioMonitor.Start();
            brightnessMonitor.Start();
            mediaMonitor.Start();
            bluetoothMonitor.Start();
            spectrumMonitor.Start();
            _services.GetRequiredService<Services.Telemetry.SystemStatsMonitor>().Start();

            // System battery: seed the pill display from the first report.
            var systemBatteryMonitor = _services.GetRequiredService<Core.Monitors.ISystemBatteryMonitor>();
            systemBatteryMonitor.Start();
            if (systemBatteryMonitor.Current is { } batteryState)
            {
                _services.GetRequiredService<ViewModels.NotchViewModel>().SeedSystemBattery(batteryState);
            }

            logger.LogInformation(
                "Monitors started — audio supported: {Audio}; brightness: {Brightness} ({BrightnessDetail}); media: {Media}; bluetooth: {Bluetooth}; system battery: {Battery}; spectrum: starting on its capture thread.",
                audioMonitor.IsSupported,
                brightnessMonitor.IsSupported,
                brightnessMonitor.SupportDescription,
                mediaMonitor.IsSupported,
                bluetoothMonitor.IsSupported,
                systemBatteryMonitor.IsSupported);

            // Agent profile editing opens in a small activatable window (text
            // input needs keyboard focus, which the notch never takes).
            var agents = _services.GetRequiredService<ViewModels.AgentsViewModel>();
            agents.EditRequested += profile => Dispatcher.BeginInvoke(
                () => new Views.AgentEditorWindow(agents, profile).Show());

            _trayService = _services.GetRequiredService<TrayIconService>();
            _trayService.ExitRequested += ShutdownFromTray;
            _trayService.Initialize();

            logger.LogInformation("Startup complete.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Fatal error during startup.");
            MessageBox.Show(
                $"Grumphy Swarm failed to start:\n\n{ex.Message}\n\nDetails were written to the log file.",
                "Grumphy Swarm",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    /// <summary>
    /// One-shot Bluetooth battery source dump: enumerates PnP devnodes and
    /// containers with every battery-ish property key and logs any object that
    /// carries a value or an allow-listed name.
    /// </summary>
    private void RunBluetoothDiagnostic()
    {
        var logger = _loggerProvider.CreateLogger("BtDiagnostic");
        logger.LogInformation("Bluetooth battery diagnostic starting.");

        var hfpKeys = new[]
        {
            "{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2",
            "{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 3",
        };

        var aepKeys = new[]
        {
            "System.Devices.AepBattery_Level",
            "{83DA6326-97A6-4088-9453-A1923F573B29} 4",
        };

        foreach (var (kind, keys) in new[]
                 {
                    (global::Windows.Devices.Enumeration.DeviceInformationKind.Device, hfpKeys),
                    (global::Windows.Devices.Enumeration.DeviceInformationKind.DeviceContainer, hfpKeys),
                    (global::Windows.Devices.Enumeration.DeviceInformationKind.AssociationEndpoint, aepKeys),
                    (global::Windows.Devices.Enumeration.DeviceInformationKind.AssociationEndpointContainer, aepKeys),
                 })
        {
            try
            {
                var results = global::Windows.Devices.Enumeration.DeviceInformation.FindAllAsync(
                    string.Empty,
                    keys,
                    kind).GetAwaiter().GetResult();

                logger.LogInformation("[{Kind}] {Count} object(s).", kind, results.Count);

                foreach (var device in results)
                {
                    var hasValue = false;
                    foreach (var property in device.Properties)
                    {
                        if (property.Value is not null)
                        {
                            hasValue = true;
                        }
                    }

                    var nameMatches = device.Name?.Contains("airdopes", StringComparison.OrdinalIgnoreCase) == true
                        || device.Name?.Contains("zeb", StringComparison.OrdinalIgnoreCase) == true
                        || device.Name?.Contains("boat", StringComparison.OrdinalIgnoreCase) == true;

                    if (!hasValue && !nameMatches)
                    {
                        continue;
                    }

                    var detail = string.Join(
                        ", ",
                        device.Properties.Where(p => p.Value is not null)
                            .Select(p => $"{p.Key}={p.Value}"));
                    logger.LogInformation(
                        "  {Name} | {Id} | {Properties}",
                        device.Name,
                        device.Id,
                        detail);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning("[{Kind}] failed: {Message}", kind, ex.Message);
            }
        }

        logger.LogInformation("Bluetooth battery diagnostic complete.");
    }

    private void ShutdownFromTray()
    {
        var logger = _services.GetRequiredService<ILogger<App>>();
        logger.LogInformation("Exit requested from tray menu — shutting down.");
        Shutdown(0);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_services is null)
        {
            // Diagnostic mode exits before composition is built.
            _loggerProvider?.Dispose();
            base.OnExit(e);
            return;
        }

        try
        {
            _settingsStore.Save(_settings);
        }
        catch
        {
            // Settings persistence must never block shutdown.
        }

        _trayService?.Dispose();
        _notchWindow?.Close();
        _notchWindow?.ViewModel.Dispose();
        _singleInstance.Dispose();

        // Disposes every registered monitor (audio, brightness, media) and
        // view model — no background service may outlive shutdown.
        _services.Dispose();
        _loggerProvider.Dispose();

        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _services.GetRequiredService<ILogger<App>>()
            .LogError(e.Exception, "Unhandled exception on the dispatcher thread — continuing.");
        e.Handled = true;
    }

    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var logger = _loggerProvider.CreateLogger("GrumphySwarm.App");
        logger.LogError(e.ExceptionObject as Exception, "Unhandled exception on a non-UI thread (terminating: {Terminating}).", e.IsTerminating);
        _loggerProvider.Dispose();
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        _services.GetRequiredService<ILogger<App>>()
            .LogError(e.Exception, "Unobserved task exception — marking observed.");
        e.SetObserved();
    }
}
