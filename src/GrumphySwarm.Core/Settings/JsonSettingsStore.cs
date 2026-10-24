using System.IO;
using System.Text.Json;

namespace GrumphySwarm.Core.Settings;

/// <summary>Loads and persists <see cref="NotchSettings"/>.</summary>
public interface ISettingsStore
{
    NotchSettings Load();

    void Save(NotchSettings settings);
}

/// <summary>
/// JSON-file backed settings store. The file lives in
/// %APPDATA%\GrumphySwarm\settings.json by default. Reads never throw: a missing
/// or corrupt file yields defaults, and writes are atomic (temp file + move).
/// </summary>
public sealed class JsonSettingsStore : ISettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly string _filePath;

    public JsonSettingsStore(string? filePath = null)
    {
        _filePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "GrumphySwarm",
            "settings.json");
    }

    /// <summary>Path of the settings file (exposed for diagnostics and tests).</summary>
    public string FilePath => _filePath;

    public NotchSettings Load()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                return new NotchSettings();
            }

            var json = File.ReadAllText(_filePath);
            var settings = JsonSerializer.Deserialize<NotchSettings>(json, SerializerOptions);
            return settings ?? new NotchSettings();
        }
        catch
        {
            // Corrupt or unreadable settings must never prevent startup.
            return new NotchSettings();
        }
    }

    public void Save(NotchSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(settings, SerializerOptions);

        // Atomic replace: a crash mid-write can never leave a truncated file behind.
        var temporaryPath = _filePath + ".tmp";
        File.WriteAllText(temporaryPath, json);

        if (File.Exists(_filePath))
        {
            File.Replace(temporaryPath, _filePath, destinationBackupFileName: null);
        }
        else
        {
            File.Move(temporaryPath, _filePath);
        }
    }
}
