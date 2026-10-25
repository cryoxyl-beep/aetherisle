using System.IO;
using GrumphySwarm.Core.Media;
using GrumphySwarm.Core.Monitors;
using Microsoft.Extensions.Logging;
using Windows.Media.Control;

namespace GrumphySwarm.App.Services.Media;

/// <summary>
/// Live media monitoring through the Windows System Media Transport Controls
/// (GlobalSystemMediaTransportControlsSessionManager). Only sessions that apps
/// publish to SMTC are visible — browsers, players and galleries that expose
/// them show up; anything that does not is simply absent. No window scraping.
///
/// Session choice: prefer the user's current session when it is actively
/// playing, else the first playing session, else the current session.
/// </summary>
public sealed class WindowsMediaMonitor : IMediaMonitor
{
    private readonly ILogger<WindowsMediaMonitor> _logger;
    private readonly object _gate = new();

    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;
    private bool _disposed;
    private MediaInfo? _lastPublished;

    public WindowsMediaMonitor(ILogger<WindowsMediaMonitor> logger)
    {
        _logger = logger;
    }

    public bool IsSupported => _manager is not null;

    public MediaInfo? Current { get; private set; }

    public event Action<MediaInfo?>? MediaChanged;

    public event Action<byte[]>? ArtworkChanged;

    public event Action<MediaTimeline>? TimelineChanged;

    public MediaTimeline? Timeline { get; private set; }

    private byte[]? _lastArtworkBytes;
    private bool _artworkAllowed;
    private bool _artworkAllowedLogged;

    public void Start()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                lock (_gate)
                {
                    if (_disposed)
                    {
                        return;
                    }

                    _manager = manager;
                    manager.SessionsChanged += OnSessionsChanged;
                    manager.CurrentSessionChanged += OnCurrentSessionChanged;
                }

                _logger.LogInformation("Media: SMTC session manager connected (event-driven).");
                RebindSession();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Media: SMTC unavailable — media monitoring disabled.");
            }
        });
    }

    private void OnSessionsChanged(GlobalSystemMediaTransportControlsSessionManager sender, object? args)
        => RebindSession();

    private void OnCurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender, object? args)
        => RebindSession();

    private void RebindSession()
    {
        GlobalSystemMediaTransportControlsSessionManager? manager;
        lock (_gate)
        {
            manager = _manager;
        }

        if (manager is null)
        {
            return;
        }

        try
        {
            var chosen = PickSession(manager);
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                if (!ReferenceEquals(_session, chosen))
                {
                    if (_session is not null)
                    {
                        _session.MediaPropertiesChanged -= OnSessionPropertyChanged;
                        _session.PlaybackInfoChanged -= OnSessionPropertyChanged;
                        _session.TimelinePropertiesChanged -= OnSessionTimelineChanged;
                    }

                    _session = chosen;

                    if (chosen is not null)
                    {
                        chosen.MediaPropertiesChanged += OnSessionPropertyChanged;
                        chosen.PlaybackInfoChanged += OnSessionPropertyChanged;
                        chosen.TimelinePropertiesChanged += OnSessionTimelineChanged;
                    }
                }
            }

            if (chosen is null)
            {
                Timeline = null;
                Publish(null);
            }
            else
            {
                PublishTimeline(chosen);
                _ = PublishSnapshotAsync(chosen);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Media: session rebind failed, will retry on next change.");
        }
    }

    /// <summary>Session selection: current-if-playing, else first playing, else current.</summary>
    private static GlobalSystemMediaTransportControlsSession? PickSession(GlobalSystemMediaTransportControlsSessionManager manager)
    {
        var current = manager.GetCurrentSession();
        if (current is null)
        {
            return manager.GetSessions().FirstOrDefault();
        }

        if (IsPlaying(current))
        {
            return current;
        }

        var playing = manager.GetSessions().FirstOrDefault(IsPlaying);
        return playing ?? current;
    }

    private static bool IsPlaying(GlobalSystemMediaTransportControlsSession session)
    {
        try
        {
            return session.GetPlaybackInfo().PlaybackStatus
                is GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
        }
        catch
        {
            return false;
        }
    }

    private void OnSessionPropertyChanged(GlobalSystemMediaTransportControlsSession sender, object? args)
        => _ = PublishSnapshotAsync(sender);

    private void OnSessionTimelineChanged(GlobalSystemMediaTransportControlsSession sender, object? args)
        => PublishTimeline(sender);

    private void PublishTimeline(GlobalSystemMediaTransportControlsSession session)
    {
        try
        {
            var timeline = session.GetTimelineProperties();
            var snapshot = new MediaTimeline(timeline.Position, timeline.EndTime, DateTimeOffset.Now);

            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                if (Timeline is not null
                    && Timeline.Position == snapshot.Position
                    && Timeline.Duration == snapshot.Duration)
                {
                    return;
                }

                Timeline = snapshot;
            }

            TimelineChanged?.Invoke(snapshot);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Media: timeline read failed (kept previous).");
        }
    }

    private async Task PublishSnapshotAsync(GlobalSystemMediaTransportControlsSession session)
    {
        try
        {
            var playback = session.GetPlaybackInfo();
            var controls = playback.Controls;
            var properties = await session.TryGetMediaPropertiesAsync();

            // Artwork gating happens HERE on the raw app-user-model id (e.g.
            // "AppleInc.AppleMusicWin_…"), because the friendly display name
            // ("Apple Music") would never match the allow-list tokens.
            var artworkAllowed = MediaAllowList.IsAlbumArtAllowed(session.SourceAppUserModelId);
            if (artworkAllowed != _artworkAllowedLogged)
            {
                _artworkAllowedLogged = artworkAllowed;
                _logger.LogInformation(
                    "Media: album art {State} for {Source}.",
                    artworkAllowed ? "enabled" : "disabled (app not on the allow-list)",
                    session.SourceAppUserModelId);
            }

            _artworkAllowed = artworkAllowed;

            var info = new MediaInfo(
                Title: properties?.Title ?? string.Empty,
                Artist: properties?.Artist ?? string.Empty,
                AppName: FriendlyAppName(session.SourceAppUserModelId),
                Status: MapStatus(playback.PlaybackStatus),
                new MediaTransportControls(
                    SupportsPlayPause: controls.IsPlayEnabled || controls.IsPauseEnabled,
                    SupportsNext: controls.IsNextEnabled,
                    SupportsPrevious: controls.IsPreviousEnabled),
                DateTimeOffset.Now,
                session.SourceAppUserModelId);

            Publish(info);
            PublishTimeline(session);
            await PublishArtworkAsync(properties);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Media: snapshot read failed, keeping previous state.");
        }
    }

    private async Task PublishArtworkAsync(GlobalSystemMediaTransportControlsSessionMediaProperties? properties)
    {
        var handler = ArtworkChanged;
        if (handler is null)
        {
            return;
        }

        // Not on the allow-list → never publish artwork, and clear leftovers.
        if (!_artworkAllowed)
        {
            if (_lastArtworkBytes is not null)
            {
                _lastArtworkBytes = null;
                handler(Array.Empty<byte>());
            }

            return;
        }

        try
        {
            if (properties?.Thumbnail is null)
            {
                if (_lastArtworkBytes is not null)
                {
                    _lastArtworkBytes = null;
                    handler(Array.Empty<byte>());
                }

                return;
            }

            using var winrtStream = await properties.Thumbnail.OpenReadAsync();
            using var stream = winrtStream.AsStreamForRead();
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer);
            var bytes = buffer.ToArray();

            // Dedupe: metadata changes re-deliver the same thumbnail.
            if (_lastArtworkBytes is not null
                && bytes.Length == _lastArtworkBytes.Length
                && bytes.AsSpan().SequenceEqual(_lastArtworkBytes))
            {
                return;
            }

            _lastArtworkBytes = bytes;
            handler(bytes);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Media: artwork read failed (kept previous).");
        }
    }

    private void Publish(MediaInfo? info)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            if (_lastPublished is not null && info is not null && _lastPublished.Equals(info))
            {
                return;
            }

            _lastPublished = info;
            Current = info;
        }

        MediaChanged?.Invoke(info);
    }

    private static MediaPlaybackStatus MapStatus(GlobalSystemMediaTransportControlsSessionPlaybackStatus status) => status switch
    {
        GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing => MediaPlaybackStatus.Playing,
        GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused => MediaPlaybackStatus.Paused,
        GlobalSystemMediaTransportControlsSessionPlaybackStatus.Stopped => MediaPlaybackStatus.Stopped,
        GlobalSystemMediaTransportControlsSessionPlaybackStatus.Changing => MediaPlaybackStatus.Changing,
        _ => MediaPlaybackStatus.Unknown,
    };

    private static string FriendlyAppName(string? sourceAppUserModelId)
    {
        if (string.IsNullOrWhiteSpace(sourceAppUserModelId))
        {
            return string.Empty;
        }

        // Pretty names for the apps people actually play media from.
        var lower = sourceAppUserModelId.ToLowerInvariant();
        if (lower.Contains("applemusic"))
        {
            return "Apple Music";
        }

        if (lower.Contains("spotify"))
        {
            return "Spotify";
        }

        if (lower.Contains("msedge") || lower.Contains("microsoftedge"))
        {
            return "Edge";
        }

        if (lower.Contains("chrome"))
        {
            return "Chrome";
        }

        if (lower.Contains("firefox"))
        {
            return "Firefox";
        }

        // User model ids look like "chrome.exe" or "com.company.app!App";
        // take the executable-ish head and strip the extension.
        var head = sourceAppUserModelId.Split('!')[0];
        if (head.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            head = head[..^4];
        }

        if (head.Length == 0)
        {
            return string.Empty;
        }

        return char.ToUpperInvariant(head[0]) + head[1..];
    }

    public async Task<bool> TryPlayPauseAsync()
    {
        var session = _session;
        if (session is null)
        {
            return false;
        }

        try
        {
            return await session.TryTogglePlayPauseAsync();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Media: play/pause failed.");
            return false;
        }
    }

    public async Task<bool> TryNextAsync()
    {
        var session = _session;
        if (session is null)
        {
            return false;
        }

        try
        {
            return await session.TrySkipNextAsync();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Media: next failed.");
            return false;
        }
    }

    public async Task<bool> TryPreviousAsync()
    {
        var session = _session;
        if (session is null)
        {
            return false;
        }

        try
        {
            return await session.TrySkipPreviousAsync();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Media: previous failed.");
            return false;
        }
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

            if (_session is not null)
            {
                _session.MediaPropertiesChanged -= OnSessionPropertyChanged;
                _session.PlaybackInfoChanged -= OnSessionPropertyChanged;
                _session.TimelinePropertiesChanged -= OnSessionTimelineChanged;
            }

            if (_manager is not null)
            {
                _manager.SessionsChanged -= OnSessionsChanged;
                _manager.CurrentSessionChanged -= OnCurrentSessionChanged;
            }

            _session = null;
            _manager = null;
        }
    }
}
