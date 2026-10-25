namespace GrumphySwarm.Core.Monitors;

// ======================================================================
// System monitor contracts. Interfaces live in Core so view models and tests
// depend on abstractions only; the App layer provides Windows implementations
// (Core Audio, WMI/DDC, GSMTC). Events may be raised from background threads —
// consumers marshal to their own thread.
// ======================================================================

/// <summary>Master volume snapshot of the default audio output endpoint.</summary>
/// <param name="Level">0..1 scalar level.</param>
/// <param name="IsMuted">Endpoint mute state.</param>
/// <param name="Timestamp">When the state was observed.</param>
public sealed record VolumeState(float Level, bool IsMuted, DateTimeOffset Timestamp)
{
    public int Percent => Math.Clamp((int)Math.Round(Level * 100), 0, 100);
}

/// <summary>Windows Core Audio volume monitoring (event-driven, no polling).</summary>
public interface IAudioVolumeMonitor : IDisposable
{
    /// <summary>False when no audio endpoint could be acquired.</summary>
    bool IsSupported { get; }

    /// <summary>Latest known state, if any.</summary>
    VolumeState? Current { get; }

    /// <summary>Raised on every actual volume/mute change (keys, sliders, apps).</summary>
    event Action<VolumeState>? VolumeChanged;

    /// <summary>Binds to the default render endpoint. Safe to call once at startup.</summary>
    void Start();
}

/// <summary>Display brightness snapshot.</summary>
/// <param name="Percent">0..100.</param>
/// <param name="Source">How it was read: <c>WmiEvent</c>, <c>Wmi</c> or <c>Ddc</c>.</param>
/// <param name="Timestamp">When the state was observed.</param>
public sealed record BrightnessState(int Percent, string Source, DateTimeOffset Timestamp);

/// <summary>
/// Display brightness monitoring. Support varies by hardware: internal panels
/// usually expose WMI events; many external monitors expose DDC/CI only; some
/// expose neither. Implementations must log and set <see cref="IsSupported"/>
/// accordingly instead of throwing.
/// </summary>
public interface IBrightnessMonitor : IDisposable
{
    bool IsSupported { get; }

    /// <summary>Human-readable support description for diagnostics, e.g. "unsupported: no WMI brightness, DDC/CI present".</summary>
    string SupportDescription { get; }

    BrightnessState? Current { get; }

    event Action<BrightnessState>? BrightnessChanged;

    void Start();
}

/// <summary>Playback status of a media session.</summary>
public enum MediaPlaybackStatus
{
    Unknown,
    Playing,
    Paused,
    Stopped,
    Changing,
}

/// <summary>Which transport controls the current session actually supports.</summary>
/// <param name="SupportsPlayPause">Play/pause toggle is available.</param>
/// <param name="SupportsNext">Next track is available.</param>
/// <param name="SupportsPrevious">Previous track is available.</param>
public sealed record MediaTransportControls(bool SupportsPlayPause, bool SupportsNext, bool SupportsPrevious);

/// <summary>Snapshot of the currently relevant media session.</summary>
/// <param name="Title">Track title.</param>
/// <param name="Artist">Artist line, may be empty.</param>
/// <param name="AppName">Best-effort source app name, may be empty.</param>
/// <param name="Status">Playback status.</param>
/// <param name="Controls">Supported transport controls.</param>
/// <param name="Timestamp">When the snapshot was taken.</param>
/// <param name="SourceAppUserModelId">Raw SMTC source id (used for icon lookup); may be empty.</param>
public sealed record MediaInfo(
    string Title,
    string Artist,
    string AppName,
    MediaPlaybackStatus Status,
    MediaTransportControls Controls,
    DateTimeOffset Timestamp,
    string SourceAppUserModelId = "");

/// <summary>Playback position snapshot from the system media timeline.</summary>
/// <param name="Position">Elapsed playback position as reported by the session.</param>
/// <param name="Duration">Total track duration; zero when the session does not report it.</param>
/// <param name="Timestamp">When the position was reported (for local interpolation).</param>
public sealed record MediaTimeline(TimeSpan Position, TimeSpan Duration, DateTimeOffset Timestamp);

/// <summary>Bluetooth earbud/battery status snapshot.</summary>
/// <param name="Name">Device name as reported by Windows.</param>
/// <param name="BatteryPercent">Battery level when the device reports one, else null.</param>
/// <param name="IsConnected">Connection state.</param>
public sealed record EarbudStatus(string Name, int? BatteryPercent, bool IsConnected);

/// <summary>System (laptop) battery snapshot.</summary>
/// <param name="Percent">Remaining charge 0..100.</param>
/// <param name="IsCharging">True while plugged in and charging.</param>
/// <param name="Timestamp">When the state was observed.</param>
public sealed record SystemBatteryState(int Percent, bool IsCharging, DateTimeOffset Timestamp);

/// <summary>
/// System battery monitoring via the Windows power APIs. Unsupported (quietly)
/// on machines without a battery — implementers must not throw.
/// </summary>
public interface ISystemBatteryMonitor : IDisposable
{
    bool IsSupported { get; }

    SystemBatteryState? Current { get; }

    /// <summary>Raised whenever the level or charging state changes.</summary>
    event Action<SystemBatteryState>? BatteryChanged;

    void Start();
}

/// <summary>
/// Bluetooth accessory monitoring, restricted to the user's earbud allow-list
/// (see <see cref="Media.MediaAllowList.EarbudNameTokens"/>). Implementations
/// must never throw for unpaired/absent devices; unsupported setups simply
/// publish nothing.
/// </summary>
public interface IBluetoothBatteryMonitor : IDisposable
{
    bool IsSupported { get; }

    /// <summary>Latest status per allow-listed device id.</summary>
    IReadOnlyDictionary<string, EarbudStatus> Current { get; }

    /// <summary>Raised whenever a allow-listed device's status changes.</summary>
    event Action<IReadOnlyDictionary<string, EarbudStatus>>? StatusChanged;

    void Start();
}

/// <summary>
/// Windows media session monitoring (SMTC). Only apps that publish a usable
/// system media session are visible — no window scraping, ever.
/// </summary>
public interface IMediaMonitor : IDisposable
{
    bool IsSupported { get; }

    /// <summary>Snapshot of the currently relevant session, or null when there is none.</summary>
    MediaInfo? Current { get; }

    /// <summary>Raised when metadata or playback status changes (null payload = no session).</summary>
    event Action<MediaInfo?>? MediaChanged;

    /// <summary>
    /// Raised with raw image bytes of the current track's thumbnail (PNG/JPG),
    /// or an empty array when there is none. Consumers decode on their own thread.
    /// </summary>
    event Action<byte[]>? ArtworkChanged;

    /// <summary>Raised when the playback position/timeline is re-reported.</summary>
    event Action<MediaTimeline>? TimelineChanged;

    /// <summary>Latest timeline snapshot, if any.</summary>
    MediaTimeline? Timeline { get; }

    /// <summary>Transport actions; return false when unsupported or failed.</summary>
    Task<bool> TryPlayPauseAsync();

    Task<bool> TryNextAsync();

    Task<bool> TryPreviousAsync();

    void Start();
}
