namespace GrumphySwarm.App.Services;

/// <summary>
/// Enforces a single application instance across the user session.
/// The first instance owns a named mutex; any later instance signals a named
/// event (so the running instance can show the notch) and exits.
/// </summary>
public sealed class SingleInstanceGuard : IDisposable
{
    private readonly string _mutexName;
    private readonly string _activateEventName;

    private Mutex? _mutex;
    private EventWaitHandle? _activateEvent;
    private RegisteredWaitHandle? _registration;
    private bool _disposed;

    /// <summary>Raised (on a thread-pool thread) when another instance asks this one to activate.</summary>
    public event Action? ActivationRequested;

    public SingleInstanceGuard(string mutexName, string activateEventName)
    {
        _mutexName = mutexName;
        _activateEventName = activateEventName;
    }

    /// <summary>
    /// True when this process is the first instance. False when another instance
    /// already runs — in that case the other instance has been signalled and the
    /// caller should exit immediately.
    /// </summary>
    public bool TryAcquire()
    {
        _mutex = new Mutex(initiallyOwned: true, _mutexName, out var createdNew);

        if (!createdNew)
        {
            // The name is taken — but by a REAL instance or by a zombie?
            // Zombies (crashed/hung remnants) hold kernel objects while having
            // no window, and they made later launches exit instantly — the
            // "app randomly closes" bug. Only a visible island window counts
            // as a running instance.
            if (HasVisibleIslandWindow())
            {
                TrySignalExistingInstance();
                return false;
            }

            // Zombie: fall through and run anyway; this process becomes the
            // live instance.
        }

        _activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, _activateEventName);
        _registration = ThreadPool.RegisterWaitForSingleObject(
            _activateEvent,
            callBack: (_, _) => ActivationRequested?.Invoke(),
            state: null,
            millisecondsTimeOutInterval: Timeout.Infinite,
            executeOnlyOnce: false);

        return true;
    }

    /// <summary>True when any visible top-level window with our title exists.</summary>
    private static bool HasVisibleIslandWindow()
    {
        var found = false;

        Interop.NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (!Interop.NativeMethods.IsWindowVisible(hwnd))
            {
                return true;
            }

            var text = new string(' ', 64);
            var length = Interop.NativeMethods.GetWindowText(hwnd, text, 64);
            if (length > 0)
            {
                text = text[..length];
                if (text.Contains("Grumphy Swarm Notch", StringComparison.Ordinal))
                {
                    found = true;
                    return false; // stop enumeration
                }
            }

            return true;
        }, IntPtr.Zero);

        return found;
    }

    private void TrySignalExistingInstance()
    {
        try
        {
            if (EventWaitHandle.TryOpenExisting(_activateEventName, out var existing))
            {
                existing.Set();
                existing.Dispose();
            }
        }
        catch
        {
            // The running instance died between mutex check and signal; nothing to do.
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _registration?.Unregister(null);

        try
        {
            _mutex?.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // Mutex not owned by this thread (e.g. never acquired) — fine during shutdown.
        }

        _activateEvent?.Dispose();
        _mutex?.Dispose();
    }
}
