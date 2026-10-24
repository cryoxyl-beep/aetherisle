namespace GrumphySwarm.Core.Media;

/// <summary>
/// The user's allow-list for premium media features, kept in one obvious place
/// so it is trivial to edit.
///
/// Album artwork is shown ONLY for sessions from these sources:
///   - Windows apps: Spotify and the Apple Music app (matched by app-user-model
///     id substrings, so "SpotifyAB.SpotifyMusic…" and "AppleInc.AppleMusicWin…"
///     both match).
///   - Web players in browsers: music.apple.com. Browsers do not expose the
///     active tab's URL to the operating system, and the app deliberately does
///     not scrape browser windows — so a browser session is trusted as the
///     user's web player. Editing <see cref="WebPlayerTokens"/> removes that.
///
/// Bluetooth battery status is shown ONLY for these devices.
/// </summary>
public static class MediaAllowList
{
    /// <summary>Lowercase substrings matched against the SMTC source app id.</summary>
    public static readonly IReadOnlyList<string> AlbumArtTokens =
    [
        "spotify",
        "apple",   // Apple Music app (AppleInc.AppleMusicWin…), iTunes, any Apple packaging
        "applemusic",
        "itunes",
        // Web players (music.apple.com). See class remark for the URL caveat.
        "chrome",
        "msedge",
        "edge",
        "firefox",
        "brave",
        "opera",
    ];

    /// <summary>Lowercase substrings matched against the Bluetooth device name.</summary>
    public static readonly IReadOnlyList<string> EarbudNameTokens =
    [
        "airdopes 138",
        "zeb duke",
    ];

    /// <summary>True when artwork may be shown for this SMTC source.</summary>
    public static bool IsAlbumArtAllowed(string? sourceAppUserModelId)
    {
        if (string.IsNullOrWhiteSpace(sourceAppUserModelId))
        {
            return false;
        }

        var id = sourceAppUserModelId.ToLowerInvariant();
        foreach (var token in AlbumArtTokens)
        {
            if (id.Contains(token))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>True when this Bluetooth device is on the earbud allow-list.</summary>
    public static bool IsEarbudAllowed(string? deviceName)
    {
        if (string.IsNullOrWhiteSpace(deviceName))
        {
            return false;
        }

        var name = deviceName.ToLowerInvariant();
        foreach (var token in EarbudNameTokens)
        {
            if (name.Contains(token))
            {
                return true;
            }
        }

        return false;
    }
}
