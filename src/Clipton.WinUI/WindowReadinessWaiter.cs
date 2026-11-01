namespace Clipton.WinUI;

internal static class WindowReadinessWaiter
{
    public static async Task WaitAsync(Task loaded, Task closed, Func<bool> isReady, TimeSpan timeout)
    {
        if (isReady())
        {
            return;
        }

        var timeoutTask = Task.Delay(timeout);
        var completed = await Task.WhenAny(loaded, closed, timeoutTask);
        if (completed == closed)
        {
            throw new InvalidOperationException("The main window closed before the snippet editor could open.");
        }

        if (completed == timeoutTask)
        {
            throw new TimeoutException("The main window did not load before the snippet editor could open.");
        }

        if (!isReady())
        {
            throw new InvalidOperationException("The main window loaded without a XamlRoot.");
        }
    }
}
