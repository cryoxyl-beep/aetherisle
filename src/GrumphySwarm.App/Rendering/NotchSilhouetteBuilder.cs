using System.Windows;
using System.Windows.Media;
using GrumphySwarm.Core.Layout;

namespace GrumphySwarm.App.Rendering;

/// <summary>
/// Turns a <see cref="SilhouetteSpec"/> into a frozen StreamGeometry for the
/// Dynamic Island capsule: rounded top corners, straight sides, rounded bottom
/// corners. The result is cached, so repeated paints and hit tests never
/// re-allocate, and the same instance is shared by fill, clip and hit-testing.
/// </summary>
public sealed class NotchSilhouetteBuilder
{
    private StreamGeometry? _cached;
    private SilhouetteSpec _cachedSpec;

    /// <summary>Current cached geometry (null before the first build).</summary>
    public StreamGeometry? Geometry => _cached;

    private static Point P((double X, double Y) point) => new(point.X, point.Y);

    /// <summary>
    /// Returns the capsule geometry for the given spec, rebuilding only when
    /// the spec actually changed.
    /// </summary>
    public StreamGeometry GetGeometry(SilhouetteSpec spec)
    {
        if (_cached is not null && _cachedSpec == spec)
        {
            return _cached;
        }

        var k = NotchSilhouetteMath.ArcK;
        var w = spec.Width;
        var h = spec.Height;
        var t = spec.TopRadius;
        var b = spec.BottomRadius;

        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            // Start just right of the top-left corner, on the top edge.
            context.BeginFigure(new Point(t, 0), isFilled: true, isClosed: true);

            // Top edge.
            context.LineTo(new Point(w - t, 0), isStroked: true, isSmoothJoin: false);

            // Top-right corner.
            context.BezierTo(
                new Point(w - t + k * t, 0),
                new Point(w, t - k * t),
                new Point(w, t),
                isStroked: true, isSmoothJoin: false);

            // Right side.
            context.LineTo(new Point(w, h - b), isStroked: true, isSmoothJoin: false);

            // Bottom-right corner.
            context.BezierTo(
                new Point(w, h - b + k * b),
                new Point(w - b + k * b, h),
                new Point(w - b, h),
                isStroked: true, isSmoothJoin: false);

            // Bottom edge.
            context.LineTo(new Point(b, h), isStroked: true, isSmoothJoin: false);

            // Bottom-left corner.
            context.BezierTo(
                new Point(b - k * b, h),
                new Point(0, h - b + k * b),
                new Point(0, h - b),
                isStroked: true, isSmoothJoin: false);

            // Left side.
            context.LineTo(new Point(0, t), isStroked: true, isSmoothJoin: false);

            // Top-left corner back to the start.
            context.BezierTo(
                new Point(0, t - k * t),
                new Point(t - k * t, 0),
                new Point(t, 0),
                isStroked: true, isSmoothJoin: false);
        }

        geometry.Freeze();

        _cached = geometry;
        _cachedSpec = spec;
        return geometry;
    }

    /// <summary>Precise point-in-silhouette test (used by WM_NCHITTEST).</summary>
    public bool Contains(StreamGeometry geometry, Point point)
    {
        return geometry.FillContains(point, 0.5, ToleranceType.Absolute);
    }
}
