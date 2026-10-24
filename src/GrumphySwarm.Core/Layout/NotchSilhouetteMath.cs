namespace GrumphySwarm.Core.Layout;

/// <summary>
/// Normalized silhouette specification for the Dynamic Island capsule: a
/// rounded rectangle with independently clamped top and bottom radii. Produced
/// exclusively by <see cref="NotchSilhouetteMath.Build"/> so every consumer
/// (fill, clip, hit-test) shares the exact same shape.
/// </summary>
/// <param name="Width">Total width in DIP.</param>
/// <param name="Height">Total height in DIP.</param>
/// <param name="TopRadius">Corner radius of both top corners.</param>
/// <param name="BottomRadius">Corner radius of both bottom corners.</param>
public readonly record struct SilhouetteSpec(double Width, double Height, double TopRadius, double BottomRadius);

/// <summary>
/// Pure geometry math for the Dynamic Island silhouette — a capsule-like
/// rounded shape. Radii are clamped so the geometry can never self-intersect,
/// even for degenerate sizes. No WPF dependency; fully unit-testable.
/// </summary>
public static class NotchSilhouetteMath
{
    /// <summary>Cubic-bezier approximation constant for quarter arcs.</summary>
    public const double ArcK = 0.5522847498307936;

    /// <summary>
    /// Builds a validated silhouette spec. Top radius is clamped to half the
    /// height (capsule) and half the width; bottom radius is additionally
    /// clamped so top and bottom corners never overlap vertically.
    /// </summary>
    public static SilhouetteSpec Build(double width, double height, double topRadius, double bottomRadius)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(width);
        ArgumentOutOfRangeException.ThrowIfNegative(height);
        ArgumentOutOfRangeException.ThrowIfNegative(topRadius);
        ArgumentOutOfRangeException.ThrowIfNegative(bottomRadius);

        var halfMinSide = Math.Min(width, height) / 2;
        topRadius = Math.Min(topRadius, halfMinSide);

        var maxBottom = Math.Min(halfMinSide, height - topRadius);
        bottomRadius = Math.Min(bottomRadius, Math.Max(0, maxBottom));

        return new SilhouetteSpec(width, height, topRadius, bottomRadius);
    }

    /// <summary>True when the spec describes a drawable, non-degenerate shape.</summary>
    public static bool IsValid(in SilhouetteSpec spec)
    {
        return spec.Width > 0
            && spec.Height > 0
            && spec.TopRadius <= Math.Min(spec.Width, spec.Height) / 2 + 0.001
            && spec.BottomRadius <= Math.Min(spec.Width, spec.Height) / 2 + 0.001
            && spec.TopRadius + spec.BottomRadius <= spec.Height + 0.001;
    }
}
