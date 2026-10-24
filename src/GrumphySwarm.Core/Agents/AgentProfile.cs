namespace GrumphySwarm.Core.Agents;

/// <summary>
/// A locally stored agent profile. A profile is configuration only — it is NOT
/// a running agent. Execution arrives in a future milestone via
/// <see cref="IAgentRuntime"/>.
/// </summary>
/// <param name="Id">Stable unique id (Guid string).</param>
/// <param name="Name">User-chosen display name.</param>
/// <param name="TaskTypeId">One of <see cref="AgentTaskTypes"/> ids.</param>
/// <param name="Description">Free-text task description.</param>
/// <param name="CreatedAtUtc">Creation time.</param>
/// <param name="UpdatedAtUtc">Last modification time.</param>
public sealed record AgentProfile(
    string Id,
    string Name,
    string TaskTypeId,
    string Description,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

/// <summary>Predefined task types offered when creating an agent profile.</summary>
public static class AgentTaskTypes
{
    public sealed record AgentTaskType(string Id, string DisplayName, string Description);

    public static readonly IReadOnlyList<AgentTaskType> All =
    [
        new("companion", "Companion", "A friendly desktop presence that reacts to activity."),
        new("research", "Research assistant", "Summarizes topics and gathers references on request."),
        new("task-monitor", "Task monitor", "Watches apps, files or timers and reports status."),
        new("media-helper", "Media helper", "Controls and annotates media playback."),
        new("custom", "Custom", "Free-form agent with no preset behavior."),
    ];

    public static AgentTaskType? Find(string? id) =>
        All.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase));

    public static bool IsValid(string? id) => Find(id) is not null;

    public static string DisplayNameOf(string? id) => Find(id)?.DisplayName ?? "Custom";
}

/// <summary>Validation rules for agent profiles (shared by store and UI).</summary>
public static class AgentProfileValidation
{
    public static int MaxNameLength => 32;

    public static int MaxDescriptionLength => 400;

    /// <summary>Returns an error message, or null when the values are valid.</summary>
    public static string? Validate(string? name, string? taskTypeId, string? description)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "Give the agent a name.";
        }

        if (name.Trim().Length > MaxNameLength)
        {
            return $"Name must be {MaxNameLength} characters or fewer.";
        }

        if (!AgentTaskTypes.IsValid(taskTypeId))
        {
            return "Choose a task type.";
        }

        if ((description ?? string.Empty).Length > MaxDescriptionLength)
        {
            return $"Description must be {MaxDescriptionLength} characters or fewer.";
        }

        return null;
    }
}

/// <summary>Loads and persists agent profiles locally.</summary>
public interface IAgentProfileStore
{
    IReadOnlyList<AgentProfile> Load();

    void Save(IReadOnlyList<AgentProfile> profiles);
}

/// <summary>
/// JSON-file backed profile store (agents.json next to settings.json). Reads
/// never throw; writes are atomic. Unknown task types are preserved as-is so
/// downgrades don't destroy data.
/// </summary>
public sealed class JsonAgentProfileStore : IAgentProfileStore
{
    private static readonly System.Text.Json.JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
    };

    private readonly string _filePath;

    public JsonAgentProfileStore(string? filePath = null)
    {
        _filePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "GrumphySwarm",
            "agents.json");
    }

    public string FilePath => _filePath;

    public IReadOnlyList<AgentProfile> Load()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                return [];
            }

            var json = File.ReadAllText(_filePath);
            var profiles = System.Text.Json.JsonSerializer.Deserialize<List<AgentProfile>>(json, Options);
            return profiles ?? [];
        }
        catch
        {
            return [];
        }
    }

    public void Save(IReadOnlyList<AgentProfile> profiles)
    {
        ArgumentNullException.ThrowIfNull(profiles);

        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = System.Text.Json.JsonSerializer.Serialize(profiles, Options);
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

/// <summary>
/// Future lifecycle surface for the agent runtime. Deliberately NOT implemented
/// in this milestone — the Agents UI stores and shows profiles only. When the
/// runtime arrives (separate process or in-proc service), it implements this
/// and publishes status without the notch knowing any AI details.
/// </summary>
public interface IAgentRuntime
{
    /// <summary>Runtime display state for an agent id.</summary>
    event Action<string>? AgentStatusChanged;

    /// <summary>Starts the agent with the given profile. Not implemented yet.</summary>
    Task StartAgentAsync(AgentProfile profile, CancellationToken cancellationToken);

    /// <summary>Stops the agent with the given id. Not implemented yet.</summary>
    Task StopAgentAsync(string agentId);

    /// <summary>Current status word for an agent ("stopped", "running", …).</summary>
    string QueryStatus(string agentId);
}
