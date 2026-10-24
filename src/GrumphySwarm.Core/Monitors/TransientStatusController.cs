namespace GrumphySwarm.Core.Monitors;

/// <summary>What kind of transient status is showing in the pill.</summary>
public enum TransientKind
{
    Volume,
    Brightness,
}

/// <summary>
/// Pure controller for "show briefly, then restore" pill status (volume,
/// brightness). Uses the same generation-token pattern as the notch state
/// machine: every Show bumps the generation and schedules an expiration; only
/// the current generation can hide it. This makes repeated rapid changes
/// deterministic and impossible to leave a stale timer behind.
///
/// The machine is pure — no timers inside. The host wires ExpirationScheduled
/// to one restartable timer and calls Expire(generation) when it fires.
/// </summary>
public sealed class TransientStatusController
{
    private int _generation;

    public TransientKind? ActiveKind { get; private set; }

    public bool IsVisible { get; private set; }

    /// <summary>Current token; only an Expire with this value hides the status.</summary>
    public int Generation => _generation;

    public event Action? Shown;

    public event Action<int, int>? ExpirationScheduled;

    public event Action? Hidden;

    /// <summary>Shows (or renews) the transient status for <paramref name="kind"/>.</summary>
    public void Show(TransientKind kind, int durationMs)
    {
        var kindChanged = ActiveKind != kind;
        ActiveKind = kind;
        IsVisible = true;
        _generation++;

        if (kindChanged)
        {
            Shown?.Invoke();
        }

        ExpirationScheduled?.Invoke(_generation, Math.Max(0, durationMs));
    }

    /// <summary>Expires the status; returns true only when it actually hid it.</summary>
    public bool Expire(int generation)
    {
        if (!IsVisible || generation != _generation)
        {
            return false;
        }

        IsVisible = false;
        ActiveKind = null;
        Hidden?.Invoke();
        return true;
    }

    /// <summary>Hides immediately regardless of pending expiration (e.g. shutdown).</summary>
    public void HideNow()
    {
        if (!IsVisible)
        {
            return;
        }

        IsVisible = false;
        ActiveKind = null;
        _generation++;
        Hidden?.Invoke();
    }
}
