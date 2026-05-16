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

        settings.MaxHistoryItems = Math.Clamp(Math.Max(settings.MaxHistoryItems, 200), 1, 500);
        settings.Locale = string.IsNullOrWhiteSpace(settings.Locale) ? "en" : settings.Locale;
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
}
