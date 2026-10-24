namespace GrumphySwarm.Core.State;

/// <summary>
/// A single recorded state transition, produced by <see cref="NotchStateMachine"/>.
/// Useful for diagnostics/logging; the reason string explains what triggered it.
/// </summary>
/// <param name="From">State before the transition.</param>
/// <param name="To">State after the transition.</param>
/// <param name="Reason">Short machine-readable reason, e.g. <c>pointer entered the pill</c>.</param>
/// <param name="Timestamp">Local time at which the transition occurred.</param>
public sealed record NotchTransition(NotchState From, NotchState To, string Reason, DateTimeOffset Timestamp);
