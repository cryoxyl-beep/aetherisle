using GrumphySwarm.Core.Agents;

namespace GrumphySwarm.Tests.Agents;

public class AgentProfileStoreTests : IDisposable
{
    private readonly string _tempDirectory;

    public AgentProfileStoreTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"grumphy-agents-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDirectory);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDirectory, recursive: true); }
        catch (IOException) { }
    }

    private string StorePath => Path.Combine(_tempDirectory, "agents.json");

    private static AgentProfile Sample(string name = "Research buddy", string taskTypeId = "research") => new(
        Guid.NewGuid().ToString("N"), name, taskTypeId, "Summarize papers",
        DateTime.UtcNow, DateTime.UtcNow);

    [Fact]
    public void Missing_file_returns_empty_list()
    {
        var store = new JsonAgentProfileStore(StorePath);
        Assert.Empty(store.Load());
    }

    [Fact]
    public void Roundtrip_preserves_profiles()
    {
        var store = new JsonAgentProfileStore(StorePath);
        var profiles = new List<AgentProfile> { Sample("Alpha", "companion"), Sample("Beta", "task-monitor") };

        store.Save(profiles);

        var loaded = store.Load();
        Assert.Equal(2, loaded.Count);
        Assert.Equal("Alpha", loaded[0].Name);
        Assert.Equal("companion", loaded[0].TaskTypeId);
        Assert.Equal("Beta", loaded[1].Name);
    }

    [Fact]
    public void Corrupt_file_returns_empty_without_throwing()
    {
        File.WriteAllText(StorePath, "{ not json at all");
        var store = new JsonAgentProfileStore(StorePath);
        Assert.Empty(store.Load());
    }

    [Fact]
    public void Create_update_delete_cycle_persists_every_step()
    {
        var store = new JsonAgentProfileStore(StorePath);
        var original = Sample("Watcher", "task-monitor");

        store.Save([original]);
        Assert.Single(store.Load());

        var updated = original with { Name = "Watcher 2", Description = "Watches files" };
        store.Save([updated]);
        var afterUpdate = store.Load();
        var persisted = Assert.Single(afterUpdate);
        Assert.Equal("Watcher 2", persisted.Name);
        Assert.Equal("Watches files", persisted.Description);
        Assert.Equal(original.Id, persisted.Id); // same agent, updated in place

        store.Save([]);
        Assert.Empty(store.Load());
    }
}

public class AgentProfileValidationTests
{
    [Fact]
    public void Name_is_required()
    {
        Assert.NotNull(AgentProfileValidation.Validate("", "companion", null));
        Assert.NotNull(AgentProfileValidation.Validate("   ", "companion", null));
    }

    [Fact]
    public void Name_length_is_capped()
    {
        var longName = new string('a', AgentProfileValidation.MaxNameLength + 1);
        Assert.NotNull(AgentProfileValidation.Validate(longName, "companion", null));
        Assert.Null(AgentProfileValidation.Validate(new string('a', AgentProfileValidation.MaxNameLength), "companion", null));
    }

    [Fact]
    public void Task_type_must_be_predefined()
    {
        Assert.Null(AgentProfileValidation.Validate("Bot", "custom", "does things"));
        Assert.NotNull(AgentProfileValidation.Validate("Bot", "brain-surgery", null));
        Assert.NotNull(AgentProfileValidation.Validate("Bot", null, null));
    }

    [Fact]
    public void Task_type_catalogue_is_stable()
    {
        Assert.Equal(5, AgentTaskTypes.All.Count);
        Assert.NotNull(AgentTaskTypes.Find("companion"));
        Assert.Null(AgentTaskTypes.Find("nonexistent"));
        Assert.Equal("Research assistant", AgentTaskTypes.DisplayNameOf("research"));
        Assert.Equal("Custom", AgentTaskTypes.DisplayNameOf("something-else")); // graceful fallback
    }
}
