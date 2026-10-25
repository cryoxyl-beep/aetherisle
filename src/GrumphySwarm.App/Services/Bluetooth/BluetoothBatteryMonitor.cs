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

    /// <summary>Raw fmtid/pid form of PKEY_Devices_Aep_BatteryLevel — some
    /// Windows builds reject the canonical name in watcher queries but accept
    /// (and key results by) this form.</summary>
    private static readonly string[] FullPropertiesRaw =
    [
        "System.ItemNameDisplay",
        "System.Devices.Aep.IsConnected",
        "{83DA6326-97A6-4088-9453-A1923F573B29} 4",
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
    private readonly List<Windows.Devices.Bluetooth.GenericAttributeProfile.GattDeviceService> _gattServiceHolders = [];
    private readonly HashSet<string> _gattAttempted = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// HFP battery property exposed on Bluetooth headset PnP devnodes
    /// (DEVPKEY used by headset battery tools; raw fmtid/pid form).
    /// </summary>
    private static readonly string HfpBatteryKey = "{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2";

    private static readonly string[] PnpProperties =
    [
        HfpBatteryKey,
        "{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 3",
    ];

    private DeviceWatcher? _watcher;
    private DeviceWatcher? _containerWatcher;
    private DeviceWatcher? _serviceWatcher;
    private DeviceWatcher? _pnpWatcher;
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

            var selector = string.Empty; // ALL association endpoints — the
            // battery for TWS buds may surface on the classic, LE, AVRCP or
            // HFP endpoint; a protocol-filtered watcher misses some of them.

            // Property-list fallback chain: some builds reject individual keys.
            foreach (var properties in new[] { FullProperties, FullPropertiesRaw, ConnectedProperties, NameOnlyProperties })
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

            // Windows Settings shows battery on the device CONTAINER page; the
            // value may be aggregated there even when the raw endpoint property
            // reads empty. Watch containers too.
            try
            {
                _containerWatcher = DeviceInformation.CreateWatcher(
                    string.Empty,
                    _activeProperties,
                    DeviceInformationKind.AssociationEndpointContainer);
                _containerWatcher.Added += OnWatcherAdded;
                _containerWatcher.Updated += OnWatcherUpdated;
                _containerWatcher.Removed += OnWatcherRemoved;
                _containerWatcher.Start();
                _logger.LogInformation("Bluetooth: container watcher active (Settings-style battery path).");
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Bluetooth: container watcher unavailable.");
                _containerWatcher = null;
            }

            // GATT services surface as AssociationEndpointService objects, and a
            // battery service often carries System.Devices.AepBattery_Level
            // there even when the device-level property reads empty. Service
            // names are frequently blank, so allow-list matching falls back to
            // the parent endpoint id embedded in the service id.
            try
            {
                _serviceWatcher = DeviceInformation.CreateWatcher(
                    string.Empty,
                    _activeProperties,
                    DeviceInformationKind.AssociationEndpointService);
                _serviceWatcher.Added += OnWatcherAdded;
                _serviceWatcher.Updated += OnWatcherUpdated;
                _serviceWatcher.Removed += OnWatcherRemoved;
                _serviceWatcher.Start();
                _logger.LogInformation("Bluetooth: service endpoint watcher active (GATT battery service path).");
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Bluetooth: service watcher unavailable.");
                _serviceWatcher = null;
            }

            // HFP route: classic headsets report battery on their PnP devnode
            // through {104EA319-…} 2 — the same value the Settings flyout
            // renders. Names on devnodes look like "Airdopes 138 Hands-Free AG",
            // so the allow-list's contains-match still applies.
            try
            {
                _pnpWatcher = DeviceInformation.CreateWatcher(
                    string.Empty,
                    PnpProperties,
                    DeviceInformationKind.Device);
                _pnpWatcher.Added += OnPnpDeviceEvent;
                _pnpWatcher.Start();
                _logger.LogInformation("Bluetooth: PnP HFP battery watcher active.");
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Bluetooth: PnP watcher unavailable.");
                _pnpWatcher = null;
            }

            IsSupported = true;
            _logger.LogInformation(
                "Bluetooth: allow-listed accessory monitoring active (all AEPs, event-driven, {PropertyCount} properties watched).",
                _activeProperties.Length);
        }
    }

    /// <summary>PnP devnode battery (HFP). Devnode names carry the model name,
    /// possibly with a profile suffix ("Airdopes 138 Hands-Free AG").</summary>
    private void OnPnpDeviceEvent(DeviceWatcher sender, DeviceInformation info)
    {
        try
        {
            HandlePnpDevice(info);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Bluetooth: PnP handler failed (ignored).");
        }
    }

    private void HandlePnpDevice(DeviceInformation info)
    {
        var battery = ReadBatteryPercent(info.Properties);
        if (battery is null || !MediaAllowList.IsEarbudAllowed(info.Name))
        {
            return;
        }

        var percent2 = battery.Value;
        lock (_gate)
        {
            var name = info.Name;
            var applied = false;

            // Merge into any existing entry of the same physical device: PnP
            // names carry profile suffixes ("Airdopes 138 Hands-Free AG"), so
            // match on either name containing the other.
            foreach (var key in _devices.Keys.ToList())
            {
                var existing = _devices[key].Name;
                if (string.Equals(existing, name, StringComparison.OrdinalIgnoreCase)
                    || name.Contains(existing, StringComparison.OrdinalIgnoreCase)
                    || existing.Contains(name, StringComparison.OrdinalIgnoreCase))
                {
                    _devices[key] = _devices[key] with { BatteryPercent = percent2, IsConnected = true };
                    applied = true;
                }
            }

            // …or record the devnode itself (battery implies connected).
            if (!applied)
            {
                _devices["pnp:" + info.Id] = new EarbudStatus(name, percent2, true);
            }
        }

        _logger.LogInformation("Bluetooth: {Name} battery {Percent}% via PnP HFP property.", info.Name, percent2);
        Publish();
    }

    private void OnWatcherAdded(DeviceWatcher sender, DeviceInformation info)
    {
        try
        {
            UpdateFromProperties(info.Id, info.Name, info.Properties);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Bluetooth: watcher-added handler failed (ignored).");
        }
    }

    private void OnWatcherUpdated(DeviceWatcher sender, DeviceInformationUpdate update)
    {
        try
        {
            UpdateFromProperties(update.Id, ExtractName(update.Properties), update.Properties);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Bluetooth: watcher-updated handler failed (ignored).");
        }
    }

    private void OnWatcherRemoved(DeviceWatcher sender, DeviceInformationUpdate update)
    {
        try
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
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Bluetooth: watcher-removed handler failed (ignored).");
        }
    }

    private void UpdateFromProperties(string id, string name, IReadOnlyDictionary<string, object> properties)
    {
        var battery = ReadBatteryPercent(properties);

        // Service endpoints often have blank names — match them to an
        // allow-listed parent via the id prefix instead.
        if (!MediaAllowList.IsEarbudAllowed(name))
        {
            if (battery is not { } percent || !TryApplyToParentDevice(id, percent))
            {
                return;
            }

            Publish();
            return;
        }

        var connected = ReadBool(properties, "System.Devices.Aep.IsConnected");

        lock (_gate)
        {
            _devices[id] = new EarbudStatus(name, battery, connected);

            // Merge: a battery found on one entry (e.g. the container or LE
            // endpoint) fills sibling entries of the same device name.
            if (battery is { } percent)
            {
                foreach (var key in _devices.Keys.ToList())
                {
                    if (key != id && _devices[key].Name == name && _devices[key].BatteryPercent is null)
                    {
                        _devices[key] = _devices[key] with { BatteryPercent = percent };
                    }
                }
            }
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
        // Battery values often appear a few seconds AFTER the device connects.
        // Try the canonical property form first, then refresh the device a
        // couple of times (UpdateDeviceInformationAsync re-reads the store).
        if (await TryReadBatteryAsync(deviceId) is { } immediate)
        {
            ApplyBattery(deviceId, immediate, "immediate read");
            return;
        }

        foreach (var delay in new[] { 2000, 6000 })
        {
            await Task.Delay(delay);
            if (_disposed)
            {
                return;
            }

            // CreateFromIdAsync re-reads the current property store — battery
            // values often land there a few seconds after the connect event.
            if (await TryReadBatteryAsync(deviceId) is { } percent)
            {
                ApplyBattery(deviceId, percent, "delayed read");
                return;
            }
        }

        if (!_batteryDiagnosticsLogged)
        {
            _batteryDiagnosticsLogged = true;
            _logger.LogInformation(
                "Bluetooth: battery value never appeared for this device — the AEP property stays empty on this build.");
        }
    }

    private async Task<int?> TryReadBatteryAsync(string deviceId)
    {
        try
        {
            var device = await DeviceInformation.CreateFromIdAsync(
                deviceId,
                ["System.Devices.AepBattery_Level"],
                DeviceInformationKind.AssociationEndpoint);
            return ReadBatteryPercent(device.Properties);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Bluetooth: canonical battery read failed for {DeviceId}.", deviceId);
            return null;
        }
    }

    private void ApplyBattery(string deviceId, int percent, string source)
    {
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

        if (changed)
        {
            _logger.LogInformation("Bluetooth: battery {Percent}% ({Source}).", percent, source);
            Publish();
        }
    }

    private bool _batteryDiagnosticsLogged;

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
            _logger.LogInformation(
                "Bluetooth: could not parse a Bluetooth address from '{DeviceId}' — GATT read skipped.",
                deviceId);
            return;
        }

        // GATT Battery Service over LE (the only GATT surface WinRT exposes).
        var levels = new List<byte>();

        try
        {
            var leDevice = await Windows.Devices.Bluetooth.BluetoothLEDevice.FromBluetoothAddressAsync(address.Value);
            if (leDevice is not null)
            {
                await CollectBatteryLevelsAsync(
                    () => leDevice.GetGattServicesForUuidAsync(
                        GattServiceUuids.Battery,
                        Windows.Devices.Bluetooth.BluetoothCacheMode.Uncached).AsTask(),
                    levels,
                    deviceId);
            }

            if (levels.Count == 0)
            {
                _logger.LogInformation(
                    "Bluetooth: {Name} exposes no GATT Battery Service ({Address:X12}) — battery % unavailable from this channel.",
                    deviceName,
                    address.Value);
                return;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Bluetooth: GATT battery read failed for {Name}.", deviceName);
            return;
        }

        var percent = (int)Math.Round(levels.Average(l => l));
        ApplyBattery(deviceId, percent, "GATT");

        _logger.LogInformation(
            "Bluetooth: {Name} battery via GATT: {Percent}% ({Count} reading(s)).",
            deviceName,
            percent,
            levels.Count);
    }

    /// <summary>
    /// Reads every Battery Service (0x180F) characteristic from a GATT services
    /// result and keeps the first one subscribed for live updates. Service
    /// instances are retained so the GATT session stays alive.
    /// </summary>
    private async Task CollectBatteryLevelsAsync(
        Func<Task<Windows.Devices.Bluetooth.GenericAttributeProfile.GattDeviceServicesResult>> getServices,
        List<byte> levels,
        string deviceId)
    {
        var servicesResult = await getServices();

        if (servicesResult.Status != GattCommunicationStatus.Success
            || servicesResult.Services.Count == 0)
        {
            return;
        }

        foreach (var candidate in servicesResult.Services.Where(s => s.Uuid == GattServiceUuids.Battery))
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
                    lock (_gate)
                    {
                        if (!_gattCharacteristics.ContainsKey(deviceId))
                        {
                            _gattCharacteristics[deviceId] = characteristic;
                            _gattServiceHolders.Add(candidate);
                            characteristic.ValueChanged += OnGattValueChanged;
                        }
                    }
                }
            }
            catch
            {
                // Characteristic read failures just mean this slot has none.
            }
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
        // Accept whichever key form the system used for the battery property:
        // the AEP canonical name, its raw fmtid/pid form, or the HFP devnode key.
        foreach (var key in properties.Keys)
        {
            var isBatteryKey = key.Contains("AepBattery", StringComparison.OrdinalIgnoreCase)
                || key.StartsWith("{83DA6326-97A6-4088-9453-A1923F573B29}", StringComparison.OrdinalIgnoreCase)
                || key.StartsWith("{104EA319-6EE2-4701-BD47-8DDBF425BBE5}", StringComparison.OrdinalIgnoreCase);
            if (!isBatteryKey)
            {
                continue;
            }

            try
            {
                var raw = properties[key] switch
                {
                    ulong u => (long)u,
                    IConvertible convertible => convertible.ToInt64(System.Globalization.CultureInfo.InvariantCulture),
                    _ => -1,
                };

                if (raw is >= 0 and <= 100)
                {
                    return (int)raw;
                }
            }
            catch
            {
                // Wrong shape for this key — try the next one.
            }
        }

        return null;
    }

    /// <summary>
    /// Service endpoint ids embed their parent endpoint id as a prefix; applies
    /// a battery reading to the matching allow-listed parent device.
    /// </summary>
    private bool TryApplyToParentDevice(string serviceId, int percent)
    {
        lock (_gate)
        {
            foreach (var key in _devices.Keys.ToList())
            {
                if (serviceId.StartsWith(key, StringComparison.OrdinalIgnoreCase))
                {
                    _devices[key] = _devices[key] with { BatteryPercent = percent };
                    _logger.LogInformation("Bluetooth: battery {Percent}% via service endpoint.", percent);
                    return true;
                }
            }
        }

        return false;
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

            foreach (var watcher in new[] { _watcher, _containerWatcher, _serviceWatcher, _pnpWatcher })
            {
                if (watcher is null)
                {
                    continue;
                }

                try
                {
                    watcher.Added -= OnWatcherAdded;
                    watcher.Updated -= OnWatcherUpdated;
                    watcher.Removed -= OnWatcherRemoved;
                    watcher.Stop();
                }
                catch
                {
                    // Best effort during shutdown.
                }
            }

            _watcher = null;
            _containerWatcher = null;
            _serviceWatcher = null;
        }
    }
}
