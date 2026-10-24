namespace GrumphySwarm.Core.Settings;

/// <summary>
/// User-visible settings for the notch host, persisted as JSON.
/// Keep this POCO simple and JSON-friendly; every property needs a sensible default.
/// </summary>
public sealed class NotchSettings
{
    /// <summary>Delay before the expanded panel collapses after the pointer leaves.</summary>
    public int CollapseDelayMs { get; set; } = 350;

    /// <summary>How long a programmatic expand (tray menu / second launch) stays open without pointer hover.</summary>
    public int ProgrammaticExpandLingerMs { get; set; } = 4000;

    /// <summary>How long transient system status (volume/brightness) shows in the pill.</summary>
    public int TransientStatusMs { get; set; } = 2200;

    /// <summary>When true, state changes snap instantly instead of animating (accessibility).</summary>
    public bool ReducedMotion { get; set; }

    /// <summary>Show the clock inside the collapsed pill.</summary>
    public bool ShowClockInPill { get; set; } = true;

    /// <summary>Show the mini-calendar module in the expanded panel.</summary>
    public bool ShowCalendarModule { get; set; } = true;

    /// <summary>User-dragged horizontal offset from top-center, in DIP.</summary>
    public double DragOffsetX { get; set; }

    /// <summary>User-dragged vertical offset from the top edge, in DIP.</summary>
    public double DragOffsetY { get; set; }

    /// <summary>Saved island position (absolute window left/top in DIP); null values fall back to the offset fields.</summary>
    public double? IslandLeftDip { get; set; }

    public double? IslandTopDip { get; set; }
}
