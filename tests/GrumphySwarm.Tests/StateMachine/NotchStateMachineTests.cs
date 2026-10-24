using GrumphySwarm.Core.Settings;
using GrumphySwarm.Core.State;

namespace GrumphySwarm.Tests.StateMachine;

public class NotchStateMachineTests
{
    private static NotchSettings DefaultSettings() => new()
    {
        CollapseDelayMs = 350,
        ProgrammaticExpandLingerMs = 4000,
    };

    private static NotchStateMachine NewMachine(NotchSettings? settings = null)
        => new(settings ?? DefaultSettings());

    // ------------------------------------------------------------ hover basics

    [Fact]
    public void Hovering_over_collapsed_pill_expands()
    {
        var machine = NewMachine();

        machine.PointerEntered();

        Assert.Equal(NotchState.Expanded, machine.State);
        Assert.True(machine.IsPointerInside);
    }

    [Fact]
    public void Pointer_entering_twice_does_not_duplicate_transitions()
    {
        var machine = NewMachine();
        var transitions = new List<NotchTransition>();
        machine.Transitioned += transitions.Add;

        machine.PointerEntered();
        machine.PointerEntered();

        var expand = transitions.Single(t => t.To == NotchState.Expanded);
        Assert.Equal(NotchState.Collapsed, expand.From);
    }

    [Fact]
    public void Leaving_expanded_schedules_collapse_and_elapse_collapses()
    {
        var machine = NewMachine();
        machine.PointerEntered();

        var scheduled = new List<(int Generation, int DelayMs)>();
        machine.CollapseScheduled += (generation, delay) => scheduled.Add((generation, delay));

        machine.PointerLeft();

        Assert.True(machine.IsCollapsePending);
        var (generation, delay) = Assert.Single(scheduled);
        Assert.Equal(350, delay);

        Assert.True(machine.CollapseElapsed(generation));
        Assert.Equal(NotchState.Collapsed, machine.State);
        Assert.False(machine.IsCollapsePending);
    }

    [Fact]
    public void Re_entering_cancels_pending_collapse()
    {
        var machine = NewMachine();
        machine.PointerEntered();
        machine.PointerLeft();
        var generation = machine.CollapseGeneration;

        machine.PointerEntered();

        Assert.False(machine.IsCollapsePending);
        Assert.False(machine.CollapseElapsed(generation)); // stale tick must be ignored
        Assert.Equal(NotchState.Expanded, machine.State);
    }

    [Fact]
    public void Stale_collapse_generation_is_ignored_current_one_collapses()
    {
        var machine = NewMachine();
        machine.PointerEntered();

        machine.PointerLeft();
        var firstGeneration = machine.CollapseGeneration;

        machine.PointerEntered();
        machine.PointerLeft();
        var secondGeneration = machine.CollapseGeneration;

        Assert.NotEqual(firstGeneration, secondGeneration);

        Assert.False(machine.CollapseElapsed(firstGeneration));
        Assert.Equal(NotchState.Expanded, machine.State);

        Assert.True(machine.CollapseElapsed(secondGeneration));
        Assert.Equal(NotchState.Collapsed, machine.State);
    }

    [Fact]
    public void Duplicate_pointer_left_events_schedule_only_once()
    {
        var machine = NewMachine();
        machine.PointerEntered();

        var scheduled = 0;
        machine.CollapseScheduled += (_, _) => scheduled++;

        machine.PointerLeft();
        machine.PointerLeft(); // spurious duplicate

        Assert.Equal(1, scheduled);
    }

    // ------------------------------------------------------------ pinning

    [Fact]
    public void Pinning_from_expanded_cancels_pending_collapse_and_stays_open()
    {
        var machine = NewMachine();
        machine.PointerEntered();
        machine.PointerLeft();

        machine.TogglePin();

        Assert.Equal(NotchState.Pinned, machine.State);
        Assert.False(machine.IsCollapsePending);

        // Even letting the (cancelled) timer fire must not collapse.
        Assert.False(machine.CollapseElapsed(machine.CollapseGeneration));
        Assert.Equal(NotchState.Pinned, machine.State);
    }

    [Fact]
    public void Unpinning_while_pointer_is_inside_stays_expanded_without_timer()
    {
        var machine = NewMachine();
        machine.PointerEntered();
        machine.TogglePin();
        Assert.True(machine.IsPointerInside);

        machine.TogglePin();

        Assert.Equal(NotchState.Expanded, machine.State);
        Assert.False(machine.IsCollapsePending);
    }

    [Fact]
    public void Unpinning_while_pointer_is_outside_schedules_collapse()
    {
        var machine = NewMachine();
        machine.PointerEntered();
        machine.PointerLeft();
        machine.TogglePin(); // pin while outside
        Assert.False(machine.IsPointerInside);

        machine.TogglePin();

        Assert.Equal(NotchState.Expanded, machine.State);
        Assert.True(machine.IsCollapsePending);
    }

    [Fact]
    public void Clicking_outside_dismisses_pinned_notch()
    {
        var machine = NewMachine();
        machine.PointerEntered();
        machine.TogglePin();

        machine.Dismiss();

        Assert.Equal(NotchState.Collapsed, machine.State);
    }

    [Fact]
    public void Clicking_outside_collapses_expanded_notch_immediately()
    {
        var machine = NewMachine();
        machine.PointerEntered();
        machine.PointerLeft();

        machine.Dismiss();

        Assert.Equal(NotchState.Collapsed, machine.State);
        Assert.False(machine.IsCollapsePending);
    }

    // ------------------------------------------------------------ programmatic

    [Fact]
    public void Tray_expand_without_pointer_uses_linger_delay()
    {
        var machine = NewMachine();
        var scheduled = new List<(int Generation, int DelayMs)>();
        machine.CollapseScheduled += (generation, delay) => scheduled.Add((generation, delay));

        machine.RequestExpand();

        Assert.Equal(NotchState.Expanded, machine.State);
        var (_, delay) = Assert.Single(scheduled);
        Assert.Equal(4000, delay);
    }

    [Fact]
    public void Hovering_onto_programmatically_expanded_panel_cancels_linger()
    {
        var machine = NewMachine();
        machine.RequestExpand();
        Assert.True(machine.IsCollapsePending);

        machine.PointerEntered();

        Assert.False(machine.IsCollapsePending);
        Assert.Equal(NotchState.Expanded, machine.State);
    }

    [Fact]
    public void RequestCollapse_closes_any_open_state()
    {
        foreach (var initial in new[] { NotchState.Expanded, NotchState.Pinned, NotchState.EditMode })
        {
            var machine = NewMachine();
            machine.PointerEntered();
            if (initial == NotchState.Pinned) machine.TogglePin();
            if (initial == NotchState.EditMode) machine.EnterEditMode();

            machine.RequestCollapse();

            Assert.Equal(NotchState.Collapsed, machine.State);
        }
    }

    // ------------------------------------------------------------ edit mode (reserved)

    [Fact]
    public void Edit_mode_is_reserved_but_reachable_and_leavable()
    {
        var machine = NewMachine();
        machine.PointerEntered();
        machine.PointerLeft(); // pending collapse

        machine.EnterEditMode();

        Assert.Equal(NotchState.EditMode, machine.State);
        Assert.False(machine.IsCollapsePending); // edit mode keeps the panel open

        machine.ExitEditMode();

        Assert.Equal(NotchState.Expanded, machine.State);
        Assert.True(machine.IsCollapsePending); // pointer is outside → schedule
    }

    // ------------------------------------------------------------ shutdown

    [Fact]
    public void Shutdown_collapses_and_freezes_the_machine()
    {
        var machine = NewMachine();
        machine.PointerEntered();
        machine.TogglePin();

        machine.BeginShutdown();

        Assert.Equal(NotchState.Collapsed, machine.State);
        Assert.True(machine.IsShutdown);

        machine.PointerEntered();
        machine.TogglePin();
        machine.RequestExpand();
        machine.CollapseElapsed(machine.CollapseGeneration);

        Assert.Equal(NotchState.Collapsed, machine.State);
        Assert.True(machine.IsShutdown);
    }

    // ------------------------------------------------------------ stress

    [Fact]
    public void Rapid_hover_storm_never_breaks_invariants()
    {
        var machine = NewMachine();
        var random = new Random(1234);
        var pendingObserved = 0;

        for (var i = 0; i < 500; i++)
        {
            switch (random.Next(5))
            {
                case 0: machine.PointerEntered(); break;
                case 1: machine.PointerLeft(); break;
                case 2:
                    if (machine.State is NotchState.Expanded or NotchState.Pinned)
                    {
                        machine.TogglePin();
                    }
                    break;
                case 3:
                    machine.CollapseElapsed(machine.CollapseGeneration);
                    break;
                case 4: machine.Dismiss(); break;
            }

            // Invariants after every step.
            Assert.True(Enum.IsDefined(machine.State), $"undefined state {machine.State}");
            Assert.False(
                machine.IsCollapsePending && machine.State is not (NotchState.Expanded),
                $"pending collapse illegal in state {machine.State}");

            if (machine.IsPointerInside && machine.State == NotchState.Expanded)
            {
                Assert.False(machine.IsCollapsePending, "collapse must never be pending while the pointer is inside");
                pendingObserved++;
            }
        }

        Assert.True(pendingObserved > 0); // sanity: the storm actually exercised the invariant
    }

    [Fact]
    public void Full_user_session_sequence_matches_expected_timeline()
    {
        var machine = NewMachine();
        var timeline = new List<NotchTransition>();
        machine.Transitioned += timeline.Add;

        machine.PointerEntered();                     // hover -> expand
        machine.PointerLeft();                        // leave -> schedule
        machine.PointerEntered();                     // come back -> cancel
        machine.PointerLeft();                        // leave again
        machine.CollapseElapsed(machine.CollapseGeneration); // timer fires -> collapse
        machine.RequestExpand();                      // tray expand
        machine.PointerEntered();                     // user moves onto panel
        machine.TogglePin();                          // pin it
        machine.PointerLeft();                        // walk away (stays pinned)
        machine.CollapseElapsed(machine.CollapseGeneration); // no timer is pending
        machine.TogglePin();                          // unpin (pointer outside) -> schedule
        machine.CollapseElapsed(machine.CollapseGeneration); // collapse

        var states = timeline.Select(t => t.To).ToArray();
        Assert.Equal(
        [
            NotchState.Expanded,   // hover
            NotchState.Collapsed,  // collapse delay elapsed
            NotchState.Expanded,   // tray expand
            NotchState.Pinned,     // pin
            NotchState.Expanded,   // unpin (pointer outside → collapse scheduled)
            NotchState.Collapsed,  // collapse delay elapsed
        ], states);
    }
}
