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

        _modifierWatchdog = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1),
        };
        _modifierWatchdog.Tick += (_, _) => OnRecoveryWatchdogTick();

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
        InstallKeyboardHook();
        _modifierWatchdog.Start();

        // Any expand request (tray "Show", second-instance activation) also
        // heals a hidden/cloaked window — the reliable recovery path.
        _viewModel.StateChanged += OnStateChangedEnsureVisible;

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

            case NativeMethods.WM_SYSCOMMAND when (wParam.ToInt64() & 0xFFF0) == NativeMethods.SC_MINIMIZE:
                // A minimized borderless taskbar-less window is an INVISIBLE
                // window — apps that toggle fullscreen can minimize topmost
                // windows on their way in. Refuse; the modifier watchdog also
                // re-shows the window if anything else hides it.
                _logger.LogInformation("Minimize request blocked (external window toggle).");
                handled = true;
                return IntPtr.Zero;

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
    ///   - Shift ghost mode is handled by toggling WS_EX_TRANSPARENT on the
    ///     window itself (see the modifier watchdog): HTTRANSPARENT can only
    ///     pass clicks to same-process windows, so it could never make the
    ///     island click-through for other apps.
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

    // --------------------------------------------- modifier gestures (Phase 1)
    //
    // Event-driven: a WH_KEYBOARD_LL hook watches only the modifier keys and
    // schedules a state update when their combination CHANGES (the callback is
    // two integer reads — fast enough that Windows never evicts it). A 1 s
    // recovery watchdog re-syncs the same state in case a key event was ever
    // missed, and heals a window that something external hid or cloaked.

    private readonly System.Windows.Threading.DispatcherTimer _modifierWatchdog;
    private bool _ghostActive;
    private bool _hiddenByCtrlShift;
    private IntPtr _keyboardHook;
    private NativeMethods.LowLevelKeyboardProc? _keyboardProc;
    private int _lastHookShift = -1;
    private int _lastHookCtrl = -1;

    private void InstallKeyboardHook()
    {
        if (_keyboardHook != IntPtr.Zero)
        {
            return;
        }

        _keyboardProc = LowLevelKeyboardProc;
        _keyboardHook = NativeMethods.SetWindowsHookEx(
            NativeMethods.WH_KEYBOARD_LL,
            _keyboardProc,
            NativeMethods.GetModuleHandle(null),
            0);

        if (_keyboardHook == IntPtr.Zero)
        {
            _logger.LogWarning("Failed to install keyboard hook ({Error}); modifier gestures fall back to the 1 s watchdog.", Marshal.GetLastWin32Error());
        }
        else
        {
            _logger.LogInformation("Global keyboard hook active (Shift ghost/drag, Ctrl+Shift hide).");
        }
    }

    private IntPtr LowLevelKeyboardProc(int code, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (code >= 0)
            {
                var message = wParam.ToInt64();
                if (message is NativeMethods.WM_KEYDOWN or NativeMethods.WM_KEYUP
                    or NativeMethods.WM_SYSKEYDOWN or NativeMethods.WM_SYSKEYUP)
                {
                    var data = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
                    if (data.vkCode is NativeMethods.VK_SHIFT or NativeMethods.VK_LSHIFT or NativeMethods.VK_RSHIFT
                        or NativeMethods.VK_CONTROL or NativeMethods.VK_LCONTROL or NativeMethods.VK_RCONTROL)
                    {
                        // Read the authoritative state here (hook thread, O(1))
                        // and only wake the dispatcher when it changed.
                        var shift = NativeMethods.IsShiftHeld() ? 1 : 0;
                        var ctrl = NativeMethods.IsCtrlHeld() ? 1 : 0;
                        if (shift != _lastHookShift || ctrl != _lastHookCtrl)
                        {
                            _lastHookShift = shift;
                            _lastHookCtrl = ctrl;
                            Dispatcher.BeginInvoke(UpdateModifierState);
                        }
                    }
                }
            }
        }
        catch
        {
            // A hook callback must never throw — Windows would evict the hook.
        }

        return NativeMethods.CallNextHookEx(_keyboardHook, code, wParam, lParam);
    }

    /// <summary>
    /// Applies the current modifier combination:
    ///   - Shift only      → true click-through ghost (WS_EX_TRANSPARENT);
    ///                       an expanded island is collapsed first, because
    ///                       ghost mode swallows the MouseLeave that would.
    ///   - Shift + drag    → reposition (handled in the mouse hook).
    ///   - Ctrl + Shift    → hide the island entirely (takes precedence;
    ///                       hiding never starts a drag).
    ///   - neither         → normal behavior.
    /// </summary>
    private void UpdateModifierState()
    {
        try
        {
            var shift = NativeMethods.IsShiftHeld();
            var ctrl = NativeMethods.IsCtrlHeld();

            if (shift && ctrl)
            {
                if (!_hiddenByCtrlShift)
                {
                    _hiddenByCtrlShift = true;
                    NativeMethods.ShowWindow(_hwnd, NativeMethods.SW_HIDE);
                    _logger.LogInformation("Island hidden (Ctrl+Shift held).");
                }

                return;
            }

            if (_hiddenByCtrlShift)
            {
                _hiddenByCtrlShift = false;
                NativeMethods.ShowWindow(_hwnd, NativeMethods.SW_SHOWNOACTIVATE);
                RepositionToMonitor("Ctrl+Shift released");
                _logger.LogInformation("Island shown again (Ctrl+Shift released).");
            }

            var ghostDesired = shift;
            if (ghostDesired != _ghostActive)
            {
                if (ghostDesired && _viewModel.State != NotchState.Collapsed)
                {
                    // Ghost swallows all mouse input: end hover expansion now
                    // so the island cannot stay stuck open.
                    _viewModel.PointerLeft();
                }

                ApplyGhostMode(ghostDesired);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Modifier state update failed (ignored).");
        }
    }

    // --------------------------------------------- recovery watchdog (Phase 4)

    /// <summary>
    /// 1 s safety net: re-syncs modifier state (missed key events) and heals a
    /// window that something external hid, cloaked (fullscreen transitions —
    /// the "island vanished but tray is alive" bug), or moved off-monitor.
    /// It never recreates the window and never touches a deliberately hidden
    /// (Ctrl+Shift) one.
    /// </summary>
    private void OnRecoveryWatchdogTick()
    {
        try
        {
            UpdateModifierState();

            if (_hiddenByCtrlShift || _hwnd == IntPtr.Zero)
            {
                return;
            }

            // OWNERSHIP guard (the "island minimizes with a random app" bug):
            // WPF helper windows can take ownership of the island hwnd; owned
            // windows are group-minimized/group-hidden with their owner, and
            // in that state none of the visible/iconic checks below fire.
            // Ownership is pointless for the island — strip it, always.
            var owner = NativeMethods.GetWindow(_hwnd, NativeMethods.GW_OWNER);
            if (owner != IntPtr.Zero)
            {
                NativeMethods.SetWindowLongPtr(_hwnd, NativeMethods.GWLP_HWNDPARENT, IntPtr.Zero);
                _logger.LogWarning(
                    "Island window was owned by hwnd {Owner} — ownership removed so no app minimize can drag it along.",
                    owner);
                NativeMethods.ShowWindow(_hwnd, NativeMethods.SW_SHOWNOACTIVATE);
                RepositionToMonitor("owner removed");
                return;
            }

            // MINIMIZED island: a minimized borderless window reports
            // IsWindowVisible == TRUE (WS_VISIBLE stays set), so the visible
            // check below misses it — and a no-activate topmost window can be
            // minimized alongside another app's window by the shell.
            if (NativeMethods.IsIconic(_hwnd))
            {
                NativeMethods.ShowWindow(_hwnd, NativeMethods.SW_SHOWNOACTIVATE);
                RepositionToMonitor("recovered from minimized state");
                _logger.LogWarning("Island window was minimized — restored.");
                return;
            }

            if (!NativeMethods.IsWindowVisible(_hwnd))
            {
                NativeMethods.ShowWindow(_hwnd, NativeMethods.SW_SHOWNOACTIVATE);
                RepositionToMonitor("recovered from external hide");
                _logger.LogWarning("Island window was hidden externally — restored.");
                return;
            }

            if (NativeMethods.IsWindowCloaked(_hwnd))
            {
                // DWM cloak: a fullscreen app transition hid the window at the
                // compositor level. A hide/show cycle + re-topmost clears it.
                _logger.LogWarning("Island window is DWM-cloaked — forcing recloak recovery.");
                NativeMethods.ShowWindow(_hwnd, NativeMethods.SW_HIDE);
                NativeMethods.ShowWindow(_hwnd, NativeMethods.SW_SHOWNOACTIVATE);
                NativeMethods.SetWindowPos(
                    _hwnd,
                    NativeMethods.HWND_TOPMOST,
                    0, 0, 0, 0,
                    NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE
                    | NativeMethods.SWP_NOOWNERZORDER);
                return;
            }

            if (NativeMethods.GetWindowRect(_hwnd, out var rect)
                && (rect.Right - rect.Left) > 0
                && (rect.Left > _dragMonitor.Right || rect.Right < _dragMonitor.Left
                    || rect.Top > _dragMonitor.Bottom || rect.Bottom < _dragMonitor.Top))
            {
                RepositionToMonitor("recovered from off-monitor position");
                _logger.LogWarning("Island window was moved off-monitor — restored.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Recovery watchdog tick failed (ignored).");
        }
    }

    private void OnStateChangedEnsureVisible(NotchState state)
    {
        if (state is NotchState.Expanded or NotchState.Pinned
            && _hwnd != IntPtr.Zero
            && !_hiddenByCtrlShift
            && (!NativeMethods.IsWindowVisible(_hwnd) || NativeMethods.IsWindowCloaked(_hwnd)))
        {
            NativeMethods.ShowWindow(_hwnd, NativeMethods.SW_SHOWNOACTIVATE);
            RepositionToMonitor("recovered on expand request");
            _logger.LogInformation("Expand request healed a hidden/cloaked island window.");
        }
    }

    /// <summary>Toggles the real OS-level click-through style.</summary>
    private void ApplyGhostMode(bool enabled)
    {
        if (_hwnd == IntPtr.Zero)
        {
            return;
        }

        var extended = NativeMethods.GetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE);
        var flags = NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_LAYERED;
        extended = enabled ? extended | flags : extended & ~flags;
        NativeMethods.SetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE, extended);
        NativeMethods.SetWindowPos(
            _hwnd,
            IntPtr.Zero,
            0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER
            | NativeMethods.SWP_FRAMECHANGED | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOOWNERZORDER);
        _ghostActive = enabled;
        _logger.LogInformation("Ghost mode {State}.", enabled ? "ON (full click-through)" : "OFF");
    }

    private const int HTCAPTION = 2;

    // --------------------------------------------- global mouse hook (always on)
    //
    // The low-level hook serves four rare, low-frequency gestures:
    //   - Middle button anywhere → toggle the earbud battery capsule.
    //   - Shift + left-drag over the island → MOVE it (the ghost window is
    //     WS_EX_TRANSPARENT, so the hook is the only component that still
    //     sees this input; the callback stays pure math + P/Invoke).
    //   - Shift + wheel over the island → open the media player view.
    //   - Left/right click outside the island while PINNED → dismiss.
    // Ctrl+drag (window NOT in ghost mode) is the OS's own native caption
    // drag (WM_NCHITTEST → HTCAPTION) and never touches this hook.

    // Native-drag bookkeeping: base placement (offset-zero) and DPI scale are
    // cached so the window position can be converted back into drag offsets.
    private NativeMethods.RECT _dragMonitor;
    private double _dragScaleX = 1, _dragScaleY = 1;
    private int _baseX, _baseY, _winW, _winH; // offset-zero placement, physical px
    private bool _nativeDragging;
    private Rect _grabRectScreen; // island rect + margin (for the wheel gesture)

    // Shift-drag state (all physical screen pixels, hook thread only).
    private bool _shiftDragging;
    private int _shiftDragOriginX, _shiftDragOriginY;
    private int _shiftDragBaseX, _shiftDragBaseY;
    private long _lastDragLogTick;

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

                    case NativeMethods.WM_LBUTTONDOWN when NativeMethods.IsShiftHeld()
                        && !_hiddenByCtrlShift
                        && _grabRectScreen.Width > 0
                        && _grabRectScreen.Contains(screenPoint)
                        && NativeMethods.GetWindowRect(_hwnd, out var grabRect):
                        // Shift + press on the island: start a hook-driven drag.
                        // The press is swallowed so the app below never sees it.
                        _shiftDragging = true;
                        _shiftDragOriginX = data.pt.X;
                        _shiftDragOriginY = data.pt.Y;
                        _shiftDragBaseX = grabRect.Left;
                        _shiftDragBaseY = grabRect.Top;
                        _logger.LogInformation(
                            "Shift-drag start at {X},{Y} (window {WX},{WY}, grab {GX},{GY} {GW}x{GH}).",
                            data.pt.X, data.pt.Y, grabRect.Left, grabRect.Top,
                            (int)_grabRectScreen.X, (int)_grabRectScreen.Y,
                            (int)_grabRectScreen.Width, (int)_grabRectScreen.Height);
                        return new IntPtr(1);

                    case NativeMethods.WM_MOUSEMOVE when _shiftDragging:
                        var moved = NativeMethods.SetWindowPos(
                            _hwnd,
                            IntPtr.Zero,
                            _shiftDragBaseX + (data.pt.X - _shiftDragOriginX),
                            _shiftDragBaseY + (data.pt.Y - _shiftDragOriginY),
                            0, 0,
                            NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER
                            | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOOWNERZORDER);
                        if ((System.Environment.TickCount64 / 250) != _lastDragLogTick)
                        {
                            _lastDragLogTick = System.Environment.TickCount64 / 250;
                            _logger.LogInformation(
                                "Shift-drag move to {X},{Y}: SetWindowPos={Ok}.",
                                _shiftDragBaseX + (data.pt.X - _shiftDragOriginX),
                                _shiftDragBaseY + (data.pt.Y - _shiftDragOriginY),
                                moved);
                        }

                        break;

                    case NativeMethods.WM_MOUSEMOVE when NativeMethods.IsShiftHeld():
                        // Diagnostics: moves arriving while Shift is held but no
                        // drag is active (the press was never swallowed).
                        if ((System.Environment.TickCount64 / 250) != _lastDragLogTick)
                        {
                            _lastDragLogTick = System.Environment.TickCount64 / 250;
                            _logger.LogInformation(
                                "Shift held, move at {X},{Y}, dragging={Dragging}, grabW={W}.",
                                data.pt.X, data.pt.Y, _shiftDragging, (int)_grabRectScreen.Width);
                        }

                        break;

                    case NativeMethods.WM_LBUTTONUP when _shiftDragging:
                        _shiftDragging = false;
                        // Persist + clamp on the UI thread (needs the dispatcher).
                        Dispatcher.BeginInvoke(() =>
                        {
                            ClampWindowInsideMonitor();
                            SyncOffsetsFromWindowPosition();
                            _viewModel.SaveDragOffset();
                            RefreshDragCache();
                        });
                        return new IntPtr(1);

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
        _modifierWatchdog.Stop();

        if (_mouseHook != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_mouseHook);
            _mouseHook = IntPtr.Zero;
        }

        if (_keyboardHook != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_keyboardHook);
            _keyboardHook = IntPtr.Zero;
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
