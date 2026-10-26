using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GrumphySwarm.Core.Audio;
using GrumphySwarm.Core.Media;
using GrumphySwarm.Core.Monitors;
using GrumphySwarm.Core.Settings;
using GrumphySwarm.Core.State;
using GrumphySwarm.Core.Time;
using GrumphySwarm.App.Services.Audio;
using Microsoft.Extensions.Logging;
using MediaTimeline = GrumphySwarm.Core.Monitors.MediaTimeline;

namespace GrumphySwarm.App.ViewModels;

/// <summary>
/// View model for the Dynamic Island: interaction state (via the machine),
/// clock, tabs, transient system status (volume/brightness), the earbud
/// battery capsule, and live media (metadata, allow-listed artwork, timeline).
///
/// Monitor events arrive on background threads; everything is marshaled to the
/// UI dispatcher here. Timers follow the generation-token pattern so rapid
/// changes never leave stale timers or stuck overlays behind.
/// </summary>
public partial class NotchViewModel : ObservableObject, IDisposable
{
    private const string PinGlyphValue = "\uE718";
    private const string UnpinGlyphValue = "\uE77A";
    private static readonly TimeSpan BatteryCapsuleDuration = TimeSpan.FromSeconds(5);

    private readonly NotchStateMachine _machine;
    private readonly NotchSettings _settings;
    private readonly ISettingsStore _settingsStore;
    private readonly IAudioVolumeMonitor _audioMonitor;
    private readonly IBrightnessMonitor _brightnessMonitor;
    private readonly IMediaMonitor _mediaMonitor;
    private readonly IBluetoothBatteryMonitor _bluetoothMonitor;
    private readonly ISystemBatteryMonitor _systemBatteryMonitor;
    private readonly Services.Telemetry.SystemStatsMonitor _statsMonitor;
    private readonly Services.Weather.WeatherMonitor _weatherMonitor;
    private readonly Services.Media.AppIconService _appIcons = new();
    private readonly ILogger<NotchViewModel> _logger;
    private readonly Dispatcher _dispatcher;
    private readonly TransientStatusController _transient = new();

    private readonly DispatcherTimer _collapseTimer;
    private readonly DispatcherTimer _clockTimer;
    private readonly DispatcherTimer _transientTimer;
    private readonly DispatcherTimer _playbackTimer;
    private readonly DispatcherTimer _batteryTimer;

    private int _pendingCollapseGeneration;
    private int _pendingTransientGeneration;
    private DateTime _lastDate = DateTime.MinValue;
    private bool _volumeEventsSeen;
    private bool _brightnessEventsSeen;

    // Playback clock state (real SMTC timeline + local interpolation).
    private MediaTimeline? _timeline;
    private bool _isPlaying;

    // Battery capsule generation token: only the current generation can hide it.
    private int _batteryGeneration;

    public NotchViewModel(
        NotchStateMachine machine,
        NotchSettings settings,
        ISettingsStore settingsStore,
        IAudioVolumeMonitor audioMonitor,
        IBrightnessMonitor brightnessMonitor,
        IMediaMonitor mediaMonitor,
        IBluetoothBatteryMonitor bluetoothMonitor,
        ISystemBatteryMonitor systemBatteryMonitor,
        Services.Telemetry.SystemStatsMonitor statsMonitor,
        Services.Weather.WeatherMonitor weatherMonitor,
        AudioSpectrumMonitor spectrum,
        AgentsViewModel agents,
        ILogger<NotchViewModel> logger)
    {
        _machine = machine;
        _settings = settings;
        _settingsStore = settingsStore;
        _audioMonitor = audioMonitor;
        _brightnessMonitor = brightnessMonitor;
        _mediaMonitor = mediaMonitor;
        _bluetoothMonitor = bluetoothMonitor;
        _systemBatteryMonitor = systemBatteryMonitor;
        _statsMonitor = statsMonitor;
        _weatherMonitor = weatherMonitor;
        _logger = logger;
        _dispatcher = Dispatcher.CurrentDispatcher;
        Agents = agents;
        Spectrum = spectrum;
        _dragOffsetX = settings.DragOffsetX;
        _dragOffsetY = settings.DragOffsetY;

        _collapseTimer = new DispatcherTimer(DispatcherPriority.Normal, _dispatcher);
        _collapseTimer.Tick += (_, _) =>
        {
            _collapseTimer.Stop();
            _machine.CollapseElapsed(_pendingCollapseGeneration);
        };

        _transientTimer = new DispatcherTimer(DispatcherPriority.Normal, _dispatcher);
        _transientTimer.Tick += (_, _) =>
        {
            _transientTimer.Stop();
            _transient.Expire(_pendingTransientGeneration);
        };

        _batteryTimer = new DispatcherTimer(DispatcherPriority.Normal, _dispatcher)
        {
            Interval = BatteryCapsuleDuration,
        };
        _batteryTimer.Tick += (_, _) =>
        {
            _batteryTimer.Stop();
            IsBatteryVisible = false;
        };

        _playbackTimer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher)
        {
            Interval = TimeSpan.FromSeconds(1),
        };
        _playbackTimer.Tick += (_, _) => UpdatePlaybackTexts();

        _clockTimer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher)
        {
            Interval = TimeSpan.FromSeconds(5),
        };
        _clockTimer.Tick += (_, _) => UpdateClock(DateTime.Now);

        _machine.Transitioned += OnMachineTransitioned;
        _machine.CollapseScheduled += OnCollapseScheduled;
        _machine.CollapseCancelled += OnCollapseCancelled;

        _transient.Hidden += () => _dispatcher.BeginInvoke(() =>
        {
            TransientVisible = false;
            VolumeTransient = false;
            BrightnessTransient = false;
        });
        _transient.ExpirationScheduled += OnTransientExpirationScheduled;

        _audioMonitor.VolumeChanged += OnVolumeChanged;
        _brightnessMonitor.BrightnessChanged += OnBrightnessChanged;
        _mediaMonitor.MediaChanged += OnMediaChanged;
        _mediaMonitor.ArtworkChanged += OnArtworkChanged;
        _mediaMonitor.TimelineChanged += OnTimelineChanged;
        _bluetoothMonitor.StatusChanged += OnEarbudStatusChanged;
        _systemBatteryMonitor.BatteryChanged += OnSystemBatteryChanged;
        _statsMonitor.SnapshotAvailable += OnStatsSnapshot;
        _statsMonitor.MachineInfoAvailable += OnMachineInfo;
        _weatherMonitor.WeatherChanged += OnWeatherChanged;
        _weatherMonitor.FetchFailed += OnWeatherFailed;

        ReducedMotion = settings.ReducedMotion;
        UpdateClock(DateTime.Now);
        _clockTimer.Start();
    }

    /// <summary>Agents workspace sub view model (profiles only in this milestone).</summary>
    public AgentsViewModel Agents { get; }

    /// <summary>Live system-audio spectrum for the visualizers.</summary>
    public AudioSpectrumMonitor Spectrum { get; }

    // ------------------------------------------------------------ UI state

    [ObservableProperty]
    private NotchState state = NotchState.Collapsed;

    [ObservableProperty]
    private bool isPinned;

    [ObservableProperty]
    private string pinGlyph = PinGlyphValue;

    [ObservableProperty]
    private string pinToolTip = "Pin notch";

    [ObservableProperty]
    private bool reducedMotion;

    [ObservableProperty]
    private NotchTab selectedTab = NotchTab.Nook;

    [ObservableProperty]
    private string timeText = "--:--";

    [ObservableProperty]
    private NotchCalendarModel? calendar;

    [ObservableProperty]
    private string shortDateText = string.Empty;

    // Transient system status (pill center / panel header center).
    [ObservableProperty]
    private bool transientVisible;

    [ObservableProperty]
    private string transientGlyph = string.Empty;

    [ObservableProperty]
    private string transientText = string.Empty;

    // Volume (pill center): icon turns into a live percentage during changes
    // and tints red while muted.
    [ObservableProperty]
    private string volumeGlyph = StatusPresenters.VolumeGlyphOn;

    [ObservableProperty]
    private bool volumeMuted;

    [ObservableProperty]
    private bool volumeTransient;

    [ObservableProperty]
    private string volumeTransientText = string.Empty;

    [ObservableProperty]
    private bool brightnessTransient;

    // System battery (pill center, between volume and clock).
    [ObservableProperty]
    private bool systemBatterySupported;

    [ObservableProperty]
    private int? systemBatteryPercent;

    [ObservableProperty]
    private bool systemBatteryCharging;

    public string SystemBatteryText => SystemBatteryPercent is { } percent ? $"{percent}%" : string.Empty;

    // Media.
    [ObservableProperty]
    private string mediaTitle = "Nothing playing";

    [ObservableProperty]
    private string mediaSubtitle = string.Empty;

    [ObservableProperty]
    private string mediaArtistText = string.Empty;

    [ObservableProperty]
    private string mediaTotalText = string.Empty;

    [ObservableProperty]
    private string mediaStatusText = string.Empty;

    // Source-app badge on the Nook artwork: real brand logos for Apple Music /
    // Spotify, extracted app icon for other sources.
    [ObservableProperty]
    private bool mediaSourceBadgeVisible;

    [ObservableProperty]
    private bool isAppleMusicSource;

    [ObservableProperty]
    private bool isSpotifySource;

    [ObservableProperty]
    private string mediaSourceBadgeBrush = "Transparent";

    [ObservableProperty]
    private string mediaSourceBadgeToolTip = string.Empty;

    [ObservableProperty]
    private ImageSource? mediaSourceAppIcon;

    /// <summary>True while the session is actively playing (image toggle for play/pause).</summary>
    [ObservableProperty]
    private bool isMediaPlaying;

    [ObservableProperty]
    private string playPauseGlyph = StatusPresenters.PlayGlyph;

    [ObservableProperty]
    private bool canPlayPause;

    [ObservableProperty]
    private bool canNext;

    [ObservableProperty]
    private bool canPrevious;

    [ObservableProperty]
    private ImageSource? mediaArtwork;

    [ObservableProperty]
    private bool hasArtwork;

    [ObservableProperty]
    private bool hasActiveMedia;

    [ObservableProperty]
    private string elapsedText = string.Empty;

    [ObservableProperty]
    private string remainingText = string.Empty;

    [ObservableProperty]
    private double playbackFraction;

    // Earbud battery capsule.
    [ObservableProperty]
    private bool isBatteryVisible;

    [ObservableProperty]
    private string batteryDeviceName = "Earbuds";

    [ObservableProperty]
    private string batteryText = string.Empty;

    [ObservableProperty]
    private int? batteryPercent;

    // Stats tab.
    [ObservableProperty]
    private double? cpuPercent;

    [ObservableProperty]
    private double? gpuPercent;

    [ObservableProperty]
    private double? diskPercent;

    partial void OnCpuPercentChanged(double? value) => OnPropertyChanged(nameof(CpuText));

    partial void OnGpuPercentChanged(double? value) => OnPropertyChanged(nameof(GpuText));

    partial void OnDiskPercentChanged(double? value) => OnPropertyChanged(nameof(DiskText));

    [ObservableProperty]
    private double cpuFraction;

    [ObservableProperty]
    private double gpuFraction;

    [ObservableProperty]
    private double diskFraction;

    [ObservableProperty]
    private string ramText = "—";

    [ObservableProperty]
    private string ramFreeText = string.Empty;

    [ObservableProperty]
    private double ramFraction;

    [ObservableProperty]
    private string netDownText = "0 B/s";

    [ObservableProperty]
    private string netUpText = "0 B/s";

    [ObservableProperty]
    private string uptimeText = "—";

    [ObservableProperty]
    private string diskUsageText = "—";

    [ObservableProperty]
    private string cpuName = "CPU";

    [ObservableProperty]
    private string gpuName = "GPU";

    [ObservableProperty]
    private string cpuBadgeText = string.Empty;

    [ObservableProperty]
    private IReadOnlyList<AppUsage> topApps = [];

    // Weather card (Nook tab) — real fetched data only.
    [ObservableProperty]
    private bool hasWeather;

    [ObservableProperty]
    private string weatherTempText = "—";

    [ObservableProperty]
    private string weatherConditionText = "Checking…";

    [ObservableProperty]
    private string weatherCityText = string.Empty;

    [ObservableProperty]
    private string weatherHighText = "—";

    [ObservableProperty]
    private string weatherLowText = "—";

    [ObservableProperty]
    private WeatherIconKind weatherIconKind = WeatherIconKind.Unknown;

    // Internet speed panel removed (user request) — Stats shows top apps
    // across the full right column instead.

    private void OnWeatherChanged(WeatherState weather)
    {
        _dispatcher.BeginInvoke(() =>
        {
            var (label, _) = WeatherCodes.Map(weather.WeatherCode);
            HasWeather = true;
            WeatherTempText = StatsPresenters.FormatTemperature(weather.TempC);
            WeatherConditionText = label;
            WeatherCityText = weather.City;
            WeatherHighText = StatsPresenters.FormatTemperature(weather.HighC);
            WeatherLowText = StatsPresenters.FormatTemperature(weather.LowC);
            WeatherIconKind = WeatherCodes.IconKind(weather.WeatherCode, weather.IsDay);
        });
    }

    private void OnWeatherFailed()
    {
        _dispatcher.BeginInvoke(() =>
        {
            if (HasWeather)
            {
                return; // keep the last real observation until fresh data arrives
            }

            WeatherConditionText = "Weather unavailable";
        });
    }

    private void OnMachineInfo(MachineInfo machine)
    {
        _dispatcher.BeginInvoke(() =>
        {
            if (!string.IsNullOrEmpty(machine.CpuName))
            {
                CpuName = machine.CpuName;
            }

            if (!string.IsNullOrEmpty(machine.GpuName))
            {
                GpuName = machine.GpuName.Length > 18 ? machine.GpuName[..18].Trim() : machine.GpuName;
            }

            CpuBadgeText = string.IsNullOrEmpty(machine.CpuClockText)
                ? $"{machine.LogicalProcessorCount} threads"
                : $"{machine.CpuClockText} · {machine.LogicalProcessorCount}T";
        });
    }

    /// <summary>Bound two-way by the Nook tab chip.</summary>
    public bool IsNookTab
    {
        get => SelectedTab == NotchTab.Nook;
        set
        {
            if (value)
            {
                SelectedTab = NotchTab.Nook;
            }
        }
    }

    /// <summary>Bound two-way by the Agents tab chip.</summary>
    public bool IsAgentsTab
    {
        get => SelectedTab == NotchTab.Agents;
        set
        {
            if (value)
            {
                SelectedTab = NotchTab.Agents;
            }
        }
    }

    /// <summary>Bound two-way by the Stats tab chip.</summary>
    public bool IsStatsTab
    {
        get => SelectedTab == NotchTab.Stats;
        set
        {
            if (value)
            {
                SelectedTab = NotchTab.Stats;
            }
        }
    }

    /// <summary>
    /// Explicit entry into the media player view: clicking the pill artwork,
    /// clicking the Nook media card, or Shift+scroll over the island.
    /// </summary>
    public void OpenMediaPlayer() => SelectedTab = NotchTab.Media;

    /// <summary>Runs the playback clock only while the player view is visible.</summary>
    partial void OnSelectedTabChanged(NotchTab value)
    {
        if (value == NotchTab.Media)
        {
            UpdatePlaybackTexts();
            _playbackTimer.Start();
        }
        else
        {
            _playbackTimer.Stop();
        }
    }

    /// <summary>Raised on every state change (used by the window to arm the outside-click dismissal hook).</summary>
    public event Action<NotchState>? StateChanged;

    // ------------------------------------------------------------ island drag

    private double _dragOffsetX;
    private double _dragOffsetY;

    /// <summary>User drag offset from top-center, in DIP (persisted in settings).</summary>
    public double DragOffsetX => _dragOffsetX;

    public double DragOffsetY => _dragOffsetY;

    /// <summary>Called by the window while the island is dragged; updates and re-clamps live.</summary>
    public void UpdateDragOffset(double x, double y)
    {
        _dragOffsetX = x;
        _dragOffsetY = y;
        _settings.DragOffsetX = x;
        _settings.DragOffsetY = y;
    }

    /// <summary>Called when a drag finishes; persists the final offset.</summary>
    public void SaveDragOffset()
    {
        try
        {
            _settingsStore.Save(_settings);
            _logger.LogInformation("Island position saved ({X:F0}, {Y:F0}).", _dragOffsetX, _dragOffsetY);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Island position could not be persisted.");
        }
    }

    // ------------------------------------------------------------ input (window / tray)

    public void PointerEntered() => _machine.PointerEntered();

    public void PointerLeft() => _machine.PointerLeft();

    public void DismissFromOutsideClick() => _machine.Dismiss();

    public void RequestExpandFromTray() => _machine.RequestExpand();

    public void RequestCollapseFromTray() => _machine.RequestCollapse();

    public void BeginShutdown() => _machine.BeginShutdown();

    [RelayCommand]
    private void TogglePin() => _machine.TogglePin();

    /// <summary>
    /// Single click on the bare island / pill artwork: opens the media player
    /// view when a session is active. Hover-expand itself always shows the
    /// Nook/Agents/Stats tabs — the player is always an explicit action.
    /// </summary>
    public void HandleIslandClick()
    {
        if (HasActiveMedia)
        {
            OpenMediaPlayer();
        }
    }

    /// <summary>
    /// Toggles the earbud battery capsule (middle mouse button or double tap).
    /// Repeated toggles restart the timeout via a generation token.
    /// </summary>
    public void ToggleBattery()
    {
        if (IsBatteryVisible)
        {
            _batteryGeneration++;
            _batteryTimer.Stop();
            IsBatteryVisible = false;
            return;
        }

        // Only devices CURRENTLY connected are eligible — paired-but-
        // disconnected devices keep cached battery values that must not show.
        var status = _bluetoothMonitor.Current.Values
            .Where(d => d.IsConnected)
            .OrderByDescending(d => d.BatteryPercent.HasValue)
            .ThenBy(d => d.Name, StringComparer.CurrentCulture)
            .FirstOrDefault();

        if (status is null || string.IsNullOrEmpty(status.Name))
        {
            // No allow-listed device is connected right now.
            BatteryDeviceName = "No earbuds";
            BatteryText = "Airdopes 138 / Zeb Duke not connected";
            BatteryPercent = null;
        }
        else
        {
            BatteryDeviceName = StatusPresenters.EarbudDisplayName(status.Name);
            BatteryText = StatusPresenters.EarbudText(status);
            BatteryPercent = status.BatteryPercent;
        }

        IsBatteryVisible = true;
        _batteryGeneration++;
        _batteryTimer.Stop();
        _batteryTimer.Start();

        _logger.LogInformation("Battery capsule shown (device: {Device}).", BatteryDeviceName);
    }

    // ------------------------------------------------------------ media commands

    [RelayCommand]
    private async Task PlayPauseAsync()
    {
        if (!await _mediaMonitor.TryPlayPauseAsync())
        {
            _logger.LogDebug("Media: play/pause not available right now.");
        }
    }

    [RelayCommand]
    private async Task NextTrackAsync()
    {
        if (!await _mediaMonitor.TryNextAsync())
        {
            _logger.LogDebug("Media: next not available right now.");
        }
    }

    [RelayCommand]
    private async Task PreviousTrackAsync()
    {
        if (!await _mediaMonitor.TryPreviousAsync())
        {
            _logger.LogDebug("Media: previous not available right now.");
        }
    }

    // ------------------------------------------------------------ machine events

    private void OnMachineTransitioned(NotchTransition transition)
    {
        State = transition.To;
        IsPinned = transition.To == NotchState.Pinned;
        PinGlyph = IsPinned ? UnpinGlyphValue : PinGlyphValue;
        PinToolTip = IsPinned ? "Unpin notch" : "Pin notch";
        OnPropertyChanged(nameof(LockGlyph));

        _logger.LogInformation(
            "Notch {From} -> {To} ({Reason}).",
            transition.From,
            transition.To,
            transition.Reason);

        // Every hover-expand lands on the tab view (Nook/Agents/Stats); the
        // media player only ever opens through an explicit action.
        if (transition.To is NotchState.Expanded or NotchState.Pinned
            && SelectedTab == NotchTab.Media)
        {
            SelectedTab = NotchTab.Nook;
        }

        StateChanged?.Invoke(transition.To);
    }

    private void OnCollapseScheduled(int generation, int delayMs)
    {
        _pendingCollapseGeneration = generation;
        _collapseTimer.Stop();
        _collapseTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(1, delayMs));
        _collapseTimer.Start();
    }

    private void OnCollapseCancelled()
    {
        _collapseTimer.Stop();
    }

    // ------------------------------------------------------------ monitor events

    private void OnVolumeChanged(VolumeState state)
    {
        _dispatcher.BeginInvoke(() =>
        {
            var percent = state.Percent;

            if (state.IsMuted != VolumeMuted)
            {
                _logger.LogInformation("Volume mute state -> {Muted} ({Percent}%).", state.IsMuted, percent);
            }

            VolumeMuted = state.IsMuted;
            VolumeGlyph = StatusPresenters.VolumeGlyph(state.IsMuted, percent);

            // The first event only seeds the icon (launch-time snapshot); later
            // changes flip the icon into a live percentage, then back.
            if (!_volumeEventsSeen)
            {
                _volumeEventsSeen = true;
                return;
            }

            // Muted volume keeps the (red) icon; level changes show the number.
            VolumeTransientText = state.IsMuted ? string.Empty : $"{percent}%";
            VolumeTransient = true;
            TransientVisible = true;
            TransientGlyph = VolumeGlyph;
            TransientText = StatusPresenters.VolumeText(state.IsMuted, percent);
            _transient.Show(TransientKind.Volume, _settings.TransientStatusMs);
        });
    }

    private void OnBrightnessChanged(BrightnessState state)
    {
        _dispatcher.BeginInvoke(() =>
        {
            if (!_brightnessEventsSeen)
            {
                _brightnessEventsSeen = true;
                return;
            }

            // Brightness briefly replaces the battery slot in the pill center.
            BrightnessTransient = true;
            ShowTransient(
                TransientKind.Brightness,
                StatusPresenters.BrightnessGlyph,
                StatusPresenters.BrightnessText(state.Percent));
        });
    }

    private void OnSystemBatteryChanged(SystemBatteryState state)
    {
        _dispatcher.BeginInvoke(() => SeedSystemBattery(state));
    }

    private void OnStatsSnapshot(SystemStatsSnapshot snapshot)
    {
        _dispatcher.BeginInvoke(() =>
        {
            CpuPercent = snapshot.CpuPercent;
            GpuPercent = snapshot.GpuPercent;
            DiskPercent = snapshot.DiskPercent;
            CpuFraction = StatsPresenters.BarFraction(snapshot.CpuPercent);
            GpuFraction = StatsPresenters.BarFraction(snapshot.GpuPercent);
            DiskFraction = StatsPresenters.BarFraction(snapshot.DiskPercent);
            RamText = StatsPresenters.FormatMemory(snapshot.RamUsedGb, snapshot.RamTotalGb);
            RamFreeText = snapshot.RamTotalGb > snapshot.RamUsedGb
                ? $"{snapshot.RamTotalGb - snapshot.RamUsedGb:0.0} GB free"
                : string.Empty;
            RamFraction = StatsPresenters.MemoryFraction(snapshot.RamUsedGb, snapshot.RamTotalGb);
            NetDownText = StatsPresenters.FormatBytesPerSecond(snapshot.NetDownBytesPerSec);
            NetUpText = StatsPresenters.FormatBytesPerSecond(snapshot.NetUpBytesPerSec);
            UptimeText = StatsPresenters.FormatUptime(snapshot.Uptime);
            DiskUsageText = StatsPresenters.FormatDisk(snapshot.DiskUsedGb, snapshot.DiskTotalGb);
            TopApps = snapshot.TopApps;
        });
    }

    /// <summary>Formatted CPU/GPU/disk percent for XAML.</summary>
    public string CpuText => StatsPresenters.FormatPercent(CpuPercent);

    public string GpuText => StatsPresenters.FormatPercent(GpuPercent);

    public string DiskText => StatsPresenters.FormatPercent(DiskPercent);

    /// <summary>Seeds (or updates) the system battery display.</summary>
    public void SeedSystemBattery(SystemBatteryState state)
    {
        SystemBatterySupported = true;
        SystemBatteryPercent = state.Percent;
        SystemBatteryCharging = state.IsCharging;
        OnPropertyChanged(nameof(SystemBatteryText));
    }

    private void ShowTransient(TransientKind kind, string glyph, string text)
    {
        TransientGlyph = glyph;
        TransientText = text;
        TransientVisible = true;
        _transient.Show(kind, _settings.TransientStatusMs);
    }

    private void OnTransientExpirationScheduled(int generation, int delayMs)
    {
        _pendingTransientGeneration = generation;
        _transientTimer.Stop();
        _transientTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(1, delayMs));
        _transientTimer.Start();
    }

    private void OnMediaChanged(MediaInfo? media)
    {
        _dispatcher.BeginInvoke(() =>
        {
            MediaTitle = StatusPresenters.MediaTitle(media);
            MediaSubtitle = StatusPresenters.MediaSubtitle(media);
            MediaArtistText = media is null || string.IsNullOrWhiteSpace(media.Artist)
                ? StatusPresenters.MediaSubtitle(media)
                : media.Artist;
            MediaStatusText = media is null ? string.Empty : StatusPresenters.MediaStatusText(media.Status);
            PlayPauseGlyph = StatusPresenters.PlayPauseGlyph(media?.Status ?? MediaPlaybackStatus.Unknown);
            HasActiveMedia = StatusPresenters.HasUsableMedia(media);
            _isPlaying = media?.Status == MediaPlaybackStatus.Playing;
            IsMediaPlaying = _isPlaying;

            UpdateSourceBadge(media);
            UpdateMediaTotalText();
            CanPlayPause = media is not null && HasActiveMedia && media.Controls.SupportsPlayPause;
            CanNext = media is not null && media.Controls.SupportsNext;
            CanPrevious = media is not null && media.Controls.SupportsPrevious;

            if (!HasActiveMedia)
            {
                HasArtwork = false;
                MediaArtwork = null;
                _timeline = null;
                _playbackTimer.Stop();
                UpdatePlaybackTexts();

                if (SelectedTab == NotchTab.Media && State is NotchState.Expanded or NotchState.Pinned)
                {
                    SelectedTab = NotchTab.Nook;
                }
            }
            else
            {
                UpdatePlaybackTexts();
            }
        });
    }

    private void OnArtworkChanged(byte[] bytes)
    {
        _dispatcher.BeginInvoke(() =>
        {
            // The service already gates artwork on the allow-list (checked
            // against the raw source id); here we only handle presence.
            if (bytes.Length == 0 || !HasActiveMedia)
            {
                HasArtwork = false;
                MediaArtwork = null;
                return;
            }

            try
            {
                var image = new BitmapImage();
                using var stream = new MemoryStream(bytes);
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.DecodePixelWidth = 96;
                image.StreamSource = stream;
                image.EndInit();
                image.Freeze();

                MediaArtwork = image;
                HasArtwork = true;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Media: artwork bytes could not be decoded (ignored).");
            }
        });
    }

    private void OnTimelineChanged(MediaTimeline timeline)
    {
        _dispatcher.BeginInvoke(() =>
        {
            _timeline = timeline;
            UpdatePlaybackTexts();
            UpdateMediaTotalText();
        });
    }

    private void UpdatePlaybackTexts()
    {
        if (_timeline is not { } timeline || !HasActiveMedia)
        {
            ElapsedText = string.Empty;
            RemainingText = string.Empty;
            PlaybackFraction = 0;
            return;
        }

        var elapsed = MediaPlaybackClock.ComputeElapsed(
            timeline.Position,
            timeline.Timestamp,
            timeline.Duration,
            _isPlaying,
            DateTimeOffset.Now);

        ElapsedText = MediaPlaybackClock.FormatMediaTime(elapsed);
        RemainingText = timeline.Duration > TimeSpan.Zero
            ? MediaPlaybackClock.FormatRemainingTime(timeline.Duration - elapsed)
            : string.Empty;
        PlaybackFraction = timeline.Duration > TimeSpan.Zero
            ? Math.Clamp(elapsed / timeline.Duration, 0, 1)
            : 0;
    }

    /// <summary>
    /// Sets the artwork source badge: real brand logos (user-supplied assets)
    /// for Apple Music and Spotify; for any other source the app icon
    /// extracted from its package/executable, or no badge at all.
    /// </summary>
    private void UpdateSourceBadge(MediaInfo? media)
    {
        var app = media?.AppName ?? string.Empty;

        if (string.IsNullOrEmpty(app) || !HasActiveMedia)
        {
            MediaSourceBadgeVisible = false;
            IsAppleMusicSource = false;
            IsSpotifySource = false;
            MediaSourceAppIcon = null;
            return;
        }

        if (app.Contains("apple", StringComparison.OrdinalIgnoreCase))
        {
            IsAppleMusicSource = true;
            IsSpotifySource = false;
            MediaSourceAppIcon = null;
            MediaSourceBadgeBrush = "Transparent";
            MediaSourceBadgeToolTip = "Playing from Apple Music";
            MediaSourceBadgeVisible = true;
            return;
        }

        if (app.Contains("spotify", StringComparison.OrdinalIgnoreCase))
        {
            IsAppleMusicSource = false;
            IsSpotifySource = true;
            MediaSourceAppIcon = null;
            MediaSourceBadgeBrush = "Transparent";
            MediaSourceBadgeToolTip = "Playing from Spotify";
            MediaSourceBadgeVisible = true;
            return;
        }

        IsAppleMusicSource = false;
        IsSpotifySource = false;
        MediaSourceAppIcon = _appIcons.GetIcon(media?.SourceAppUserModelId ?? string.Empty);

        if (MediaSourceAppIcon is not null)
        {
            MediaSourceBadgeBrush = "#1C1C22";
            MediaSourceBadgeToolTip = $"Playing from {app}";
            MediaSourceBadgeVisible = true;
        }
        else
        {
            MediaSourceBadgeVisible = false;
        }
    }

    /// <summary>Total duration text for the progress row.</summary>
    private void UpdateMediaTotalText()
        => MediaTotalText = _timeline is { } timeline && timeline.Duration > TimeSpan.Zero
            ? MediaPlaybackClock.FormatMediaTime(timeline.Duration)
            : string.Empty;

    // ------------------------------------------------------------ tab commands

    [RelayCommand]
    private void ShowNook() => SelectedTab = NotchTab.Nook;

    [RelayCommand]
    private void ShowAgents() => SelectedTab = NotchTab.Agents;

    [RelayCommand]
    private void ShowStats() => SelectedTab = NotchTab.Stats;

    /// <summary>Lock icon for the nav row: closed when pinned, open when not.</summary>
    public string LockGlyph => IsPinned ? "\uE72E" : "\uE785";

    private void OnEarbudStatusChanged(IReadOnlyDictionary<string, EarbudStatus> devices)
    {
        _dispatcher.BeginInvoke(() =>
        {
            var status = devices.Values
                .Where(d => d.IsConnected)
                .OrderByDescending(d => d.BatteryPercent.HasValue)
                .ThenBy(d => d.Name, StringComparer.CurrentCulture)
                .FirstOrDefault();

            if (status is null || string.IsNullOrEmpty(status.Name))
            {
                return;
            }

            BatteryDeviceName = StatusPresenters.EarbudDisplayName(status.Name);
            BatteryText = StatusPresenters.EarbudText(status);
            BatteryPercent = status.BatteryPercent;
        });
    }

    private void UpdateClock(DateTime now)
    {
        TimeText = NotchClock.FormatTime(now);
        ShortDateText = NotchClock.FormatShortDate(now);

        if (now.Date != _lastDate)
        {
            _lastDate = now.Date;
            Calendar = NotchCalendar.Build(now);
        }
    }

    public void Dispose()
    {
        _collapseTimer.Stop();
        _clockTimer.Stop();
        _transientTimer.Stop();
        _playbackTimer.Stop();
        _batteryTimer.Stop();
        _transient.ExpirationScheduled -= OnTransientExpirationScheduled;
        _machine.Transitioned -= OnMachineTransitioned;
        _machine.CollapseScheduled -= OnCollapseScheduled;
        _machine.CollapseCancelled -= OnCollapseCancelled;
        _audioMonitor.VolumeChanged -= OnVolumeChanged;
        _brightnessMonitor.BrightnessChanged -= OnBrightnessChanged;
        _mediaMonitor.MediaChanged -= OnMediaChanged;
        _mediaMonitor.ArtworkChanged -= OnArtworkChanged;
        _mediaMonitor.TimelineChanged -= OnTimelineChanged;
        _bluetoothMonitor.StatusChanged -= OnEarbudStatusChanged;
        _systemBatteryMonitor.BatteryChanged -= OnSystemBatteryChanged;
        _statsMonitor.SnapshotAvailable -= OnStatsSnapshot;
        _statsMonitor.MachineInfoAvailable -= OnMachineInfo;
        _weatherMonitor.WeatherChanged -= OnWeatherChanged;
        _weatherMonitor.FetchFailed -= OnWeatherFailed;
    }
}
