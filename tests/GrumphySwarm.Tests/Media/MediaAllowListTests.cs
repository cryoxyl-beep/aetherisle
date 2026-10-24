using GrumphySwarm.Core.Audio;
using GrumphySwarm.Core.Media;

namespace GrumphySwarm.Tests.Media;

public class MediaAllowListTests
{
    [Theory]
    [InlineData("Spotify.exe", true)]
    [InlineData("SpotifyAB.SpotifyMusicforPC_1p62q0xwzpwrc!Spotify", true)]
    [InlineData("AppleInc.AppleMusicWin_n2jrzybd8bz3m!AppleMusic", true)]
    [InlineData("iTunes.exe", true)]
    [InlineData("chrome.exe", true)] // web player (music.apple.com), see allow-list remark
    [InlineData("msedge.exe", true)]
    [InlineData("firefox.exe", true)]
    [InlineData("brave.exe", true)]
    [InlineData("opera.exe", true)]
    [InlineData("Videos.exe", false)]
    [InlineData("vlc.exe", false)]
    [InlineData(null, false)]
    [InlineData("", false)]
    public void Album_art_is_restricted_to_the_allow_list(string? source, bool expected)
    {
        Assert.Equal(expected, MediaAllowList.IsAlbumArtAllowed(source));
    }

    [Theory]
    [InlineData("Airdopes 138", true)]
    [InlineData("AIRDOPES 138 (stereo)", true)] // case-insensitive
    [InlineData("boAt Airdopes 138", true)]
    [InlineData("Airdopes 138 Hands-Free AG", true)] // PnP devnode name
    [InlineData("Zeb Duke", true)]
    [InlineData("ZEB-DUKE Headphones", true)] // dashed spelling
    [InlineData("Headset (ZEB-DUKE Hands-Free)", true)] // audio-endpoint name
    [InlineData("Zeb Duke 2", true)]
    [InlineData("WH-1000XM5", false)]
    [InlineData(null, false)]
    public void Earbud_allow_list_matches_only_listed_devices(string? name, bool expected)
    {
        Assert.Equal(expected, MediaAllowList.IsEarbudAllowed(name));
    }
}

public class MediaPlaybackClockTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 24, 22, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Paused_playback_does_not_advance()
    {
        var elapsed = MediaPlaybackClock.ComputeElapsed(
            TimeSpan.FromMinutes(2), T0, TimeSpan.FromMinutes(4), isPlaying: false, T0.AddMinutes(1));
        Assert.Equal(TimeSpan.FromMinutes(2), elapsed);
    }

    [Fact]
    public void Playing_playback_advances_with_wall_clock()
    {
        var elapsed = MediaPlaybackClock.ComputeElapsed(
            TimeSpan.FromMinutes(2), T0, TimeSpan.FromMinutes(4), isPlaying: true, T0.AddSeconds(30));
        Assert.Equal(TimeSpan.FromMinutes(2).Add(TimeSpan.FromSeconds(30)), elapsed);
    }

    [Fact]
    public void Elapsed_never_exceeds_the_duration()
    {
        var elapsed = MediaPlaybackClock.ComputeElapsed(
            TimeSpan.FromMinutes(3.9), T0, TimeSpan.FromMinutes(4), isPlaying: true, T0.AddMinutes(5));
        Assert.Equal(TimeSpan.FromMinutes(4), elapsed);
    }

    [Fact]
    public void Time_formatting_uses_mss_and_negative_remaining()
    {
        Assert.Equal("2:36", MediaPlaybackClock.FormatMediaTime(TimeSpan.FromSeconds(156)));
        Assert.Equal("0:05", MediaPlaybackClock.FormatMediaTime(TimeSpan.FromSeconds(5)));
        Assert.Equal("-3:14", MediaPlaybackClock.FormatRemainingTime(TimeSpan.FromSeconds(194)));
        Assert.Equal("1:02:03", MediaPlaybackClock.FormatMediaTime(new TimeSpan(1, 2, 3)));
        Assert.Equal("-0:00", MediaPlaybackClock.FormatRemainingTime(TimeSpan.FromSeconds(-30)));
    }
}

public class SpectrumAnalyzerTests
{
    private const int FftSize = 1024;
    private const double SampleRate = 48000;

    private static double[] BandsForSine(double frequency, float amplitude = 0.8f)
    {
        var analyzer = new SpectrumAnalyzer(FftSize, 24, SampleRate);
        var samples = new float[FftSize];
        for (var i = 0; i < FftSize; i++)
        {
            samples[i] = (float)(amplitude * Math.Sin(2 * Math.PI * frequency * i / SampleRate));
        }

        return analyzer.ComputeBands(samples);
    }

    [Fact]
    public void A_sine_lands_in_the_expected_band_and_nowhere_else()
    {
        const double frequency = 1000; // Hz
        var bands = BandsForSine(frequency);

        var binHz = SampleRate / FftSize;
        var expectedBin = (int)(frequency / binHz);

        // Map bin to band index using the analyzer's log spacing (mirrors the
        // band-edge computation in SpectrumAnalyzer).
        var expectedBand = -1;
        var logMin = Math.Log(40);
        var logMax = Math.Log(16000);
        for (var b = 0; b < 24; b++)
        {
            var f0 = Math.Exp(logMin + (logMax - logMin) * b / 24);
            var f1 = Math.Exp(logMin + (logMax - logMin) * (b + 1) / 24);
            if (expectedBin >= (int)Math.Floor(f0 / binHz) && expectedBin < (int)Math.Ceiling(f1 / binHz))
            {
                expectedBand = b;
            }
        }

        Assert.True(expectedBand >= 0, "test setup: 1 kHz must fall inside the band range");
        Assert.True(bands[expectedBand] > 0.25, $"expected band {expectedBand} to react, got {bands[expectedBand]}");

        // Bands far from the tone must stay quiet.
        var far = expectedBand < 12 ? 23 : 0;
        Assert.True(bands[far] < 0.1, $"far band {far} should be quiet, got {bands[far]}");
    }

    [Fact]
    public void Silence_produces_near_zero_bands()
    {
        var analyzer = new SpectrumAnalyzer(FftSize, 24, SampleRate);
        var bands = analyzer.ComputeBands(new float[FftSize]);
        Assert.All(bands, v => Assert.True(v < 0.01, $"silent band value {v}"));
    }

    [Fact]
    public void Band_count_and_buffer_validation_are_enforced()
    {
        var analyzer = new SpectrumAnalyzer(FftSize, 24, SampleRate);
        Assert.Equal(24, analyzer.BandCount);
        Assert.Throws<ArgumentException>(() => analyzer.ComputeBands(new float[FftSize / 2]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SpectrumAnalyzer(1000, 24, SampleRate));
    }
}
