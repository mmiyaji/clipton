namespace Clipton.WinUI;

internal static class AppDiagnostics
{
    private static readonly object SyncRoot = new();

    public static void Log(Exception exception, string context)
    {
        try
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Clipton",
                "Logs");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "clipton.log");
            var message = $"{DateTimeOffset.Now:O} [{context}]{Environment.NewLine}{exception}{Environment.NewLine}";
            lock (SyncRoot)
            {
                File.AppendAllText(path, message);
            }
        }
        catch
        {
            // Diagnostics must never make the app fail harder.
        }
    }
}
