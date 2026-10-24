using GrumphySwarm.Core.Settings;

namespace GrumphySwarm.Core.State;

/// <summary>
/// Explicit, deterministic interaction state machine for the notch.
///
/// The machine is pure logic: it owns no timers or window handles. Time-based
/// behavior (the collapse delay) is expressed as events that the host wires to a
/// single timer, plus a generation token that makes stale timer ticks harmless.
/// This keeps the machine fully unit-testable without a live desktop.
///
/// Threading contract: instances are not thread-safe; call all members from one
/// thread (in the app, the WPF UI thread).
/// </summary>
public sealed class NotchStateMachine
{
    private readonly NotchSettings _settings;

    public NotchStateMachine(NotchSettings settings)
    {
        _settings = settings;
    }

    /// <summary>Current interaction state.</summary>
    public NotchState State { get; private set; } = NotchState.Collapsed;

    /// <summary>Whether the pointer is currently inside the pill/panel interaction region.</summary>
    public bool IsPointerInside { get; private set; }

    /// <summary>Whether a delayed collapse is currently scheduled.</summary>
    public bool IsCollapsePending { get; private set; }

    /// <summary>
    /// Monotonic token for the currently scheduled collapse. Only a
    /// <see cref="CollapseElapsed"/> call carrying the current generation can
    /// collapse the notch; stale ticks are ignored. Rescheduling bumps the value,
    /// which implicitly cancels any earlier schedule.
    /// </summary>
    public int CollapseGeneration { get; private set; }

    /// <summary>True once <see cref="BeginShutdown"/> ran; all inputs are then ignored.</summary>
    public bool IsShutdown { get; private set; }

    /// <summary>Raised after every successful state change.</summary>
    public event Action<NotchTransition>? Transitioned;

    /// <summary>
    /// Raised when a collapse should be scheduled:
    /// (generation, delay in milliseconds). The host starts or restarts one timer
    /// and later calls <see cref="CollapseElapsed"/> with the same generation.
    /// </summary>
    public event Action<int, int>? CollapseScheduled;

    /// <summary>Raised when a previously scheduled collapse is cancelled (pin, re-enter, dismiss...).</summary>
    public event Action? CollapseCancelled;

    // ------------------------------------------------------------------ inputs

    /// <summary>Pointer entered the pill or the expanded panel.</summary>
    public void PointerEntered()
    {
        if (IsShutdown) return;

        IsPointerInside = true;
        CancelPendingCollapse();

        if (State == NotchState.Collapsed)
        {
            Transition(NotchState.Expanded, "pointer entered the pill");
        }
        // Expanded / Pinned / EditMode intentionally stay: entering never closes.
    }

    /// <summary>Pointer left the pill/panel interaction region.</summary>
    public void PointerLeft()
    {
        if (IsShutdown) return;

        IsPointerInside = false;

        if (State == NotchState.Expanded && !IsCollapsePending)
        {
            ScheduleCollapse(_settings.CollapseDelayMs, "pointer left");
        }
        // Pinned: stays open. EditMode: reserved, stays open. Collapsed: nothing to do.
    }

    /// <summary>
    /// A previously scheduled collapse timer fired. Returns true only when the
    /// tick was current and actually collapsed the notch.
    /// </summary>
    public bool CollapseElapsed(int generation)
    {
        if (IsShutdown) return false;
        if (!IsCollapsePending || generation != CollapseGeneration) return false;

        IsCollapsePending = false;
        if (State == NotchState.Expanded)
        {
            Transition(NotchState.Collapsed, "collapse delay elapsed");
            return true;
        }

        return false;
    }

    /// <summary>Pin/unpin toggle requested by the user (pin button).</summary>
    public void TogglePin()
    {
        if (IsShutdown) return;

        switch (State)
        {
            case NotchState.Expanded:
                CancelPendingCollapse();
                Transition(NotchState.Pinned, "pin toggled on");
                break;
            case NotchState.Pinned:
                Transition(NotchState.Expanded, "pin toggled off");
                if (!IsPointerInside)
                {
                    // The pointer is not over the panel anymore (e.g. tray-unpin),
                    // so fall back to normal hover behavior: close after the delay.
                    ScheduleCollapse(_settings.CollapseDelayMs, "unpinned without pointer");
                }
                break;
        }
    }

    /// <summary>The user clicked somewhere outside the notch while it was open.</summary>
    public void Dismiss()
    {
        if (IsShutdown) return;

        if (State is NotchState.Pinned or NotchState.Expanded or NotchState.EditMode)
        {
            CancelPendingCollapse();
            Transition(NotchState.Collapsed, "clicked outside");
        }
    }

    /// <summary>Programmatic expand (tray menu, second-instance activation, ...).</summary>
    public void RequestExpand()
    {
        if (IsShutdown) return;

        if (State == NotchState.Collapsed)
        {
            Transition(NotchState.Expanded, "expand requested");
            if (!IsPointerInside)
            {
                // Give the user time to move the pointer onto the panel before it
                // closes again; re-entering cancels this schedule like any other.
                ScheduleCollapse(_settings.ProgrammaticExpandLingerMs, "programmatic expand");
            }
        }
    }

    /// <summary>Programmatic collapse (tray menu, shutdown).</summary>
    public void RequestCollapse()
    {
        if (IsShutdown) return;

        if (State != NotchState.Collapsed)
        {
            CancelPendingCollapse();
            Transition(NotchState.Collapsed, "collapse requested");
        }
    }

    /// <summary>Reserved for a future milestone: enter in-place edit mode.</summary>
    public void EnterEditMode()
    {
        if (IsShutdown) return;

        if (State is NotchState.Expanded or NotchState.Pinned)
        {
            CancelPendingCollapse();
            Transition(NotchState.EditMode, "edit mode requested");
        }
    }

    /// <summary>Reserved for a future milestone: leave in-place edit mode.</summary>
    public void ExitEditMode()
    {
        if (IsShutdown) return;

        if (State == NotchState.EditMode)
        {
            Transition(NotchState.Expanded, "edit mode exited");
            if (!IsPointerInside)
            {
                ScheduleCollapse(_settings.CollapseDelayMs, "edit mode exited without pointer");
            }
        }
    }

    /// <summary>
    /// Final, deliberate shutdown of the interaction logic: collapses, cancels
    /// pending work and permanently ignores further input.
    /// </summary>
    public void BeginShutdown()
    {
        if (IsShutdown) return;

        if (State != NotchState.Collapsed)
        {
            Transition(NotchState.Collapsed, "shutdown");
        }

        IsPointerInside = false;
        CancelPendingCollapse();
        IsShutdown = true;
    }

    // ------------------------------------------------------------------ helpers

    private void ScheduleCollapse(int delayMs, string reason)
    {
        IsCollapsePending = true;
        CollapseGeneration++;
        CollapseScheduled?.Invoke(CollapseGeneration, Math.Max(0, delayMs));
    }

    private void CancelPendingCollapse()
    {
        if (!IsCollapsePending) return;

        IsCollapsePending = false;
        CollapseCancelled?.Invoke();
    }

    private void Transition(NotchState to, string reason)
    {
        var from = State;
        if (from == to) return;

        State = to;
        Transitioned?.Invoke(new NotchTransition(from, to, reason, DateTimeOffset.Now));
    }
}
