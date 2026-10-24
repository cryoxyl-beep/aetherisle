namespace GrumphySwarm.Core.State;

/// <summary>
/// Interaction states of the notch.
/// EditMode is reserved for a future milestone (in-place notch customization)
/// and is only reachable through explicit calls today.
/// </summary>
public enum NotchState
{
    /// <summary>Compact pill docked to the top edge of the screen.</summary>
    Collapsed,

    /// <summary>Panel is open because the pointer is over the notch (or was just there).</summary>
    Expanded,

    /// <summary>User pinned the panel; it stays open until unpinned or dismissed.</summary>
    Pinned,

    /// <summary>Reserved for future in-place editing of the notch contents.</summary>
    EditMode,
}
