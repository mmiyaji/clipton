using System.Diagnostics;

namespace Clipton.WinUI;

internal static class AppMemory
{
    public static void TrimWorkingSetSoon()
    {
        _ = Task.Delay(500).ContinueWith(_ => TrimWorkingSet(), TaskScheduler.Default);
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
