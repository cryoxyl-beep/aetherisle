using System.Net.Http;
using System.Text.Json;
using GrumphySwarm.Core.Monitors;
using Microsoft.Extensions.Logging;

namespace GrumphySwarm.App.Services.Weather;

/// <summary>
/// Live local weather for the Nook weather card. Geolocates by IP (ipwho.is
/// first, ipapi.co as fallback) and reads current conditions + today's
/// high/low from open-meteo — all keyless and free. Refreshes every 10
/// minutes; after a failure it retries every 3 minutes so the card recovers
/// quickly. Failures are logged once per streak; the card shows placeholders,
/// never invented numbers.
/// </summary>
public sealed class WeatherMonitor : IDisposable
{
    private static readonly string[] GeoUris =
    [
        "https://ipwho.is/",
        "https://ipapi.co/json/",
    ];

    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan FailureRetryInterval = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan FetchTimeout = TimeSpan.FromSeconds(8);

    private readonly ILogger<WeatherMonitor> _logger;
    private readonly HttpClient _http = new() { Timeout = FetchTimeout };
    private readonly object _gate = new();

    private Thread? _thread;
    private CancellationTokenSource? _cancellation;
    private bool _disposed;
    private bool _loggedFailure;

    public WeatherMonitor(ILogger<WeatherMonitor> logger)
    {
        _logger = logger;
    }

    /// <summary>Raised on every successful refresh (background thread).</summary>
    public event Action<WeatherState>? WeatherChanged;

    /// <summary>Raised when a fetch cycle failed (rate limit, offline, …).</summary>
    public event Action? FetchFailed;

    public WeatherState? Current { get; private set; }

    public void Start()
    {
        lock (_gate)
        {
            if (_thread is not null || _disposed)
            {
                return;
            }

            _cancellation = new CancellationTokenSource();
            _thread = new Thread(FetchLoop)
            {
                IsBackground = true,
                Name = "GrumphySwarm.Weather",
                Priority = ThreadPriority.BelowNormal,
            };
            _thread.SetApartmentState(ApartmentState.MTA);
            _thread.Start(_cancellation.Token);
        }
    }

    private void FetchLoop(object? state)
    {
        var cancellation = (CancellationToken)state!;

        while (!cancellation.IsCancellationRequested)
        {
            var success = false;
            foreach (var geoUri in GeoUris)
            {
                if (TryFetch(geoUri, out var weather) && weather is not null)
                {
                    Current = weather;
                    _loggedFailure = false;
                    WeatherChanged?.Invoke(weather);
                    success = true;
                    break;
                }
            }

            if (!success)
            {
                FetchFailed?.Invoke();
            }

            cancellation.WaitHandle.WaitOne(success ? RefreshInterval : FailureRetryInterval);
        }
    }

    private bool TryFetch(string geoUri, out WeatherState? weather)
    {
        weather = null;

        try
        {
            double latitude, longitude;
            string city;

            using (var document = JsonDocument.Parse(_http.GetStringAsync(geoUri).GetAwaiter().GetResult()))
            {
                var root = document.RootElement;
                latitude = root.GetProperty("latitude").GetDouble();
                longitude = root.GetProperty("longitude").GetDouble();
                city = root.TryGetProperty("city", out var cityElement)
                    && cityElement.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(cityElement.GetString())
                        ? cityElement.GetString()!
                        : root.TryGetProperty("region", out var regionElement)
                            && regionElement.ValueKind == JsonValueKind.String
                            ? regionElement.GetString()!
                            : "Here";
            }

            var culture = System.Globalization.CultureInfo.InvariantCulture;
            var forecastUri =
                "https://api.open-meteo.com/v1/forecast?latitude="
                + latitude.ToString("0.####", culture)
                + "&longitude="
                + longitude.ToString("0.####", culture)
                + "&current=temperature_2m,weather_code,is_day&daily=temperature_2m_max,temperature_2m_min&forecast_days=1&timezone=auto";

            using (var document = JsonDocument.Parse(_http.GetStringAsync(forecastUri).GetAwaiter().GetResult()))
            {
                var current = document.RootElement.GetProperty("current");
                var daily = document.RootElement.GetProperty("daily");
                weather = new WeatherState(
                    City: city,
                    TempC: current.GetProperty("temperature_2m").GetDouble(),
                    HighC: daily.GetProperty("temperature_2m_max")[0].GetDouble(),
                    LowC: daily.GetProperty("temperature_2m_min")[0].GetDouble(),
                    WeatherCode: current.GetProperty("weather_code").GetInt32(),
                    IsDay: current.TryGetProperty("is_day", out var isDayElement) && isDayElement.GetInt32() == 1,
                    Timestamp: DateTimeOffset.Now);
            }

            _logger.LogInformation(
                "Weather: {City} {Temp:0}°C (H {High:0}° / L {Low:0}°), code {Code}.",
                weather.City,
                weather.TempC,
                weather.HighC,
                weather.LowC,
                weather.WeatherCode);
            return true;
        }
        catch (Exception ex)
        {
            if (!_loggedFailure)
            {
                _logger.LogInformation("Weather unavailable ({Message}); will retry in 15 min.", ex.Message);
                _loggedFailure = true;
            }

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
            _cancellation?.Cancel();
            try
            {
                _thread?.Join(TimeSpan.FromSeconds(2));
            }
            catch
            {
                // Best effort.
            }

            _http.Dispose();
            _cancellation?.Dispose();
        }
    }
}
