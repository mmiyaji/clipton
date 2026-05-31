using System.Diagnostics;

namespace Clipton.WinUI;

internal static class AppMemory
{
    private static int _trimScheduled;

    public static void TrimWorkingSetSoon()
    {
        if (Interlocked.Exchange(ref _trimScheduled, 1) == 1)
        {
            return;
        }

        _ = TrimWorkingSetSoonAsync();
    }

    private static async Task TrimWorkingSetSoonAsync()
    {
        try
        {
            await Task.Delay(500).ConfigureAwait(false);
            TrimWorkingSet();
        }
        finally
        {
            Interlocked.Exchange(ref _trimScheduled, 0);
        }
    }

    private static void TrimWorkingSet()
    {
        try
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            using var process = Process.GetCurrentProcess();
            NativeMethods.EmptyWorkingSet(process.Handle);
            AppDiagnostics.Info("Memory", "Trimmed process working set.");
        }
        catch
        {
            // Memory trimming is opportunistic; it must never affect app behavior.
        }
    }
}
