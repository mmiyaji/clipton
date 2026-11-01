using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace Clipton.WinUI;

// Dedicated STA thread for clipboard work. It pumps native messages while idle and
// between queued operations so COM/clipboard callbacks for delayed data can run.
internal sealed class ClipboardCaptureWorker : IDisposable
{
    private readonly ConcurrentQueue<Action> _queue = new();
    private readonly AutoResetEvent _wakeEvent = new(false);
    private readonly object _stateGate = new();
    private readonly Thread _thread;
    private bool _completionRequested;

    public ClipboardCaptureWorker()
    {
        _thread = new Thread(Run)
        {
            IsBackground = true,
            Name = "Clipton clipboard worker"
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        TryEnqueue(action);
    }

    public Task<T?> InvokeAsync<T>(Func<T?> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var completion = new TaskCompletionSource<T?>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!TryEnqueue(() =>
        {
            try
            {
                completion.TrySetResult(action());
            }
            catch (Exception exception)
            {
                AppDiagnostics.Log(exception, "Clipboard capture worker");
                completion.TrySetException(exception);
            }
        }))
        {
            completion.TrySetResult(default);
        }

        return completion.Task;
    }

    private bool TryEnqueue(Action action)
    {
        lock (_stateGate)
        {
            if (_completionRequested)
            {
                return false;
            }

            _queue.Enqueue(action);
            _wakeEvent.Set();
            return true;
        }
    }

    private void Run()
    {
        try
        {
            while (true)
            {
                PumpMessages();
                if (_queue.TryDequeue(out var action))
                {
                    try
                    {
                        action();
                    }
                    catch (Exception exception)
                    {
                        AppDiagnostics.Log(exception, "Clipboard capture worker");
                    }

                    PumpMessages();
                    continue;
                }

                lock (_stateGate)
                {
                    if (_completionRequested && _queue.IsEmpty)
                    {
                        break;
                    }
                }

                WaitForActionOrMessage();
            }
        }
        catch (Exception exception)
        {
            AppDiagnostics.Log(exception, "Clipboard capture worker pump");
            lock (_stateGate)
            {
                _completionRequested = true;
            }

            while (_queue.TryDequeue(out var action))
            {
                try
                {
                    action();
                }
                catch (Exception queuedException)
                {
                    AppDiagnostics.Log(queuedException, "Clipboard capture worker shutdown");
                }
            }
        }
        finally
        {
            lock (_stateGate)
            {
                _completionRequested = true;
            }

            _wakeEvent.Dispose();
        }
    }

    private static void PumpMessages()
    {
        while (NativeMethods.PeekMessage(out var message, IntPtr.Zero, 0, 0, NativeMethods.PmRemove))
        {
            NativeMethods.TranslateMessage(ref message);
            NativeMethods.DispatchMessage(ref message);
        }
    }

    private void WaitForActionOrMessage()
    {
        var handles = new[] { _wakeEvent.SafeWaitHandle.DangerousGetHandle() };
        var result = NativeMethods.MsgWaitForMultipleObjectsEx(
            1, handles, NativeMethods.Infinite, NativeMethods.QsAllInput, NativeMethods.MwmoInputAvailable);
        if (result == NativeMethods.WaitFailed)
        {
            AppDiagnostics.Log(new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()), "Clipboard capture worker wait");
            _wakeEvent.WaitOne(TimeSpan.FromMilliseconds(100));
        }
    }

    public void Dispose()
    {
        lock (_stateGate)
        {
            if (!_completionRequested)
            {
                _completionRequested = true;
                _wakeEvent.Set();
            }
        }

        if (Environment.CurrentManagedThreadId != _thread.ManagedThreadId)
        {
            _thread.Join(TimeSpan.FromSeconds(2));
        }
    }
}
