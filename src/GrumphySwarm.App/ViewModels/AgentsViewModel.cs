using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GrumphySwarm.Core.Agents;
using Microsoft.Extensions.Logging;

namespace GrumphySwarm.App.ViewModels;

/// <summary>
/// Agents workspace: local agent profiles only in this milestone. Profiles are
/// configuration, never running agents — the runtime that will eventually
/// execute them is intentionally not connected yet (<see cref="IAgentRuntime"/>
/// is the future seam).
/// </summary>
public partial class AgentsViewModel : ObservableObject
{
    private readonly IAgentProfileStore _store;
    private readonly ILogger<AgentsViewModel> _logger;

    public AgentsViewModel(IAgentProfileStore store, ILogger<AgentsViewModel> logger)
    {
        _store = store;
        _logger = logger;

        foreach (var profile in store.Load().OrderBy(p => p.Name, StringComparer.CurrentCulture))
        {
            Profiles.Add(profile);
        }

        HasProfiles = Profiles.Count > 0;
    }

    public ObservableCollection<AgentProfile> Profiles { get; } = [];

    [ObservableProperty]
    private bool hasProfiles;

    /// <summary>Fired when the user wants to create or edit a profile (null = create new).</summary>
    public event Action<AgentProfile?>? EditRequested;

    [RelayCommand]
    private void NewAgent() => EditRequested?.Invoke(null);

    [RelayCommand]
    private void EditAgent(AgentProfile? profile)
    {
        if (profile is not null)
        {
            EditRequested?.Invoke(profile);
        }
    }

    [RelayCommand]
    private void DeleteAgent(AgentProfile? profile)
    {
        if (profile is null)
        {
            return;
        }

        Profiles.Remove(profile);
        Persist();
        _logger.LogInformation("Agent profile '{Name}' deleted.", profile.Name);
    }

    /// <summary>
    /// Creates or updates a profile. Returns an error message, or null on
    /// success. The caller (editor window) shows the message inline.
    /// </summary>
    public string? TrySave(AgentProfile? existing, string name, string taskTypeId, string description)
    {
        var error = AgentProfileValidation.Validate(name, taskTypeId, description);
        if (error is not null)
        {
            return error;
        }

        name = name.Trim();
        description = description.Trim();
        var now = DateTime.UtcNow;

        if (existing is null)
        {
            var profile = new AgentProfile(Guid.NewGuid().ToString("N"), name, taskTypeId, description, now, now);
            var insertAt = Profiles.ToList().FindIndex(p => string.Compare(p.Name, name, StringComparison.CurrentCulture) > 0);
            if (insertAt < 0)
            {
                Profiles.Add(profile);
            }
            else
            {
                Profiles.Insert(insertAt, profile);
            }

            _logger.LogInformation("Agent profile '{Name}' created ({TaskType}).", name, taskTypeId);
        }
        else
        {
            var updated = existing with
            {
                Name = name,
                TaskTypeId = taskTypeId,
                Description = description,
                UpdatedAtUtc = now,
            };

            var index = Profiles.IndexOf(existing);
            if (index >= 0)
            {
                Profiles[index] = updated;
            }
            else
            {
                Profiles.Add(updated);
            }

            _logger.LogInformation("Agent profile '{Name}' updated.", name);
        }

        Persist();
        return null;
    }

    private void Persist()
    {
        HasProfiles = Profiles.Count > 0;
        try
        {
            _store.Save(Profiles.ToList());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Agent profiles could not be persisted.");
        }
    }
}
