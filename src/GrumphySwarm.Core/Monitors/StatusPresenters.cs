namespace GrumphySwarm.Core.Monitors;

/// <summary>
/// Pure presentation mappers for system status (glyphs, texts, empty states).
/// Kept free of WPF so they are trivially unit-testable; the app maps the
/// strings onto Segoe Fluent Icons glyphs.
/// </summary>
public static class StatusPresenters
{
    // Segoe Fluent Icons / MDL2 code points (verified against the font:
    // E76C is Next — E76D is a pencil glyph on current font versions).
    public const string VolumeGlyphOn = "\uE767";
    public const string VolumeGlyphMuted = "\uE74F";
    public const string BrightnessGlyph = "\uE706";
    public const string PlayGlyph = "\uE768";
    public const string PauseGlyph = "\uE769";
    public const string PreviousGlyph = "\uE76B";
    public const string NextGlyph = "\uE76C";
    public const string MusicGlyph = "\uE8D6";
    public const string AddGlyph = "\uE710";

    /// <summary>Speaker glyph for the given volume state.</summary>
    public static string VolumeGlyph(bool isMuted, int percent) =>
        isMuted || percent <= 0 ? VolumeGlyphMuted : VolumeGlyphOn;

    /// <summary>Compact pill text for volume: "Muted" or "42%".</summary>
    public static string VolumeText(bool isMuted, int percent) =>
        isMuted ? "Muted" : $"{percent}%";

    /// <summary>Compact pill text for brightness.</summary>
    public static string BrightnessText(int percent) => $"{percent}%";

    /// <summary>Play/pause glyph reflecting the current playback status.</summary>
    public static string PlayPauseGlyph(MediaPlaybackStatus status) =>
        status == MediaPlaybackStatus.Playing ? PauseGlyph : PlayGlyph;

    /// <summary>True when a media snapshot carries anything worth displaying.</summary>
    public static bool HasUsableMedia(MediaInfo? media) =>
        media is not null
        && ((!string.IsNullOrWhiteSpace(media.Title) || !string.IsNullOrWhiteSpace(media.Artist))
            || media.Status is MediaPlaybackStatus.Playing or MediaPlaybackStatus.Paused);

    /// <summary>Title line for the media card, with a clean fallback.</summary>
    public static string MediaTitle(MediaInfo? media) =>
        media is not null && !string.IsNullOrWhiteSpace(media.Title) ? media.Title : "Nothing playing";

    /// <summary>Artist · app line for the media card (may be empty).</summary>
    public static string MediaSubtitle(MediaInfo? media)
    {
        if (media is null)
        {
            return string.Empty;
        }

        var parts = new[]
        {
            string.IsNullOrWhiteSpace(media.Artist) ? null : media.Artist,
            string.IsNullOrWhiteSpace(media.AppName) ? null : media.AppName,
        }.Where(p => p is not null);

        return string.Join(" · ", parts);
    }

    /// <summary>Status word for the media card (e.g. under the title).</summary>
    public static string MediaStatusText(MediaPlaybackStatus status) => status switch
    {
        MediaPlaybackStatus.Playing => "Playing",
        MediaPlaybackStatus.Paused => "Paused",
        MediaPlaybackStatus.Stopped => "Stopped",
        MediaPlaybackStatus.Changing => "Updating",
        _ => string.Empty,
    };

    /// <summary>Earbud battery caption: "86%" with real data, "Connected" without.</summary>
    public static string EarbudText(EarbudStatus status) =>
        status.BatteryPercent is { } percent ? $"{percent}%" : "Connected";

    /// <summary>Battery display name, trimmed of common noise.</summary>
    public static string EarbudDisplayName(string deviceName) => deviceName.Trim();

    /// <summary>System battery pill text: percent, or empty when unknown.</summary>
    public static string SystemBatteryText(int? percent) =>
        percent is { } value ? $"{value}%" : string.Empty;

    /// <summary>Charge fraction (0..1) for battery fill rendering.</summary>
    public static double SystemBatteryFraction(int? percent) =>
        percent is { } value ? Math.Clamp(value / 100.0, 0, 1) : 0;
}
