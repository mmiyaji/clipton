namespace Clipton.WinUI;

/// <summary>
/// Periodically asks the Store whether a newer package exists and remembers the answer so the
/// settings window can show it immediately, even while the app only lives in the tray.
/// </summary>
internal sealed class StoreUpdateMonitor : IDisposable
{
    internal static readonly TimeSpan DefaultInitialDelay = TimeSpan.FromSeconds(45);
    internal static readonly TimeSpan DefaultInterval = TimeSpan.FromHours(12);

    private readonly Func<bool> _isEnabled;
    private readonly Func<CancellationToken, Task<bool>> _checkAsync;
    private readonly TimeSpan _initialDelay;
    private readonly TimeSpan _interval;
    private readonly CancellationTokenSource _cancellation = new();
    private int _started;
    private int _checking;
    private volatile bool _updateAvailable;
    private long _lastCheckedAtTicks;

    public StoreUpdateMonitor(
        Func<bool> isEnabled,
        Func<CancellationToken, Task<bool>> checkAsync,
        TimeSpan? initialDelay = null,
        TimeSpan? interval = null)
    {
        _isEnabled = isEnabled;
        _checkAsync = checkAsync;
        _initialDelay = initialDelay ?? DefaultInitialDelay;
        _interval = interval ?? DefaultInterval;
    }

    public event EventHandler? AvailabilityChanged;

    public bool UpdateAvailable => _updateAvailable;

    public DateTimeOffset? LastCheckedAt
    {
        get
        {
            var ticks = Interlocked.Read(ref _lastCheckedAtTicks);
            return ticks == 0 ? null : new DateTimeOffset(ticks, TimeSpan.Zero).ToLocalTime();
        }
    }

    public void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            return;
        }

        _ = RunAsync(_cancellation.Token);
    }

    public async Task CheckNowAsync()
    {
        if (!_isEnabled() || Interlocked.Exchange(ref _checking, 1) != 0)
        {
            return;
        }

        try
        {
            Report(await _checkAsync(_cancellation.Token));
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch (Exception exception)
        {
            AppDiagnostics.Log(exception, "Store update background check");
        }
        finally
        {
            Interlocked.Exchange(ref _checking, 0);
        }
    }

    public void Report(bool updateAvailable)
    {
        Interlocked.Exchange(ref _lastCheckedAtTicks, DateTimeOffset.UtcNow.UtcTicks);
        if (_updateAvailable == updateAvailable)
        {
            return;
        }

        _updateAvailable = updateAvailable;
        AvailabilityChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        _cancellation.Cancel();
        _cancellation.Dispose();
    }

    private async Task RunAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(_initialDelay, token);
            while (!token.IsCancellationRequested)
            {
                await CheckNowAsync();
                await Task.Delay(_interval, token);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }
}
