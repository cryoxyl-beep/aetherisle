using System.Runtime.InteropServices;
using GrumphySwarm.Core.Monitors;
using Microsoft.Extensions.Logging;

namespace GrumphySwarm.App.Services.Audio;

/// <summary>
/// Event-driven master-volume monitoring over Windows Core Audio. A control
/// change callback on the default render endpoint fires for every volume or
/// mute change regardless of origin (keyboard, app, slider), so there is no
/// polling. If the default endpoint changes, the notification client rebinds
/// the callback on a background thread.
/// </summary>
public sealed class CoreAudioVolumeMonitor : IAudioVolumeMonitor
{
    private readonly ILogger<CoreAudioVolumeMonitor> _logger;
    private readonly object _gate = new();

    private CoreAudioInterop.IMMDeviceEnumerator? _enumerator;
    private NotificationClient? _notificationClient;
    private CoreAudioInterop.IAudioEndpointVolume? _endpoint;
    private EndpointVolumeCallback? _endpointCallback;
    private VolumeState? _lastPublished;
    private System.Threading.Timer? _resilienceTimer;
    private bool _disposed;

    public CoreAudioVolumeMonitor(ILogger<CoreAudioVolumeMonitor> logger)
    {
        _logger = logger;
    }

    public bool IsSupported { get; private set; }

    public VolumeState? Current { get; private set; }

    public event Action<VolumeState>? VolumeChanged;

    public void Start()
    {
        lock (_gate)
        {
            if (_disposed || _enumerator is not null)
            {
                return;
            }

            try
            {
                _enumerator = (CoreAudioInterop.IMMDeviceEnumerator)new CoreAudioInterop.MMDeviceEnumeratorComObject();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Audio: MMDeviceEnumerator unavailable — volume monitoring disabled.");
                return;
            }

            if (!TryBindDefaultEndpoint())
            {
                _logger.LogWarning("Audio: no default render endpoint — volume monitoring disabled (a device may appear later and will NOT be picked up this session).");
                return;
            }

            _notificationClient = new NotificationClient(this);
            if (_enumerator.RegisterEndpointNotificationCallback(_notificationClient) < 0)
            {
                _logger.LogWarning("Audio: could not register device notifications; endpoint switching won't be tracked.");
                _notificationClient = null;
            }

            IsSupported = true;
            _logger.LogInformation("Audio: volume monitoring active (Core Audio, event-driven + 1.5s resilience poll).");

            // Safety net: some Windows sessions silently stop delivering
            // IAudioEndpointVolume callbacks (mute-only changes are the common
            // victim). A low-frequency read through the held endpoint catches
            // anything the events miss; the publish dedupe makes no-op ticks free.
            _resilienceTimer = new System.Threading.Timer(
                _ => PollEndpointOnce(),
                null,
                TimeSpan.FromMilliseconds(1500),
                TimeSpan.FromMilliseconds(1500));
        }
    }

    /// <summary>Low-frequency fallback read; publishes only on real changes.</summary>
    private void PollEndpointOnce()
    {
        try
        {
            lock (_gate)
            {
                if (_disposed || _endpoint is null)
                {
                    return;
                }

                if (_endpoint.GetMasterVolumeLevelScalar(out var level) >= 0
                    && _endpoint.GetMute(out var mute) >= 0)
                {
                    Publish(new VolumeState(level, mute, DateTimeOffset.Now));
                }
            }
        }
        catch
        {
            // Endpoint raced with a device change; the next tick retries.
        }
    }

    private bool TryBindDefaultEndpoint()
    {
        try
        {
            if (_enumerator!.GetDefaultAudioEndpoint(
                    CoreAudioInterop.EDataFlowRender,
                    CoreAudioInterop.ERoleMultimedia,
                    out var device) < 0)
            {
                return false;
            }

            var iid = typeof(CoreAudioInterop.IAudioEndpointVolume).GUID;
            if (device.Activate(ref iid, CoreAudioInterop.ClsctxAll, IntPtr.Zero, out var instance) < 0)
            {
                return false;
            }

            var endpoint = (CoreAudioInterop.IAudioEndpointVolume)instance;

            var callback = new EndpointVolumeCallback(this);
            if (endpoint.RegisterControlChangeNotify(callback) < 0)
            {
                return false;
            }

            UnbindEndpoint();
            _endpoint = endpoint;
            _endpointCallback = callback;

            // Publish the initial state so the UI is correct before any change.
            if (endpoint.GetMasterVolumeLevelScalar(out var level) >= 0
                && endpoint.GetMute(out var mute) >= 0)
            {
                Publish(new VolumeState(level, mute, DateTimeOffset.Now));
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Audio: failed to bind default endpoint.");
            return false;
        }
    }

    private void UnbindEndpoint()
    {
        if (_endpoint is not null && _endpointCallback is not null)
        {
            try
            {
                _endpoint.UnregisterControlChangeNotify(_endpointCallback);
            }
            catch
            {
                // Endpoint already gone — nothing to unregister.
            }
        }

        _endpoint = null;
        _endpointCallback = null;
    }

    private void Publish(VolumeState state)
    {
        // Dedupe: keyboard ramps fire many callbacks with identical values.
        if (_lastPublished is { } last
            && Math.Abs(last.Level - state.Level) < 0.005f
            && last.IsMuted == state.IsMuted)
        {
            return;
        }

        _lastPublished = state;
        Current = state;
        VolumeChanged?.Invoke(state);
    }

    /// <summary>Called by the endpoint callback on an RPC thread.</summary>
    private void OnEndpointNotify(float level, bool muted)
    {
        bool muteChanged = muted != _lastNotifiedMuted;
        _lastNotifiedMuted = muted;

        if (muteChanged)
        {
            _logger.LogInformation("CoreAudio notify: mute -> {Muted} (level {Level:P0}).", muted, level);
        }

        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            Publish(new VolumeState(level, muted, DateTimeOffset.Now));
        }
    }

    private bool _lastNotifiedMuted;

    internal void LogCallbackFault(Exception ex)
        => _logger.LogWarning(ex, "CoreAudio: endpoint callback faulted while reading the notification.");

    /// <summary>Called by the notification client when the default device changes.</summary>
    private void OnDefaultDeviceChanged()
    {
        Task.Run(() =>
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                if (TryBindDefaultEndpoint())
                {
                    _logger.LogInformation("Audio: default endpoint changed — rebound volume monitoring.");
                }
                else
                {
                    _logger.LogInformation("Audio: default endpoint changed but no render endpoint is available.");
                    UnbindEndpoint();
                }
            }
        });
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
            _resilienceTimer?.Dispose();
            _resilienceTimer = null;

            if (_enumerator is not null && _notificationClient is not null)
            {
                try
                {
                    _enumerator.UnregisterEndpointNotificationCallback(_notificationClient);
                }
                catch
                {
                    // Best effort during shutdown.
                }
            }

            UnbindEndpoint();
        }
    }

    private sealed class EndpointVolumeCallback(CoreAudioVolumeMonitor owner) : CoreAudioInterop.IAudioEndpointVolumeCallback
    {
        void CoreAudioInterop.IAudioEndpointVolumeCallback.OnNotify(IntPtr notifyData)
        {
            if (notifyData == IntPtr.Zero)
            {
                return;
            }

            try
            {
                var data = Marshal.PtrToStructure<CoreAudioInterop.AudioVolumeNotificationData>(notifyData);
                owner.OnEndpointNotify(data.MasterVolume, data.Muted);
            }
            catch (Exception ex)
            {
                owner.LogCallbackFault(ex);
            }
        }
    }

    private sealed class NotificationClient(CoreAudioVolumeMonitor owner) : CoreAudioInterop.IMMNotificationClient
    {
        void CoreAudioInterop.IMMNotificationClient.OnDeviceStateChanged(string deviceId, int newState)
        {
        }

        void CoreAudioInterop.IMMNotificationClient.OnDeviceAdded(string deviceId)
        {
        }

        void CoreAudioInterop.IMMNotificationClient.OnDeviceRemoved(string deviceId)
        {
        }

        void CoreAudioInterop.IMMNotificationClient.OnDefaultEndpointChanged(int dataFlow, int role, string defaultDeviceId)
        {
            if (dataFlow == CoreAudioInterop.EDataFlowRender)
            {
                owner.OnDefaultDeviceChanged();
            }
        }

        void CoreAudioInterop.IMMNotificationClient.OnPropertyValueChanged(string deviceId, CoreAudioInterop.PropertyKey key)
        {
        }
    }
}
