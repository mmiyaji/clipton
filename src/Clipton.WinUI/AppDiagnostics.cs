namespace Clipton.WinUI;

internal static class AppDiagnostics
{
    private const long MaxLogBytes = 1_000_000;
    private const int MaxArchives = 3;
    private static readonly object SyncRoot = new();
    private static bool _verboseEnabled;

    public static string LogDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Clipton",
        "Logs");

    private static string CurrentLogPath => Path.Combine(LogDirectory, "clipton.log");

    public static void Configure(bool verboseEnabled)
    {
        _verboseEnabled = verboseEnabled;
        Info("Diagnostics", verboseEnabled ? "Diagnostic logging enabled." : "Diagnostic logging disabled.");
    }

    public static void Log(Exception exception, string context)
    {
        Write($"{DateTimeOffset.Now:O} [Error:{context}]{Environment.NewLine}{exception}{Environment.NewLine}");
    }

    public static void Info(string context, string message)
    {
        if (!_verboseEnabled)
        {
            return;
        }

        Write($"{DateTimeOffset.Now:O} [Info:{context}] {message}{Environment.NewLine}");
    }

    public static void OpenLogDirectory()
    {
        Directory.CreateDirectory(LogDirectory);
        _ = ExternalLauncher.OpenFolderAsync(LogDirectory);
    }

    public static void ClearLogs()
    {
        lock (SyncRoot)
        {
            if (!Directory.Exists(LogDirectory))
            {
                return;
            }

            foreach (var path in Directory.EnumerateFiles(LogDirectory, "*.log*"))
            {
                try
                {
                    File.Delete(path);
                }
                catch
                {
                    // Diagnostics cleanup must remain best-effort.
                }
            }
        }
    }

    private static void Write(string message)
    {
        try
        {
            lock (SyncRoot)
            {
                Directory.CreateDirectory(LogDirectory);
                RotateIfNeeded();
                File.AppendAllText(CurrentLogPath, message);
            }
        }
        catch
        {
            // Diagnostics must never make the app fail harder.
        }
    }

    private static void RotateIfNeeded()
    {
        if (!File.Exists(CurrentLogPath) || new FileInfo(CurrentLogPath).Length < MaxLogBytes)
        {
            return;
        }

        var oldest = Path.Combine(LogDirectory, $"clipton.{MaxArchives}.log");
        if (File.Exists(oldest))
        {
            File.Delete(oldest);
        }

        for (var index = MaxArchives - 1; index >= 1; index--)
        {
            var source = Path.Combine(LogDirectory, $"clipton.{index}.log");
            var destination = Path.Combine(LogDirectory, $"clipton.{index + 1}.log");
            if (File.Exists(source))
            {
                File.Move(source, destination);
            }
        }

        File.Move(CurrentLogPath, Path.Combine(LogDirectory, "clipton.1.log"));
    }
}
