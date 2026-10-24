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
            TrySignalExistingInstance();
            return false;
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
