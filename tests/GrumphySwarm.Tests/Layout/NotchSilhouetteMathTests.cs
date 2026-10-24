using GrumphySwarm.Core.Layout;

namespace GrumphySwarm.Tests.Layout;

public class NotchSilhouetteMathTests
{
    [Fact]
    public void Collapsed_pill_is_a_capsule_flush_with_the_top_edge()
    {
        var spec = NotchSilhouetteMath.Build(
            NotchDimensions.PillWidth, NotchDimensions.PillHeight, NotchDimensions.PillRadius, NotchDimensions.PillRadius);

        Assert.True(NotchSilhouetteMath.IsValid(spec));

        // Capsule: top and bottom radii equal half the height.
        Assert.Equal(NotchDimensions.PillHeight / 2, spec.TopRadius);
        Assert.Equal(NotchDimensions.PillHeight / 2, spec.BottomRadius);
        Assert.Equal(0, spec.TopRadius + spec.BottomRadius - spec.Height); // fully capsule
        Assert.Equal(NotchDimensions.PillWidth, spec.Width);
    }

    [Fact]
    public void Expanded_panel_uses_larger_symmetric_radii()
    {
        var pill = NotchSilhouetteMath.Build(
            NotchDimensions.PillWidth, NotchDimensions.PillHeight, NotchDimensions.PillRadius, NotchDimensions.PillRadius);
        var panel = NotchSilhouetteMath.Build(
            NotchDimensions.PanelWidth, NotchDimensions.PanelHeight, NotchDimensions.PanelRadius, NotchDimensions.PanelRadius);
        var media = NotchSilhouetteMath.Build(
            NotchDimensions.MediaWidth, NotchDimensions.MediaHeight, NotchDimensions.MediaRadius, NotchDimensions.MediaRadius);

        // All states share the same design language: fully rounded corners.
        Assert.True(panel.TopRadius > pill.TopRadius);
        Assert.True(media.Height > panel.Height);
        Assert.Equal(panel.BottomRadius, panel.TopRadius);
        Assert.Equal(media.BottomRadius, media.TopRadius);

        Assert.True(NotchSilhouetteMath.IsValid(panel));
        Assert.True(NotchSilhouetteMath.IsValid(media));
    }

    [Fact]
    public void Battery_capsule_is_fully_rounded()
    {
        var spec = NotchSilhouetteMath.Build(
            NotchDimensions.BatteryWidth, NotchDimensions.BatteryHeight, NotchDimensions.BatteryRadius, NotchDimensions.BatteryRadius);

        Assert.Equal(NotchDimensions.BatteryHeight / 2, spec.TopRadius);
        Assert.True(NotchSilhouetteMath.IsValid(spec));
    }

    [Fact]
    public void Bottom_radius_is_clamped_so_corners_never_overlap()
    {
        // Bottom radius larger than the remaining vertical space after the top corner.
        var spec = NotchSilhouetteMath.Build(width: 400, height: 30, topRadius: 15, bottomRadius: 40);
        Assert.Equal(15, spec.BottomRadius); // 30 - 15
    }

    [Fact]
    public void Radii_are_clamped_to_half_the_min_side()
    {
        var spec = NotchSilhouetteMath.Build(width: 40, height: 400, topRadius: 500, bottomRadius: 500);
        Assert.Equal(20, spec.TopRadius);
        Assert.Equal(20, spec.BottomRadius);
    }

    [Fact]
    public void Degenerate_inputs_never_produce_invalid_geometry()
    {
        foreach (var (w, h) in new[] { (1, 1), (5, 100), (1000, 2), (30, 30) })
        {
            var spec = NotchSilhouetteMath.Build(w, h, topRadius: 20, bottomRadius: 20);
            Assert.True(NotchSilhouetteMath.IsValid(spec), $"invalid for {w}x{h}: {spec}");
        }
    }
}
