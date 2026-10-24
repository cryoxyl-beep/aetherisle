namespace GrumphySwarm.Core.Messaging;

/// <summary>Severity/meaning of a status update shown by the notch.</summary>
public enum NotchStatusLevel
{
    Idle,
    Info,
    Busy,
    Success,
    Warning,
    Error,
}

/// <summary>
/// One status update. Future producers (agent runtime, media watcher, task
/// monitor...) describe themselves with a stable <paramref name="Key"/> so the
/// UI can replace a producer's previous update instead of accumulating noise.
/// </summary>
/// <param name="Key">Stable producer key, e.g. <c>host</c>, <c>agent.maps</c>.</param>
/// <param name="Label">Short headline, e.g. <c>Summarizing</c>.</param>
/// <param name="Level">Semantic level driving color/presentation.</param>
/// <param name="Timestamp">When the update was produced.</param>
/// <param name="Detail">Optional longer line.</param>
public sealed record NotchStatusUpdate(
    string Key,
    string Label,
    NotchStatusLevel Level,
    DateTimeOffset Timestamp,
    string? Detail = null);

/// <summary>
/// Producer side of the status channel. Future background services implement or
/// use this to publish status without any knowledge of the notch UI.
/// </summary>
public interface INotchStatusPublisher
{
    void Publish(NotchStatusUpdate update);
}

/// <summary>Consumer side of the status channel (used by the notch UI).</summary>
public interface INotchStatusFeed
{
    /// <summary>Fire-and-forget notification. Producers may raise this from any thread.</summary>
    event Action<NotchStatusUpdate>? UpdateReceived;

    /// <summary>Latest update published under a key, if any.</summary>
    NotchStatusUpdate? Latest(string key);
}

/// <summary>
/// Lightweight in-process status bus: thread-safe publish/subscribe plus a
/// latest-per-key snapshot. Deliberately minimal — when a real agent runtime
/// arrives in a later milestone it can either keep using this or sit behind it
/// in a separate process and forward updates over a named pipe.
/// </summary>
public sealed class NotchStatusBus : INotchStatusPublisher, INotchStatusFeed
{
    private readonly object _gate = new();
    private readonly Dictionary<string, NotchStatusUpdate> _latest = new();

    public event Action<NotchStatusUpdate>? UpdateReceived;

    /// <summary>Number of distinct producers that have published so far (diagnostics).</summary>
    public int ProducerCount
    {
        get
        {
            lock (_gate)
            {
                return _latest.Count;
            }
        }
    }

    public void Publish(NotchStatusUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update);

        Action<NotchStatusUpdate>? handlers;
        lock (_gate)
        {
            _latest[update.Key] = update;
            handlers = UpdateReceived;
        }

        // Invoked outside the lock; subscribers marshal to their own thread.
        handlers?.Invoke(update);
    }

    public NotchStatusUpdate? Latest(string key)
    {
        lock (_gate)
        {
            return _latest.TryGetValue(key, out var update) ? update : null;
        }
    }
}
