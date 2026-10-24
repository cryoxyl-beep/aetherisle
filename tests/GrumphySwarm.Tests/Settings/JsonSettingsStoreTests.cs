using GrumphySwarm.Core.Settings;

namespace GrumphySwarm.Tests.Settings;

public class JsonSettingsStoreTests : IDisposable
{
    private readonly string _tempDirectory;

    public JsonSettingsStoreTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"grumphy-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDirectory);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDirectory, recursive: true); }
        catch (IOException) { }
    }

    private string SettingsPath => Path.Combine(_tempDirectory, "settings.json");

    [Fact]
    public void Missing_file_returns_defaults()
    {
        var store = new JsonSettingsStore(SettingsPath);

        var settings = store.Load();

        Assert.Equal(350, settings.CollapseDelayMs);
        Assert.Equal(4000, settings.ProgrammaticExpandLingerMs);
        Assert.False(settings.ReducedMotion);
    }

    [Fact]
    public void Roundtrip_preserves_values()
    {
        var store = new JsonSettingsStore(SettingsPath);
        var original = new NotchSettings
        {
            CollapseDelayMs = 900,
            ProgrammaticExpandLingerMs = 8000,
            ReducedMotion = true,
            ShowClockInPill = false,
        };

        store.Save(original);
        var loaded = store.Load();

        Assert.Equal(900, loaded.CollapseDelayMs);
        Assert.Equal(8000, loaded.ProgrammaticExpandLingerMs);
        Assert.True(loaded.ReducedMotion);
        Assert.False(loaded.ShowClockInPill);
    }

    [Fact]
    public void Corrupt_file_returns_defaults_without_throwing()
    {
        File.WriteAllText(SettingsPath, "{ this is not json !!!");

        var store = new JsonSettingsStore(SettingsPath);
        var settings = store.Load();

        Assert.Equal(new NotchSettings().CollapseDelayMs, settings.CollapseDelayMs);
    }

    [Fact]
    public void Save_creates_missing_directories_and_writes_camel_case_json()
    {
        var nestedPath = Path.Combine(_tempDirectory, "deep", "folder", "settings.json");
        var store = new JsonSettingsStore(nestedPath);

        store.Save(new NotchSettings { CollapseDelayMs = 4242 });

        Assert.True(File.Exists(nestedPath));
        Assert.Contains("collapseDelayMs", File.ReadAllText(nestedPath));
    }
}
