using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using GrumphySwarm.App.Interop;
using GrumphySwarm.App.ViewModels;
using GrumphySwarm.Core.Layout;
using GrumphySwarm.Core.State;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace GrumphySwarm.App.Views;

/// <summary>
/// Native notch host window. Key Win32 behavior:
///
///  - WS_EX_NOACTIVATE: hovering or clicking the notch never steals keyboard
///    focus from the user's foreground app (Topmost alone does NOT give this).
///  - WS_EX_TOOLWINDOW (+ ShowInTaskbar=false): hidden from Alt+Tab.
///  - WM_MOUSEACTIVATE -> MA_NOACTIVATE: belt-and-braces against activation on click.
///  - WM_NCHITTEST -> HTTRANSPARENT outside the island's actual visual bounds,
///    so the invisible window area never blocks clicks meant for apps below.
///  - WM_DPICHANGED / WM_DISPLAYCHANGE / SystemEvents.DisplaySettingsChanged:
///    recompute the pixel-accurate top-center placement (PerMonitorV2 aware).
///  - A low-level mouse hook is installed ONLY while the notch is pinned, to
///    detect "clicked somewhere else" and dismiss the pinned panel.
/// </summary>
public partial class NotchWindow : Window
{
    private readonly NotchViewModel _viewModel;
    private readonly ILogger<NotchWindow> _logger;

    private IntPtr _hwnd;
    private HwndSource? _hwndSource;
    private IntPtr _mouseHook;
    private NativeMethods.LowLevelMouseProc? _mouseProc;

    public NotchWindow(NotchViewModel viewModel, ILogger<NotchWindow> logger)
    {
        _viewModel = viewModel;
        _logger = logger;
        InitializeComponent();

        DataContext = viewModel;

        // The island is one contiguous hit-test region (pill + expanded panel),
        // so moving from the pill into the panel never fires MouseLeave.
        Island.MouseEnter += (_, _) => _viewModel.PointerEntered();
        Island.MouseLeave += (_, _) => _viewModel.PointerLeft();

        Deactivated += (_, _) => _logger.LogInformation(
            "Window deactivated (state {State}); pinned state is preserved intentionally.",
            _viewModel.State);
    }

    public NotchViewModel ViewModel => _viewModel;

    // ------------------------------------------------------------- native setup

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        _hwnd = new WindowInteropHelper(this).Handle;
        _hwndSource = HwndSource.FromHwnd(_hwnd);
        _hwndSource?.AddHook(WndProc);

        ApplyNoActivateWindowStyles();
        RepositionToMonitor("initial placement");
        Island.SizeChanged += (_, _) => RefreshDragCache();
        Dispatcher.BeginInvoke(RefreshDragCache, System.Windows.Threading.DispatcherPriority.Background);
        InstallDismissalAndMiddleClickHook();

        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
    }

    /// <summary>
    /// Applies WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW (Topmost is already set by WPF).
    /// SWP_FRAMECHANGED makes the style change take effect without activation.
    /// </summary>
    private void ApplyNoActivateWindowStyles()
    {
        var extended = NativeMethods.GetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE);
        extended |= NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW;
        NativeMethods.SetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE, extended);

        NativeMethods.SetWindowPos(
            _hwnd,
            IntPtr.Zero,
            0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER
            | NativeMethods.SWP_FRAMECHANGED | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOOWNERZORDER);
    }

    /// <summary>
    /// Places the window top-centered on the primary monitor in physical pixels,
    /// accounting for the current per-monitor DPI scale.
    /// </summary>
    private void RepositionToMonitor(string reason)
    {
        if (_hwnd == IntPtr.Zero) return;

        var monitor = NativeMethods.MonitorFromPoint(
            new NativeMethods.POINT { X = 0, Y = 0 },
            NativeMethods.MONITOR_DEFAULTTOPRIMARY);

        var info = new NativeMethods.MONITORINFO
        {
            cbSize = Marshal.SizeOf<NativeMethods.MONITORINFO>(),
        };

        if (!NativeMethods.GetMonitorInfo(monitor, ref info))
        {
            _logger.LogWarning("GetMonitorInfo failed ({Error}); keeping current placement.", Marshal.GetLastWin32Error());
            return;
        }

        if (PresentationSource.FromVisual(this)?.CompositionTarget is not { } target)
        {
            return; // not yet connected to a presentation source
        }

        var scale = target.TransformToDevice;
        var placement = NotchLayout.ComputePlacement(
            info.rcMonitor.Left,
            info.rcMonitor.Top,
            info.rcMonitor.Right - info.rcMonitor.Left,
            info.rcMonitor.Bottom - info.rcMonitor.Top,
            NotchDimensions.WindowWidth,
            NotchDimensions.WindowHeight,
            scale.M11,
            scale.M22,
            _viewModel.DragOffsetX,
            _viewModel.DragOffsetY);

        NativeMethods.SetWindowPos(
            _hwnd,
            NativeMethods.HWND_TOPMOST,
            placement.X,
            placement.Y,
            placement.Width,
            placement.Height,
            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOOWNERZORDER);

        Dispatcher.BeginInvoke(RefreshDragCache, System.Windows.Threading.DispatcherPriority.Background);

        _logger.LogInformation(
            "Window placed ({Reason}): monitor {Left},{Top} {Width}x{Height}, scale {ScaleX:N2}/{ScaleY:N2}, window {WinX},{WinY} {WinWidth}x{WinHeight} px.",
            reason,
            info.rcMonitor.Left,
            info.rcMonitor.Top,
            info.rcMonitor.Right - info.rcMonitor.Left,
            info.rcMonitor.Bottom - info.rcMonitor.Top,
            scale.M11,
            scale.M22,
            placement.X,
            placement.Y,
            placement.Width,
            placement.Height);
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
        => Dispatcher.BeginInvoke(() => RepositionToMonitor("display settings changed"));

    // ------------------------------------------------------------- window proc

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case NativeMethods.WM_NCHITTEST:
                if (HandleNcHitTest(lParam, out var hitTestResult))
                {
                    handled = true;
                    return hitTestResult;
                }
                break;

            case NativeMethods.WM_MOUSEACTIVATE:
                // Deliver clicks without ever activating the notch window.
                handled = true;
                return new IntPtr(NativeMethods.MA_NOACTIVATE);

            case NativeMethods.WM_DPICHANGED:
            {
                var newDpi = NativeMethods.GetUnsignedLowWord(wParam);
                // Let WPF perform its own per-monitor rescale first, then re-center.
                Dispatcher.BeginInvoke(() => RepositionToMonitor($"DPI changed to {newDpi}"));
                break;
            }

            case NativeMethods.WM_DISPLAYCHANGE:
                Dispatcher.BeginInvoke(() => RepositionToMonitor("display resolution changed"));
                break;

            case NativeMethods.WM_ENTERSIZEMOVE:
                _nativeDragging = true;
                break;

            case NativeMethods.WM_MOVE when _nativeDragging:
                SyncOffsetsFromWindowPosition();
                break;

            case NativeMethods.WM_EXITSIZEMOVE:
                if (_nativeDragging)
                {
                    _nativeDragging = false;
                    ClampWindowInsideMonitor();
                    _viewModel.SaveDragOffset();
                    RefreshDragCache();
                }

                break;

            case NativeMethods.WM_ACTIVATEAPP when wParam != IntPtr.Zero:
                _logger.LogWarning("Notch window got activated despite WS_EX_NOACTIVATE.");
                break;
        }

        return IntPtr.Zero;
    }

    /// <summary>
    /// Hit-test routing:
    ///   - Shift held → the ENTIRE island is click-through (full ghost mode);
    ///     clicks and wheels land on whatever is behind it. The Shift+scroll
    ///     gesture is handled globally in the mouse hook.
    ///   - Ctrl held → the island is a native caption (left-drag moves the
    ///     window through Windows' own drag loop); outside stays transparent.
    ///   - Otherwise → silhouette-exact island hit / transparent elsewhere.
    /// </summary>
    private bool HandleNcHitTest(IntPtr lParam, out IntPtr result)
    {
        result = IntPtr.Zero;

        if (!IsLoaded || Island.ActualWidth <= 0 || Island.ActualHeight <= 0)
        {
            return false;
        }

        try
        {
            var screenX = NativeMethods.GetSignedLowWord(lParam);
            var screenY = NativeMethods.GetSignedHighWord(lParam);

            var windowPoint = PointFromScreen(new Point(screenX, screenY));
            var islandPoint = TranslatePoint(windowPoint, Island);

            if (NativeMethods.IsShiftHeld())
            {
                // Full ghost: everything passes through.
                result = new IntPtr(NativeMethods.HTTRANSPARENT);
                return true;
            }

            var onIsland = Island.HitTestSilhouette(islandPoint);

            if (NativeMethods.IsCtrlHeld())
            {
                result = onIsland
                    ? new IntPtr(HTCAPTION)
                    : new IntPtr(NativeMethods.HTTRANSPARENT);
                return true;
            }

            if (!onIsland)
            {
                result = new IntPtr(NativeMethods.HTTRANSPARENT);
                return true;
            }
        }
        catch (InvalidOperationException)
        {
            // Visual tree not ready for coordinate conversion — use default handling.
        }

        return false;
    }

    private const int HTCAPTION = 2;

    // --------------------------------------------- global mouse hook (always on)
    //
    // The low-level hook now serves only two rare, low-frequency gestures:
    //   - Middle button anywhere → toggle the earbud battery capsule.
    //   - Left/right click outside the island while PINNED → dismiss the panel.
    // Island REPOSITIONING no longer uses the hook at all: Shift+drag is the
    // OS's own native caption drag (WM_NCHITTEST → HTCAPTION), which Windows
    // runs in its internal move loop. Nothing here can be silently evicted.

    // Native-drag bookkeeping: base placement (offset-zero) and DPI scale are
    // cached so the window position can be converted back into drag offsets.
    private NativeMethods.RECT _dragMonitor;
    private double _dragScaleX = 1, _dragScaleY = 1;
    private int _baseX, _baseY, _winW, _winH; // offset-zero placement, physical px
    private bool _nativeDragging;
    private Rect _grabRectScreen; // island rect + margin (for the wheel gesture)

    private void RefreshDragCache()
    {
        if (_hwnd == IntPtr.Zero)
        {
            return;
        }

        try
        {
            if (PresentationSource.FromVisual(this)?.CompositionTarget is not { } target)
            {
                return;
            }

            var scale = target.TransformToDevice;
            _dragScaleX = scale.M11;
            _dragScaleY = scale.M22;

            const uint monitorNearest = 2;
            var monitor = NativeMethods.MonitorFromWindow(_hwnd, monitorNearest);
            var info = new NativeMethods.MONITORINFO { cbSize = Marshal.SizeOf<NativeMethods.MONITORINFO>() };
            if (!NativeMethods.GetMonitorInfo(monitor, ref info))
            {
                return;
            }

            _dragMonitor = info.rcMonitor;

            var placement = NotchLayout.ComputePlacement(
                info.rcMonitor.Left,
                info.rcMonitor.Top,
                info.rcMonitor.Right - info.rcMonitor.Left,
                info.rcMonitor.Bottom - info.rcMonitor.Top,
                NotchDimensions.WindowWidth,
                NotchDimensions.WindowHeight,
                _dragScaleX,
                _dragScaleY);
            _baseX = placement.X;
            _baseY = placement.Y;
            _winW = placement.Width;
            _winH = placement.Height;

            var topLeft = Island.PointToScreen(new Point(0, 0));
            var margin = 40 * _dragScaleX;
            _grabRectScreen = new Rect(
                topLeft.X - margin,
                topLeft.Y - margin,
                (Island.ActualWidth * _dragScaleX) + (2 * margin),
                (Island.ActualHeight * _dragScaleY) + (2 * margin));
        }
        catch (InvalidOperationException)
        {
            // Layout not ready — the next refresh fills the cache.
        }
    }

    private void InstallDismissalAndMiddleClickHook()
    {
        if (_mouseHook != IntPtr.Zero)
        {
            return;
        }

        _mouseProc = LowLevelMouseProc;
        _mouseHook = NativeMethods.SetWindowsHookEx(
            NativeMethods.WH_MOUSE_LL,
            _mouseProc,
            NativeMethods.GetModuleHandle(null),
            0);

        if (_mouseHook == IntPtr.Zero)
        {
            _logger.LogWarning("Failed to install mouse hook ({Error}); shift-drag, middle-click battery and pinned outside-dismiss are disabled.", Marshal.GetLastWin32Error());
        }
        else
        {
            _logger.LogInformation("Global mouse hook active (shift-drag, middle-click battery, pinned outside-dismiss).");
        }
    }

    private IntPtr LowLevelMouseProc(int code, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (code >= 0)
            {
                var message = wParam.ToInt64();
                var data = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
                var screenPoint = new Point(data.pt.X, data.pt.Y);

                switch (message)
                {
                    case NativeMethods.WM_MBUTTONDOWN:
                        // Toggle the battery capsule on middle click, anywhere.
                        Dispatcher.BeginInvoke(() => _viewModel.ToggleBattery());
                        break;

                    case NativeMethods.WM_MOUSEWHEEL when NativeMethods.IsShiftHeld() && _grabRectScreen.Contains(screenPoint):
                        // Shift + scroll over the (fully click-through) island:
                        // open the media player view. The wheel is swallowed so
                        // the app beneath doesn't zoom/scroll.
                        Dispatcher.BeginInvoke(() => _viewModel.OpenMediaPlayer());
                        return new IntPtr(1);

                    case NativeMethods.WM_LBUTTONDOWN or NativeMethods.WM_RBUTTONDOWN:
                        // Never block the hook chain: decide later on the UI thread.
                        Dispatcher.BeginInvoke(() =>
                        {
                            if (_viewModel.State != NotchState.Pinned)
                            {
                                return;
                            }

                            if (!IsPointOverIsland(screenPoint))
                            {
                                _viewModel.DismissFromOutsideClick();
                            }
                        });
                        break;
                }
            }
        }
        catch
        {
            // A hook callback must never throw — Windows would evict the hook
            // (and an escaping exception can take the process down).
        }

        return NativeMethods.CallNextHookEx(_mouseHook, code, wParam, lParam);
    }

    /// <summary>
    /// Converts the live window position (native caption drag) back into the
    /// persisted drag offsets, so display changes and restarts keep the
    /// user-chosen position.
    /// </summary>
    private void SyncOffsetsFromWindowPosition()
    {
        if (!NativeMethods.GetWindowRect(_hwnd, out var rect) || _dragScaleX <= 0)
        {
            return;
        }

        _viewModel.UpdateDragOffset(
            (rect.Left - _baseX) / _dragScaleX,
            (rect.Top - _baseY) / _dragScaleY);
    }

    /// <summary>Keeps at least most of the window inside the monitor after a drag.</summary>
    private void ClampWindowInsideMonitor()
    {
        if (!NativeMethods.GetWindowRect(_hwnd, out var rect))
        {
            return;
        }

        var monitorWidth = _dragMonitor.Right - _dragMonitor.Left;
        var monitorHeight = _dragMonitor.Bottom - _dragMonitor.Top;

        var x = Math.Clamp(rect.Left, _dragMonitor.Left, _dragMonitor.Left + Math.Max(0, monitorWidth - _winW));
        var y = Math.Clamp(rect.Top, _dragMonitor.Top, _dragMonitor.Top + Math.Max(0, monitorHeight - _winH));

        if (x != rect.Left || y != rect.Top)
        {
            NativeMethods.SetWindowPos(
                _hwnd,
                IntPtr.Zero,
                x,
                y,
                0,
                0,
                NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOOWNERZORDER);
            SyncOffsetsFromWindowPosition();
        }
    }

    private bool IsPointOverIsland(Point screenPointPhysical)
    {
        try
        {
            if (PresentationSource.FromVisual(this)?.CompositionTarget is not { } target)
            {
                return true;
            }

            var topLeft = Island.PointToScreen(new Point(0, 0));
            var scale = target.TransformToDevice;
            var bounds = new Rect(
                topLeft.X,
                topLeft.Y,
                Island.ActualWidth * scale.M11,
                Island.ActualHeight * scale.M22);

            return bounds.Contains(screenPointPhysical);
        }
        catch (InvalidOperationException)
        {
            return true; // layout unavailable — never dismiss blindly
        }
    }

    // ------------------------------------------------------------- teardown

    protected override void OnClosed(EventArgs e)
    {
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;

        if (_mouseHook != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_mouseHook);
            _mouseHook = IntPtr.Zero;
        }

        _hwndSource?.RemoveHook(WndProc);

        // Deliberate machine shutdown: collapses and permanently disables input.
        _viewModel.BeginShutdown();

        _logger.LogInformation("Notch window closed.");

        // If the window closed without the tray Exit path (e.g. WM_CLOSE from a
        // graceful taskkill), take the whole app down instead of leaving a
        // headless tray process behind.
        if (Application.Current is { } app && !app.Dispatcher.HasShutdownStarted)
        {
            app.Shutdown(0);
        }

        base.OnClosed(e);
    }
}
