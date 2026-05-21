using System.Text.Json;

namespace Clipton.Core;

public sealed class JsonSettingsStore
{
    private readonly string _path;
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public JsonSettingsStore(string path)
    {
        _path = path;
    }

    public CliptonSettings Load()
    {
        if (!File.Exists(_path))
        {
            return new CliptonSettings();
        }

        var json = File.ReadAllText(_path);
        var settings = JsonSerializer.Deserialize<CliptonSettings>(json, Options) ?? new CliptonSettings();
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty(nameof(CliptonSettings.HistoryPersistenceConfigured), out _))
        {
            settings.PersistEncryptedHistory = true;
        }

        settings.MaxHistoryItems = Math.Clamp(settings.MaxHistoryItems, 1, 1000);
        settings.Locale = NormalizeLocale(settings.Locale);
        settings.Theme = NormalizeTheme(settings.Theme);
        return settings;
    }

    public void Save(CliptonSettings settings)
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(_path, JsonSerializer.Serialize(settings, Options));
    }

    private static string NormalizeLocale(string? locale)
    {
        if (string.Equals(locale, "system", StringComparison.OrdinalIgnoreCase))
        {
            return "system";
        }

        return string.Equals(locale, "ja", StringComparison.OrdinalIgnoreCase) ? "ja" : "en";
    }

    private static string NormalizeTheme(string? theme)
    {
        if (string.Equals(theme, "system", StringComparison.OrdinalIgnoreCase))
        {
            return "system";
        }

        return string.Equals(theme, "dark", StringComparison.OrdinalIgnoreCase) ? "dark" : "light";
    }
}
