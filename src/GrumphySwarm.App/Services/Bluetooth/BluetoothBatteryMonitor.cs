using GrumphySwarm.Core.Media;
using GrumphySwarm.Core.Monitors;
using Microsoft.Extensions.Logging;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;

namespace GrumphySwarm.App.Services.Bluetooth;

/// <summary>
/// Bluetooth earbud monitoring for the user's allow-list devices, using the
/// same Windows association-endpoint (AEP) properties that the Settings app's
/// Bluetooth page uses for its battery badges:
///   - System.Devices.Aep.IsConnected
///   - System.Devices.AepBattery_Level (0-100 when the device reports it)
///
/// This is push-based (DeviceWatcher Updated events), not polled. Some Windows
/// builds reject individual property keys at watcher creation, so creation
/// falls back through progressively simpler property lists; when the battery
/// key cannot be watched directly it is fetched per-device for allow-listed
/// accessories only. Devices that do not report battery still appear as
/// "connected" without a percentage — the app never fabricates a value.
/// </summary>
public sealed class BluetoothBatteryMonitor : IBluetoothBatteryMonitor
{
    private static readonly string[] FullProperties =
    [
        "System.ItemNameDisplay",
        "System.Devices.Aep.IsConnected",
        "System.Devices.AepBattery_Level",
    ];

    private static readonly string[] ConnectedProperties =
    [
        "System.ItemNameDisplay",
        "System.Devices.Aep.IsConnected",
    ];

    private static readonly string[] NameOnlyProperties = ["System.ItemNameDisplay"];

    private readonly ILogger<BluetoothBatteryMonitor> _logger;
    private readonly object _gate = new();
    private readonly Dictionary<string, EarbudStatus> _devices = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Windows.Devices.Bluetooth.GenericAttributeProfile.GattCharacteristic> _gattCharacteristics = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _gattAttempted = new(StringComparer.OrdinalIgnoreCase);

    private DeviceWatcher? _watcher;
    private string[] _activeProperties = FullProperties;
    private bool _disposed;

    public BluetoothBatteryMonitor(ILogger<BluetoothBatteryMonitor> logger)
    {
        _logger = logger;
    }

    public bool IsSupported { get; private set; }

    public IReadOnlyDictionary<string, EarbudStatus> Current
    {
        get
        {
            lock (_gate)
            {
                return new Dictionary<string, EarbudStatus>(_devices, StringComparer.OrdinalIgnoreCase);
            }
        }
    }

    public event Action<IReadOnlyDictionary<string, EarbudStatus>>? StatusChanged;

    public void Start()
    {
        lock (_gate)
        {
            if (_disposed || _watcher is not null)
            {
                return;
            }

            var selector = Windows.Devices.Bluetooth.BluetoothDevice.GetDeviceSelector();

            // Property-list fallback chain: some builds reject individual keys.
            foreach (var properties in new[] { FullProperties, ConnectedProperties, NameOnlyProperties })
            {
                try
                {
                    _watcher = DeviceInformation.CreateWatcher(selector, properties, DeviceInformationKind.AssociationEndpoint);
                    _activeProperties = properties;
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Bluetooth: watcher with {Count} properties rejected.", properties.Length);
                    _watcher = null;
                }
            }

            if (_watcher is null)
            {
                _logger.LogWarning("Bluetooth: device watcher unavailable — battery monitoring disabled.");
                return;
            }

            _watcher.Added += OnWatcherAdded;
            _watcher.Updated += OnWatcherUpdated;
            _watcher.Removed += OnWatcherRemoved;
            _watcher.EnumerationCompleted += (_, _) => _logger.LogInformation("Bluetooth: device enumeration complete.");
            _watcher.Start();

            IsSupported = true;
            _logger.LogInformation(
                "Bluetooth: allow-listed accessory monitoring active (event-driven, {PropertyCount} properties watched).",
                _activeProperties.Length);
        }
    }

    private void OnWatcherAdded(DeviceWatcher sender, DeviceInformation info)
        => UpdateFromProperties(info.Id, info.Name, info.Properties);

    private void OnWatcherUpdated(DeviceWatcher sender, DeviceInformationUpdate update)
        => UpdateFromProperties(update.Id, ExtractName(update.Properties), update.Properties);

    private void OnWatcherRemoved(DeviceWatcher sender, DeviceInformationUpdate update)
    {
        var changed = false;
        lock (_gate)
        {
            changed = _devices.Remove(update.Id);
        }

        if (changed)
        {
            Publish();
        }
    }

    private void UpdateFromProperties(string id, string name, IReadOnlyDictionary<string, object> properties)
    {
        if (!MediaAllowList.IsEarbudAllowed(name))
        {
            return;
        }

        var connected = ReadBool(properties, "System.Devices.Aep.IsConnected");
        var battery = ReadBatteryPercent(properties);

        lock (_gate)
        {
            _devices[id] = new EarbudStatus(name, battery, connected);
        }

        _logger.LogInformation(
            "Bluetooth: {Name} — connected: {Connected}, battery: {Battery}.",
            name,
            connected,
            battery?.ToString() ?? "not reported by device");

        Publish();

        // The battery key could not be watched directly: query it per device.
        if (connected && battery is null)
        {
            if (!_activeProperties.Contains("System.Devices.AepBattery_Level"))
            {
                _ = QueryBatteryAsync(id);
            }

            // Many TWS earbuds (like the user's Airdopes 138) never populate the
            // AEP battery property at all — read the GATT Battery Service (0x180F)
            // directly and subscribe to live updates. One attempt per device.
            if (_gattAttempted.Add(id))
            {
                _ = QueryGattBatteryAsync(id, name);
            }
        }
    }

    private async Task QueryBatteryAsync(string deviceId)
    {
        try
        {
            var device = await DeviceInformation.CreateFromIdAsync(
                deviceId,
                ["System.Devices.AepBattery_Level"],
                DeviceInformationKind.AssociationEndpoint);

            var battery = ReadBatteryPercent(device.Properties);
            if (battery is null)
            {
                return; // device genuinely does not report battery
            }

            bool changed;
            lock (_gate)
            {
                if (_devices.TryGetValue(deviceId, out var known))
                {
                    _devices[deviceId] = known with { BatteryPercent = battery };
                    changed = true;
                }
                else
                {
                    changed = false;
                }
            }

            if (changed)
            {
                _logger.LogInformation("Bluetooth: battery reported via per-device query: {Percent}%.", battery);
                Publish();
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Bluetooth: per-device battery query failed for {DeviceId}.", deviceId);
        }
    }

    /// <summary>
    /// Direct GATT Battery Service read (and live subscription) for devices
    /// that don't publish battery through the AEP property. Parses the Bluetooth
    /// address from the AEP device id, opens a GATT session on the standard
    /// 0x180F service and reads every 0x2A19 characteristic (left/right buds
    /// often expose one each; the average is published).
    /// </summary>
    private async Task QueryGattBatteryAsync(string deviceId, string deviceName)
    {
        var address = ParseBluetoothAddress(deviceId);
        if (address is null)
        {
            _logger.LogDebug("Bluetooth: could not parse an address from {DeviceId} — GATT battery read skipped.", deviceId);
            return;
        }

        // One GATT session per device, kept for live updates.
        GattDeviceService? heldService = null;
        var levels = new List<byte>();

        try
        {
            var leDevice = await Windows.Devices.Bluetooth.BluetoothLEDevice.FromBluetoothAddressAsync(address.Value);
            if (leDevice is null)
            {
                _logger.LogInformation(
                    "Bluetooth: {Name} does not expose a GATT connection — battery % unavailable (device reports none).",
                    deviceName);
                return;
            }

            var servicesResult = await leDevice.GetGattServicesForUuidAsync(
                GattServiceUuids.Battery,
                Windows.Devices.Bluetooth.BluetoothCacheMode.Uncached);

            if (servicesResult.Status != GattCommunicationStatus.Success
                || servicesResult.Services.Count == 0)
            {
                _logger.LogInformation(
                    "Bluetooth: {Name} has no readable GATT Battery Service ({Status}) — battery % unavailable.",
                    deviceName,
                    servicesResult.Status);
                return;
            }

            foreach (var candidate in servicesResult.Services)
            {
                try
                {
                    var characteristics = await candidate.GetCharacteristicsForUuidAsync(
                        GattCharacteristicUuids.BatteryLevel,
                        Windows.Devices.Bluetooth.BluetoothCacheMode.Uncached);

                    foreach (var characteristic in characteristics.Characteristics)
                    {
                        var read = await characteristic.ReadValueAsync(Windows.Devices.Bluetooth.BluetoothCacheMode.Uncached);
                        var reader = Windows.Storage.Streams.DataReader.FromBuffer(read.Value);
                        var level = reader.ReadByte();
                        if (level <= 100)
                        {
                            levels.Add(level);
                        }

                        // Keep the first characteristic subscribed for live updates.
                        if (_gattCharacteristics.Count == 0)
                        {
                            _gattCharacteristics[deviceId] = characteristic;
                            characteristic.ValueChanged += OnGattValueChanged;
                        }
                    }

                    if (levels.Count > 0 && heldService is null)
                    {
                        heldService = candidate; // hold a reference so the session stays alive
                    }
                }
                catch
                {
                    // Characteristic read failures just mean this service slot has none.
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Bluetooth: GATT battery read failed for {Name}.", deviceName);
            return;
        }

        if (levels.Count == 0)
        {
            _logger.LogInformation(
                "Bluetooth: {Name} exposes no GATT battery level — battery % unavailable (hardware does not report it).",
                deviceName);
            return;
        }

        var percent = (int)Math.Round(levels.Average(l => l));
        bool changed;
        lock (_gate)
        {
            if (_devices.TryGetValue(deviceId, out var known))
            {
                _devices[deviceId] = known with { BatteryPercent = percent };
                changed = true;
            }
            else
            {
                changed = false;
            }
        }

        _logger.LogInformation(
            "Bluetooth: {Name} battery via GATT: {Percent}% ({Count} reading(s)).",
            deviceName,
            percent,
            levels.Count);

        if (changed)
        {
            Publish();
        }
    }

    private void OnGattValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
    {
        try
        {
            var reader = Windows.Storage.Streams.DataReader.FromBuffer(args.CharacteristicValue);
            var level = reader.ReadByte();
            if (level > 100)
            {
                return;
            }

            var changed = false;
            lock (_gate)
            {
                foreach (var key in _gattCharacteristics.Where(kvp => kvp.Value == sender).Select(kvp => kvp.Key).ToList())
                {
                    if (_devices.TryGetValue(key, out var known))
                    {
                        _devices[key] = known with { BatteryPercent = level };
                        changed = true;
                    }
                }
            }

            if (changed)
            {
                Publish();
            }
        }
        catch
        {
            // Malformed notifications are ignored.
        }
    }

    /// <summary>Extracts the 48-bit Bluetooth address from an AEP device id like "Bluetooth#BluetoothA0-B1…-C0D1E2F3A4B5".</summary>
    private static ulong? ParseBluetoothAddress(string deviceId)
    {
        try
        {
            var tail = deviceId.Split('-')[^1].Replace(":", string.Empty);
            if (tail.Length != 12 || !tail.All(Uri.IsHexDigit))
            {
                return null;
            }

            return Convert.ToUInt64(tail, 16);
        }
        catch
        {
            return null;
        }
    }

    private static string ExtractName(IReadOnlyDictionary<string, object> properties)
        => properties.TryGetValue("System.ItemNameDisplay", out var value)
            && value is string name
            && !string.IsNullOrWhiteSpace(name)
                ? name
                : string.Empty;

    private static bool ReadBool(IReadOnlyDictionary<string, object> properties, string key)
    {
        if (!properties.TryGetValue(key, out var value))
        {
            return false;
        }

        return value switch
        {
            bool b => b,
            IConvertible convertible => convertible.ToBoolean(System.Globalization.CultureInfo.InvariantCulture),
            _ => false,
        };
    }

    private static int? ReadBatteryPercent(IReadOnlyDictionary<string, object> properties)
    {
        if (!properties.TryGetValue("System.Devices.AepBattery_Level", out var value))
        {
            return null;
        }

        try
        {
            var raw = value switch
            {
                ulong u => (long)u,
                IConvertible convertible => convertible.ToInt64(System.Globalization.CultureInfo.InvariantCulture),
                _ => -1,
            };

            if (raw is < 0 or > 100)
            {
                return null;
            }

            return (int)raw;
        }
        catch
        {
            return null;
        }
    }

    private void Publish()
    {
        StatusChanged?.Invoke(Current);
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

            if (_watcher is not null)
            {
                try
                {
                    _watcher.Added -= OnWatcherAdded;
                    _watcher.Updated -= OnWatcherUpdated;
                    _watcher.Removed -= OnWatcherRemoved;
                    _watcher.Stop();
                    _watcher = null;
                }
                catch
                {
                    // Best effort during shutdown.
                }
            }
        }
    }
}
