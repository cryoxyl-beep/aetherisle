using System.Runtime.InteropServices;
using GrumphySwarm.Core.Audio;
using Microsoft.Extensions.Logging;

namespace GrumphySwarm.App.Services.Audio;

/// <summary>
/// Real-time system audio spectrum for the visualizer: WASAPI loopback capture
/// on the default render endpoint, FFT-folded into log-spaced bands.
///
/// Everything here is real signal processing — the bars dance only when the
/// machine actually produces sound, and decay to flat when it is silent. The
/// capture thread is a dedicated background thread that never touches the UI;
/// the UI reads the latest band array reference (swapped atomically) at its own
/// frame rate. Device invalidation (default output change, endpoint removal)
/// triggers a bounded automatic re-initialization.
/// </summary>
public sealed class AudioSpectrumMonitor : IDisposable
{
    private const int FftSize = 1024;
    private const int BandCount = 24;
    private const int MaxConsecutiveFailures = 5;

    private readonly ILogger<AudioSpectrumMonitor> _logger;
    private readonly SpectrumAnalyzer _analyzer = new(FftSize, BandCount, sampleRate: 48000);
    private readonly float[] _analysisWindow = new float[FftSize];
    private readonly float[] _ringBuffer = new float[FftSize * 4];
    private readonly object _publishGate = new();

    private Thread? _captureThread;
    private CancellationTokenSource? _cancellation;
    private DeviceChangeClient? _deviceChangeClient;
    private float[] _packetScratch = new float[4096];
    private int _ringWrite;
    private int _ringFill;
    private volatile double[]? _latestBands;
    private int _consecutiveFailures;
    private bool _disposed;

    public AudioSpectrumMonitor(ILogger<AudioSpectrumMonitor> logger)
    {
        _logger = logger;
    }

    public bool IsRunning { get; private set; }

    /// <summary>Latest normalized band magnitudes (0..1), or null before the first block.</summary>
    public double[]? LatestBands => _latestBands;

    public void Start()
    {
        if (_captureThread is not null)
        {
            return;
        }

        _cancellation = new CancellationTokenSource();
        _captureThread = new Thread(CaptureLoop)
        {
            IsBackground = true,
            Name = "GrumphySwarm.AudioSpectrum",
            Priority = ThreadPriority.BelowNormal,
        };
        // COM needs a deterministic apartment on the dedicated capture thread.
        _captureThread.SetApartmentState(ApartmentState.MTA);
        _captureThread.Start();
    }

    private void CaptureLoop()
    {
        var cancellation = _cancellation!.Token;

        while (!cancellation.IsCancellationRequested)
        {
            try
            {
                RunCaptureSession(cancellation);
            }
            catch (Exception ex)
            {
                _consecutiveFailures++;
                if (_consecutiveFailures >= MaxConsecutiveFailures)
                {
                    _logger.LogWarning(ex, "Spectrum: capture failed repeatedly — visualizer disabled until a device change or restart.");
                    IsRunning = false;
                    // Keep the thread alive but idle; a device change resets the counter.
                    WaitHandle.WaitAny(new[] { cancellation.WaitHandle }, TimeSpan.FromMinutes(1));
                    continue;
                }

                Thread.Sleep(1000);
            }
        }
    }

    /// <summary>One WASAPI loopback session; returns when the device is lost or cancelled.</summary>
    private void RunCaptureSession(CancellationToken cancellation)
    {
        var enumerator = (CoreAudioInterop.IMMDeviceEnumerator)new CoreAudioInterop.MMDeviceEnumeratorComObject();

        RegisterDeviceNotifications(enumerator);

        if (enumerator.GetDefaultAudioEndpoint(CoreAudioInterop.EDataFlowRender, CoreAudioInterop.ERoleMultimedia, out var device) < 0)
        {
            throw new InvalidOperationException("no default render endpoint");
        }

        var iid = typeof(CoreAudioInterop.IAudioClient).GUID;
        if (device.Activate(ref iid, CoreAudioInterop.ClsctxAll, IntPtr.Zero, out var clientObject) < 0)
        {
            throw new InvalidOperationException("IAudioClient activation failed");
        }

        var client = (CoreAudioInterop.IAudioClient)clientObject;
        if (client.GetMixFormat(out var mixFormatPointer) < 0 || mixFormatPointer == IntPtr.Zero)
        {
            throw new InvalidOperationException("GetMixFormat failed");
        }

        try
        {
            var format = Marshal.PtrToStructure<CoreAudioInterop.WaveFormatEx>(mixFormatPointer);
            if (format.Channels == 0 || format.SamplesPerSecond == 0)
            {
                throw new InvalidOperationException("unexpected mix format");
            }

            // Rebuild the analyzer if the endpoint's sample rate differs from 48k.
            var analyzer = format.SamplesPerSecond == 48000
                ? _analyzer
                : new SpectrumAnalyzer(FftSize, BandCount, format.SamplesPerSecond);

            var captureIid = typeof(CoreAudioInterop.IAudioCaptureClient).GUID;
            if (client.Initialize(
                    CoreAudioInterop.AudclntSharemodeShared,
                    CoreAudioInterop.AudclntStreamflagsLoopback,
                    bufferDuration: 2_000_000, // 200 ms
                    periodicity: 0,
                    mixFormatPointer,
                    IntPtr.Zero) < 0
                || client.GetService(ref captureIid, out var captureObject) < 0)
            {
                throw new InvalidOperationException("loopback capture initialization failed");
            }

            var capture = (CoreAudioInterop.IAudioCaptureClient)captureObject;
            if (client.Start() < 0)
            {
                throw new InvalidOperationException("capture start failed");
            }

            _consecutiveFailures = 0;
            IsRunning = true;
            _logger.LogInformation(
                "Spectrum: loopback capture active ({Channels}ch @ {Rate} Hz).",
                format.Channels,
                format.SamplesPerSecond);

            try
            {
                PumpPackets(client, capture, analyzer, format.Channels, cancellation);
            }
            finally
            {
                IsRunning = false;
                client.Stop();
            }
        }
        finally
        {
            Marshal.FreeCoTaskMem(mixFormatPointer);
            UnregisterDeviceNotifications();
        }
    }

    private void PumpPackets(
        CoreAudioInterop.IAudioClient client,
        CoreAudioInterop.IAudioCaptureClient capture,
        SpectrumAnalyzer analyzer,
        ushort channels,
        CancellationToken cancellation)
    {
        var samplesPerBlock = analyzer.FftSizeSamples;

        while (!cancellation.IsCancellationRequested)
        {
            var processed = false;
            while (capture.GetNextPacketSize(out var packetFrames) >= 0 && packetFrames > 0)
            {
                if (capture.GetBuffer(out var dataPointer, out var frames, out _, out _, out _) < 0)
                {
                    break;
                }

                AppendDownmixed(dataPointer, (int)frames, channels);
                capture.ReleaseBuffer(frames);
                processed = true;
            }

            if (_ringFill >= samplesPerBlock)
            {
                CopyAnalysisWindow(samplesPerBlock);
                var bands = analyzer.ComputeBands(_analysisWindow);
                Publish(bands);
            }

            if (!processed)
            {
                Thread.Sleep(8);
            }

            if (client.GetCurrentPadding(out _) < 0)
            {
                // Device invalidated — fall out and let the loop re-initialize.
                throw new InvalidOperationException("audio device invalidated");
            }
        }
    }

    /// <summary>Downmixes an interleaved float32 packet into the mono ring buffer.</summary>
    private void AppendDownmixed(IntPtr dataPointer, int frames, int channels)
    {
        var required = frames * channels;
        if (_packetScratch.Length < required)
        {
            _packetScratch = new float[required];
        }

        Marshal.Copy(dataPointer, _packetScratch, 0, required);

        for (var f = 0; f < frames; f++)
        {
            float sum = 0;
            for (var c = 0; c < channels; c++)
            {
                sum += _packetScratch[(f * channels) + c];
            }

            _ringBuffer[_ringWrite] = sum / channels;
            _ringWrite = (_ringWrite + 1) % _ringBuffer.Length;
            if (_ringFill < _ringBuffer.Length)
            {
                _ringFill++;
            }
        }
    }

    /// <summary>Copies the most recent block (oldest → newest) into the analysis window.</summary>
    private void CopyAnalysisWindow(int samplesPerBlock)
    {
        var start = (_ringWrite - samplesPerBlock + _ringBuffer.Length) % _ringBuffer.Length;
        for (var i = 0; i < samplesPerBlock; i++)
        {
            _analysisWindow[i] = _ringBuffer[(start + i) % _ringBuffer.Length];
        }

        _ringFill = Math.Max(0, _ringFill - samplesPerBlock / 2); // 50% overlap keeps motion smooth
    }

    private void Publish(double[] bands)
    {
        var copy = new double[bands.Length];
        Array.Copy(bands, copy, bands.Length);
        lock (_publishGate)
        {
            _latestBands = copy;
        }
    }

    private void RegisterDeviceNotifications(CoreAudioInterop.IMMDeviceEnumerator enumerator)
    {
        var client = new DeviceChangeClient(() => _consecutiveFailures = 0);
        try
        {
            if (enumerator.RegisterEndpointNotificationCallback(client) >= 0)
            {
                _deviceChangeClient = client;
            }
        }
        catch
        {
            // Notifications are best-effort; failure recovery still works.
        }
    }

    private void UnregisterDeviceNotifications()
    {
        if (_deviceChangeClient is not null)
        {
            try
            {
                var enumerator = (CoreAudioInterop.IMMDeviceEnumerator)new CoreAudioInterop.MMDeviceEnumeratorComObject();
                enumerator.UnregisterEndpointNotificationCallback(_deviceChangeClient);
            }
            catch
            {
                // Shutdown path — best effort.
            }

            _deviceChangeClient = null;
        }
    }

    /// <summary>Doubles as the managed IMMNotificationClient for device changes.</summary>
    private sealed class DeviceChangeClient(Action onDeviceChange) : CoreAudioInterop.IMMNotificationClient
    {
        void CoreAudioInterop.IMMNotificationClient.OnDeviceStateChanged(string deviceId, int newState) => onDeviceChange();

        void CoreAudioInterop.IMMNotificationClient.OnDeviceAdded(string deviceId) => onDeviceChange();

        void CoreAudioInterop.IMMNotificationClient.OnDeviceRemoved(string deviceId) => onDeviceChange();

        void CoreAudioInterop.IMMNotificationClient.OnDefaultEndpointChanged(int dataFlow, int role, string defaultDeviceId)
        {
            if (dataFlow == CoreAudioInterop.EDataFlowRender)
            {
                onDeviceChange();
            }
        }

        void CoreAudioInterop.IMMNotificationClient.OnPropertyValueChanged(string deviceId, CoreAudioInterop.PropertyKey key)
        {
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _cancellation?.Cancel();
        try
        {
            _captureThread?.Join(TimeSpan.FromSeconds(2));
        }
        catch
        {
            // Thread may already be gone.
        }

        _cancellation?.Dispose();
        _captureThread = null;
    }
}
