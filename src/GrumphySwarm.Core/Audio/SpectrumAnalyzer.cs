namespace GrumphySwarm.Core.Audio;

/// <summary>
/// Real FFT-based spectrum analysis for the audio visualizer. Runs an
/// iterative radix-2 Cooley-Tukey transform over a windowed sample block and
/// folds the magnitude spectrum into logarithmically spaced bands (the way
/// classic visualizers map frequency). Zero external dependencies.
///
/// Instances own reusable buffers, so running at ~20 blocks/second allocates
/// nothing beyond the small output array that is handed to the UI thread.
/// </summary>
public sealed class SpectrumAnalyzer
{
    private readonly int _fftSize;
    private readonly int _bandCount;
    private readonly double _sampleRate;
    private readonly double[] _re;
    private readonly double[] _im;
    private readonly double[] _window;
    private readonly int[] _bandStart;
    private readonly int[] _bandEnd;
    private readonly double[] _bands;

    /// <param name="fftSize">Transform size; must be a power of two ≥ 64.</param>
    /// <param name="bandCount">Number of output bands (e.g. 24).</param>
    /// <param name="sampleRate">PCM sample rate (e.g. 48000).</param>
    /// <param name="minFrequency">Low edge of the band range (Hz).</param>
    /// <param name="maxFrequency">High edge of the band range (Hz).</param>
    public SpectrumAnalyzer(int fftSize, int bandCount, double sampleRate, double minFrequency = 40, double maxFrequency = 16000)
    {
        if (fftSize < 64 || (fftSize & (fftSize - 1)) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(fftSize), "must be a power of two >= 64");
        }

        _fftSize = fftSize;
        _bandCount = bandCount;
        _sampleRate = sampleRate;
        _re = new double[fftSize];
        _im = new double[fftSize];
        _bands = new double[bandCount];
        _bandStart = new int[bandCount];
        _bandEnd = new int[bandCount];

        // Hann window (precomputed).
        _window = new double[fftSize];
        for (var i = 0; i < fftSize; i++)
        {
            _window[i] = 0.5 * (1 - Math.Cos(2 * Math.PI * i / (fftSize - 1)));
        }

        // Logarithmically spaced band edges between min and max frequency.
        var binHz = sampleRate / fftSize;
        var logMin = Math.Log(minFrequency);
        var logMax = Math.Log(maxFrequency);
        for (var b = 0; b < bandCount; b++)
        {
            var f0 = Math.Exp(logMin + (logMax - logMin) * b / bandCount);
            var f1 = Math.Exp(logMin + (logMax - logMin) * (b + 1) / bandCount);
            _bandStart[b] = Math.Max(1, (int)Math.Floor(f0 / binHz));
            _bandEnd[b] = Math.Max(_bandStart[b] + 1, (int)Math.Ceiling(f1 / binHz));
        }
    }

    public int BandCount => _bandCount;

    /// <summary>
    /// Computes normalized band magnitudes (0..1) for one window of mono PCM.
    /// The window must contain exactly <see cref="FftSizeSamples"/> samples.
    /// </summary>
    public double[] ComputeBands(ReadOnlySpan<float> mono)
    {
        if (mono.Length != _fftSize)
        {
            throw new ArgumentException($"expected {_fftSize} samples", nameof(mono));
        }

        for (var i = 0; i < _fftSize; i++)
        {
            _re[i] = mono[i] * _window[i];
            _im[i] = 0;
        }

        TransformInPlace(_re, _im);

        // Fold magnitudes into bands. Magnitudes are normalized by N/4 (full-
        // scale sine ≈ 1 before the window's 0.5 coherent gain) and mapped
        // through a 0.6 power curve: musical content stays visible while the
        // window's spectral leakage floor (~1e-4 normalized) stays invisible.
        for (var b = 0; b < _bandCount; b++)
        {
            double sum = 0;
            for (var bin = _bandStart[b]; bin < _bandEnd[b] && bin < _fftSize / 2; bin++)
            {
                var magnitude = Math.Sqrt(_re[bin] * _re[bin] + _im[bin] * _im[bin]);
                sum += magnitude;
            }

            var average = sum / (_bandEnd[b] - _bandStart[b]);
            var normalized = average / (_fftSize / 4.0);
            var scaled = Math.Pow(normalized, 0.6) * 1.25;
            _bands[b] = Math.Clamp(scaled, 0, 1);
        }

        return _bands;
    }

    /// <summary>Number of mono samples one analysis window needs.</summary>
    public int FftSizeSamples => _fftSize;

    private void TransformInPlace(double[] re, double[] im)
    {
        var n = _fftSize;

        // Bit-reversal permutation.
        for (int i = 1, j = 0; i < n; i++)
        {
            var bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1)
            {
                j ^= bit;
            }

            j ^= bit;

            if (i < j)
            {
                (re[i], re[j]) = (re[j], re[i]);
                (im[i], im[j]) = (im[j], im[i]);
            }
        }

        // Butterfly stages.
        for (var len = 2; len <= n; len <<= 1)
        {
            var angle = -2 * Math.PI / len;
            double wRe = Math.Cos(angle);
            double wIm = Math.Sin(angle);

            for (var start = 0; start < n; start += len)
            {
                double curRe = 1;
                double curIm = 0;

                var half = len >> 1;
                for (var k = 0; k < half; k++)
                {
                    var even = start + k;
                    var odd = even + half;

                    var tRe = re[odd] * curRe - im[odd] * curIm;
                    var tIm = re[odd] * curIm + im[odd] * curRe;

                    re[odd] = re[even] - tRe;
                    im[odd] = im[even] - tIm;
                    re[even] += tRe;
                    im[even] += tIm;

                    (curRe, curIm) = (curRe * wRe - curIm * wIm, curRe * wIm + curIm * wRe);
                }
            }
        }
    }
}

/// <summary>
/// Real playback-position interpolation on top of SMTC's coarse timeline
/// events: the system reports position changes aperiodically; while a track is
/// playing, the UI advances from the last reported position using wall-clock
/// time and resynchronizes on every report.
/// </summary>
public static class MediaPlaybackClock
{
    /// <summary>
    /// Current playback position. When playing, advances the last reported
    /// position by real elapsed time and clamps to the duration; when paused,
    /// returns the reported position unchanged.
    /// </summary>
    public static TimeSpan ComputeElapsed(TimeSpan reportedPosition, DateTimeOffset reportedAt, TimeSpan duration, bool isPlaying, DateTimeOffset now)
    {
        if (!isPlaying)
        {
            return Clamp(reportedPosition, duration);
        }

        var advanced = reportedPosition + (now - reportedAt);
        return Clamp(advanced, duration);
    }

    /// <summary>m:ss (h:mm:ss beyond an hour), e.g. "2:36".</summary>
    public static string FormatMediaTime(TimeSpan time)
    {
        time = TimeSpan.FromTicks(Math.Max(0, time.Ticks));
        if (time.TotalHours >= 1)
        {
            return $"{(int)time.TotalHours}:{time.Minutes:D2}:{time.Seconds:D2}";
        }

        return $"{time.Minutes}:{time.Seconds:D2}";
    }

    /// <summary>Remaining time with a leading sign, e.g. "-3:14".</summary>
    public static string FormatRemainingTime(TimeSpan remaining)
    {
        remaining = TimeSpan.FromTicks(Math.Max(0, remaining.Ticks));
        return "-" + FormatMediaTime(remaining);
    }

    private static TimeSpan Clamp(TimeSpan value, TimeSpan duration)
    {
        if (duration > TimeSpan.Zero && value > duration)
        {
            return duration;
        }

        return value < TimeSpan.Zero ? TimeSpan.Zero : value;
    }
}
