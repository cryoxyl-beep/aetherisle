using GrumphySwarm.Core.Layout;

namespace GrumphySwarm.Tests.Layout;

public class NotchLayoutTests
{
    [Fact]
    public void Window_is_top_centered_on_a_1080p_monitor_at_100_percent()
    {
        var placement = NotchLayout.ComputePlacement(
            monitorX: 0, monitorY: 0, monitorWidth: 1920, monitorHeight: 1080,
            windowWidthDip: NotchDimensions.WindowWidth, windowHeightDip: NotchDimensions.WindowHeight,
            dpiScaleX: 1.0, dpiScaleY: 1.0);

        Assert.Equal(0, placement.Y); // flush with the top edge
        Assert.Equal((1920 - NotchDimensions.WindowWidth) / 2, placement.X);
        Assert.Equal(NotchDimensions.WindowWidth, placement.Width);
        Assert.Equal(NotchDimensions.WindowHeight, placement.Height);
    }

    [Fact]
    public void Scaling_150_percent_converts_dip_to_physical_pixels()
    {
        var placement = NotchLayout.ComputePlacement(
            monitorX: 0, monitorY: 0, monitorWidth: 1920, monitorHeight: 1080,
            windowWidthDip: 800, windowHeightDip: 260,
            dpiScaleX: 1.5, dpiScaleY: 1.5);

        Assert.Equal(1200, placement.Width);  // 800 * 1.5
        Assert.Equal(390, placement.Height);  // 260 * 1.5
        Assert.Equal((1920 - 1200) / 2, placement.X);
        Assert.Equal(0, placement.Y);
    }

    [Fact]
    public void Monitor_with_negative_origin_is_handled()
    {
        // Secondary monitor physically left of the primary: origin at (-1920, 0).
        var placement = NotchLayout.ComputePlacement(
            monitorX: -1920, monitorY: 0, monitorWidth: 1920, monitorHeight: 1080,
            windowWidthDip: 800, windowHeightDip: 260,
            dpiScaleX: 1.0, dpiScaleY: 1.0);

        Assert.Equal(-1920 + (1920 - 800) / 2, placement.X);
        Assert.Equal(0, placement.Y);
    }

    [Fact]
    public void Narrower_than_window_monitor_clamps_to_left_edge()
    {
        var placement = NotchLayout.ComputePlacement(
            monitorX: 0, monitorY: 0, monitorWidth: 500, monitorHeight: 600,
            windowWidthDip: 680, windowHeightDip: 240,
            dpiScaleX: 1.0, dpiScaleY: 1.0);

        Assert.Equal(0, placement.X);
        Assert.Equal(0, placement.Y);
    }

    [Fact]
    public void Panel_geometry_is_consistent_with_pill_geometry()
    {
        // The expanded panel must be at least as large as the pill in both axes,
        // otherwise the grow animation would be inverted.
        Assert.True(NotchDimensions.PanelWidth > NotchDimensions.PillWidth);
        Assert.True(NotchDimensions.PanelHeight > NotchDimensions.PillHeight);

        // The window must have slack around the panel so the shadow is not clipped.
        Assert.True(NotchDimensions.WindowWidth > NotchDimensions.PanelWidth);
        Assert.True(NotchDimensions.WindowHeight > NotchDimensions.PanelHeight);
    }
}
