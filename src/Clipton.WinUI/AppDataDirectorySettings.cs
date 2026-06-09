using System.Text.Json;

namespace Clipton.WinUI;

internal static class AppDataDirectorySettings
{
    private const string EnvironmentVariableName = "CLIPTON_DATA_DIR";
    private const string ConfigFileName = "appdata.json";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Clipton");

    public static string ConfigPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Clipton",
        ConfigFileName);

    public static string? Resolve(IReadOnlyList<string> launchArguments)
    {
        if (TryGetArgumentDirectory(launchArguments) is { } argumentDirectory)
        {
            return argumentDirectory;
        }

        var environmentDirectory = Environment.GetEnvironmentVariable(EnvironmentVariableName);
        if (!string.IsNullOrWhiteSpace(environmentDirectory))
        {
            return Normalize(environmentDirectory);
        }

        return LoadConfiguredDirectory();
    }

    public static string? LoadConfiguredDirectory()
    {
        try
        {
            if (!File.Exists(ConfigPath))
            {
                return null;
            }

            var dto = JsonSerializer.Deserialize<AppDataDirectoryDto>(File.ReadAllBytes(ConfigPath));
            return string.IsNullOrWhiteSpace(dto?.DataDirectory) ? null : Normalize(dto.DataDirectory);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            AppDiagnostics.Log(exception, "App data directory config");
            return null;
        }
    }

    public static void SaveConfiguredDirectory(string? path)
    {
        var directory = Path.GetDirectoryName(ConfigPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (string.IsNullOrWhiteSpace(path) || string.Equals(Normalize(path), DefaultDirectory, StringComparison.OrdinalIgnoreCase))
        {
            if (File.Exists(ConfigPath))
            {
                File.Delete(ConfigPath);
            }

            return;
        }

        var normalized = Normalize(path);
        Directory.CreateDirectory(normalized);
        var testPath = Path.Combine(normalized, $".clipton-write-test-{Guid.NewGuid():N}.tmp");
        File.WriteAllText(testPath, "ok");
        File.Delete(testPath);
        File.WriteAllBytes(ConfigPath, JsonSerializer.SerializeToUtf8Bytes(new AppDataDirectoryDto(normalized), JsonOptions));
    }

    private static string? TryGetArgumentDirectory(IReadOnlyList<string> launchArguments)
    {
        var dataDirIndex = -1;
        for (var i = 0; i < launchArguments.Count; i++)
        {
            if (string.Equals(launchArguments[i], "--data-dir", StringComparison.OrdinalIgnoreCase))
            {
                dataDirIndex = i;
                break;
            }
        }

        return dataDirIndex >= 0 && dataDirIndex + 1 < launchArguments.Count
            ? Normalize(launchArguments[dataDirIndex + 1])
            : null;
    }

    private static string Normalize(string path)
    {
        return Path.GetFullPath(Environment.ExpandEnvironmentVariables(path));
    }

    private sealed record AppDataDirectoryDto(string DataDirectory);
}
