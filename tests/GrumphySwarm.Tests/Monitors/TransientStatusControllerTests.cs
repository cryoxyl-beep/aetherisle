using GrumphySwarm.Core.Monitors;

namespace GrumphySwarm.Tests.Monitors;

public class TransientStatusControllerTests
{
    [Fact]
    public void Show_makes_status_visible_and_schedules_expiration()
    {
        var controller = new TransientStatusController();
        var scheduled = new List<(int Generation, int Duration)>();
        controller.ExpirationScheduled += (gen, duration) => scheduled.Add((gen, duration));

        controller.Show(TransientKind.Volume, 2200);

        Assert.True(controller.IsVisible);
        Assert.Equal(TransientKind.Volume, controller.ActiveKind);
        var (generation, duration) = Assert.Single(scheduled);
        Assert.Equal(2200, duration);
        Assert.Equal(generation, controller.Generation);
    }

    [Fact]
    public void Only_the_current_generation_can_expire()
    {
        var controller = new TransientStatusController();
        controller.Show(TransientKind.Volume, 1000);
        var firstGeneration = controller.Generation;

        controller.Show(TransientKind.Volume, 1000); // renewed
        var secondGeneration = controller.Generation;

        Assert.False(controller.Expire(firstGeneration)); // stale timer tick ignored
        Assert.True(controller.IsVisible);

        Assert.True(controller.Expire(secondGeneration));
        Assert.False(controller.IsVisible);
        Assert.Null(controller.ActiveKind);
    }

    [Fact]
    public void Repeated_changes_never_leave_stale_timers()
    {
        var controller = new TransientStatusController();
        var schedules = 0;
        controller.ExpirationScheduled += (_, _) => schedules++;

        for (var i = 0; i < 50; i++)
        {
            controller.Show(i % 2 == 0 ? TransientKind.Volume : TransientKind.Brightness, 500 + i);
        }

        // Each change replaced the previous schedule; only expiring the LAST
        // generation hides the status.
        Assert.Equal(50, schedules);
        for (var i = 1; i < 50; i++)
        {
            Assert.False(controller.Expire(i));
        }

        Assert.True(controller.Expire(50));
        Assert.False(controller.IsVisible);
    }

    [Fact]
    public void Kind_change_raises_shown_again()
    {
        var controller = new TransientStatusController();
        var shown = 0;
        controller.Shown += () => shown++;

        controller.Show(TransientKind.Volume, 500);
        controller.Show(TransientKind.Volume, 500); // renewal: no re-show
        controller.Show(TransientKind.Brightness, 500); // different kind: re-show

        Assert.Equal(2, shown);
    }

    [Fact]
    public void HideNow_supersedes_any_pending_expiration()
    {
        var controller = new TransientStatusController();
        controller.Show(TransientKind.Brightness, 5000);
        var generation = controller.Generation;

        controller.HideNow();

        Assert.False(controller.IsVisible);
        Assert.False(controller.Expire(generation)); // pending tick must be harmless
    }

    [Fact]
    public void Expire_on_hidden_controller_is_inert()
    {
        var controller = new TransientStatusController();

        Assert.False(controller.Expire(1));
        Assert.False(controller.Expire(0));
    }
}
