using System.Collections.Concurrent;

namespace Clipton.WinUI;

// Dedicated STA thread for clipboard reads. The WinRT clipboard API requires an
// STA apartment, and capture can block for seconds when another process holds
// the clipboard, so it must stay off the UI thread.
internal sealed class ClipboardCaptureWorker : IDisposable
{
    private readonly BlockingCollection<Action> _queue = new();
    private readonly Thread _thread;

    public ClipboardCaptureWorker()
    {
        _thread = new Thread(Run)
        {
            IsBackground = true,
            Name = "Clipton clipboard capture"
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    public void Post(Action action)
    {
        if (_queue.IsAddingCompleted)
        {
            return;
        }

        try
        {
            _queue.Add(action);
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void Run()
    {
        foreach (var action in _queue.GetConsumingEnumerable())
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                AppDiagnostics.Log(exception, "Clipboard capture worker");
            }
        }
    }

    public void Dispose()
    {
        _queue.CompleteAdding();
        if (_thread.Join(TimeSpan.FromSeconds(2)))
        {
            _queue.Dispose();
        }
    }
}
