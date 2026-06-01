using System.Diagnostics;

namespace Clipton.WinUI;

internal static class AppProfiler
{
    private static readonly long StartTimestamp = Stopwatch.GetTimestamp();

    public static bool Enabled { get; private set; }

    public static void Initialize(IEnumerable<string> args)
    {
        Enabled = args.Any(arg =>
            string.Equals(arg, "--profiler", StringComparison.OrdinalIgnoreCase)
            || string.Equals(arg, "--profile", StringComparison.OrdinalIgnoreCase));

        if (Enabled)
        {
            AppDiagnostics.Configure(verboseEnabled: true);
        }

        Mark("Process started.");
    }

    public static void Mark(string message)
    {
        if (!Enabled)
        {
            return;
        }

        var elapsed = Stopwatch.GetElapsedTime(StartTimestamp);
        using var process = Process.GetCurrentProcess();
        AppDiagnostics.Info(
            "Profiler",
            $"{message} elapsed={elapsed.TotalMilliseconds:F1}ms workingSet={process.WorkingSet64 / 1024 / 1024}MB privateMemory={process.PrivateMemorySize64 / 1024 / 1024}MB gcHeap={GC.GetTotalMemory(forceFullCollection: false) / 1024 / 1024}MB gen0={GC.CollectionCount(0)} gen1={GC.CollectionCount(1)} gen2={GC.CollectionCount(2)}");
    }
}
