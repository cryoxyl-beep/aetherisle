namespace GrumphySwarm.Core.Layout;

/// <summary>All notch dimensions, in device-independent pixels (DIP, 1/96 inch).</summary>
public static class NotchDimensions
{
    /// <summary>Width of the collapsed Dynamic Island pill.</summary>
    public const double PillWidth = 264;

    /// <summary>Height of the collapsed pill.</summary>
    public const double PillHeight = 40;

    /// <summary>Corner radius of the collapsed pill (capsule).</summary>
    public const double PillRadius = 20;

    /// <summary>Width of the expanded panel (Nook / Agents / Stats).</summary>
    public const double PanelWidth = 760;

    /// <summary>Height of the expanded panel (tallest tab content).</summary>
    public const double PanelHeight = 230;

    /// <summary>Corner radius of the expanded panel.</summary>
    public const double PanelRadius = 36;

    /// <summary>Width of the media player expanded view (reference layout).</summary>
    public const double MediaWidth = 620;

    /// <summary>Height of the media player expanded view.</summary>
    public const double MediaHeight = 200;

    /// <summary>Corner radius of the media player view.</summary>
    public const double MediaRadius = 36;

    /// <summary>Width of the earbud battery pill.</summary>
    public const double BatteryWidth = 340;

    /// <summary>Height of the earbud battery pill.</summary>
    public const double BatteryHeight = 84;

    /// <summary>Corner radius of the battery pill (capsule).</summary>
    public const double BatteryRadius = 42;

    /// <summary>Host window width: widest island plus shadow slack.</summary>
    public const double WindowWidth = 800;

    /// <summary>Host window height: tallest island plus shadow slack.</summary>
    public const double WindowHeight = 260;
}

/// <summary>A window placement in physical screen pixels.</summary>
/// <param name="X">Left edge in virtual-screen coordinates.</param>
/// <param name="Y">Top edge in virtual-screen coordinates.</param>
/// <param name="Width">Width in physical pixels.</param>
/// <param name="Height">Height in physical pixels.</param>
public readonly record struct NotchPlacement(int X, int Y, int Width, int Height);

/// <summary>
/// Pure geometry helpers for positioning the notch host window.
/// Inputs are physical monitor pixels and the current DPI scale; output is the
/// window rectangle in physical pixels. No Win32 calls live here, so the math is
/// unit-testable.
/// </summary>
public static class NotchLayout
{
    /// <summary>
    /// Computes the window rectangle on the given monitor: horizontally centered
    /// plus a user drag offset, clamped so the window always stays fully inside
    /// the monitor. Offsets are in DIP and scale with the monitor's DPI.
    /// </summary>
    public static NotchPlacement ComputePlacement(
        int monitorX,
        int monitorY,
        int monitorWidth,
        int monitorHeight,
        double windowWidthDip,
        double windowHeightDip,
        double dpiScaleX,
        double dpiScaleY,
        double dragOffsetXDip = 0,
        double dragOffsetYDip = 0)
    {
        var width = Math.Max(1, (int)Math.Round(windowWidthDip * dpiScaleX, MidpointRounding.AwayFromZero));
        var height = Math.Max(1, (int)Math.Round(windowHeightDip * dpiScaleY, MidpointRounding.AwayFromZero));

        var offsetX = (int)Math.Round(dragOffsetXDip * dpiScaleX, MidpointRounding.AwayFromZero);
        var offsetY = (int)Math.Round(dragOffsetYDip * dpiScaleY, MidpointRounding.AwayFromZero);

        var x = monitorX + (monitorWidth - width) / 2 + offsetX;
        var y = monitorY + offsetY;

        // Clamp so the window can never leave the monitor.
        var maxX = monitorX + Math.Max(0, monitorWidth - width);
        var maxY = monitorY + Math.Max(0, monitorHeight - height);
        x = Math.Clamp(x, monitorX, maxX);
        y = Math.Clamp(y, monitorY, maxY);

        return new NotchPlacement(x, y, width, height);
    }
}
