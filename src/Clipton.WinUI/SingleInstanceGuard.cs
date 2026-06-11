namespace Clipton.WinUI;

internal sealed class SingleInstanceGuard : IDisposable
{
    private const string MutexName = "Clipton.WinUI.SingleInstance";
    private const string ActivationEventName = "Clipton.WinUI.SingleInstance.Activate";
    private readonly Mutex _mutex;
    private readonly EventWaitHandle _activationEvent;
    private RegisteredWaitHandle? _registeredWait;

    private SingleInstanceGuard(Mutex mutex, EventWaitHandle activationEvent)
    {
        _mutex = mutex;
        _activationEvent = activationEvent;
    }

    public static SingleInstanceGuard? TryAcquire()
    {
        var mutex = new Mutex(initiallyOwned: false, MutexName);
        bool acquired;
        try
        {
            acquired = mutex.WaitOne(TimeSpan.Zero);
        }
        catch (AbandonedMutexException)
        {
            // The previous owner crashed; ownership is transferred to us.
            acquired = true;
        }

        if (!acquired)
        {
            mutex.Dispose();
            return null;
        }

        var activationEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivationEventName);
        return new SingleInstanceGuard(mutex, activationEvent);
    }

    public static void NotifyExistingInstance()
    {
        try
        {
            using var activationEvent = EventWaitHandle.OpenExisting(ActivationEventName);
            activationEvent.Set();
        }
        catch (WaitHandleCannotBeOpenedException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    public void WatchActivationRequests(Action onActivated)
    {
        _registeredWait ??= ThreadPool.RegisterWaitForSingleObject(
            _activationEvent,
            (_, _) => onActivated(),
            null,
            Timeout.Infinite,
            executeOnlyOnce: false);
    }

    public void Dispose()
    {
        _registeredWait?.Unregister(null);
        _registeredWait = null;
        _activationEvent.Dispose();
        try
        {
            _mutex.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // Not the owning thread; the OS releases it on process exit.
        }

        _mutex.Dispose();
    }
}
