using GrumphySwarm.Core.Monitors;

namespace GrumphySwarm.Tests.Monitors;

public class StatusPresentersTests
{
    [Theory]
    [InlineData(0f, false, "\uE74F", "0%")]
    [InlineData(0.42f, false, "\uE767", "42%")]
    [InlineData(1f, false, "\uE767", "100%")]
    [InlineData(0.3f, true, "\uE74F", "Muted")]
    public void Volume_presentation_maps_correctly(float level, bool muted, string expectedGlyph, string expectedText)
    {
        var state = new VolumeState(level, muted, DateTimeOffset.Now);

        Assert.Equal(expectedGlyph, StatusPresenters.VolumeGlyph(state.IsMuted, state.Percent));
        Assert.Equal(expectedText, StatusPresenters.VolumeText(state.IsMuted, state.Percent));
    }

    [Fact]
    public void Volume_percent_clamps_and_rounds()
    {
        Assert.Equal(100, new VolumeState(1.2f, false, DateTimeOffset.Now).Percent);
        Assert.Equal(0, new VolumeState(-0.5f, false, DateTimeOffset.Now).Percent);
        // 0.47f is exactly representable enough: 0.47 * 100 -> 47.000004 -> 47.
        Assert.Equal(47, new VolumeState(0.47f, false, DateTimeOffset.Now).Percent);
    }

    [Fact]
    public void Brightness_text_uses_real_readings_only()
    {
        Assert.Equal("65%", StatusPresenters.BrightnessText(65));
    }

    [Fact]
    public void Play_pause_glyph_reflects_status()
    {
        Assert.Equal("\uE769", StatusPresenters.PlayPauseGlyph(MediaPlaybackStatus.Playing));
        Assert.Equal("\uE768", StatusPresenters.PlayPauseGlyph(MediaPlaybackStatus.Paused));
        Assert.Equal("\uE768", StatusPresenters.PlayPauseGlyph(MediaPlaybackStatus.Unknown));
    }

    [Fact]
    public void Empty_media_session_is_not_usable()
    {
        Assert.False(StatusPresenters.HasUsableMedia(null));

        var empty = new MediaInfo("", "", "", MediaPlaybackStatus.Unknown, new MediaTransportControls(false, false, false), DateTimeOffset.Now);
        Assert.False(StatusPresenters.HasUsableMedia(empty));
    }

    [Fact]
    public void Media_with_metadata_is_usable_and_rendered()
    {
        var media = new MediaInfo(
            "Lay Low", "Gesiå", "Spotify",
            MediaPlaybackStatus.Playing,
            new MediaTransportControls(SupportsPlayPause: true, SupportsNext: true, SupportsPrevious: false),
            DateTimeOffset.Now);

        Assert.True(StatusPresenters.HasUsableMedia(media));
        Assert.Equal("Lay Low", StatusPresenters.MediaTitle(media));
        Assert.Equal("Gesiå · Spotify", StatusPresenters.MediaSubtitle(media));
        Assert.Equal("Playing", StatusPresenters.MediaStatusText(media.Status));
    }

    [Fact]
    public void Paused_media_without_artist_still_shows_title()
    {
        var media = new MediaInfo("Watch later", "", "Chrome", MediaPlaybackStatus.Paused,
            new MediaTransportControls(true, false, false), DateTimeOffset.Now);

        Assert.True(StatusPresenters.HasUsableMedia(media));
        Assert.Equal("Chrome", StatusPresenters.MediaSubtitle(media));
        Assert.Equal("Paused", StatusPresenters.MediaStatusText(media.Status));
    }

    [Fact]
    public void Null_media_renders_clean_empty_state()
    {
        Assert.Equal("Nothing playing", StatusPresenters.MediaTitle(null));
        Assert.Equal(string.Empty, StatusPresenters.MediaSubtitle(null));
    }
}
