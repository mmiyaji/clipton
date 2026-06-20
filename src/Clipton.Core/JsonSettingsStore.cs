using System.Text.Json;

namespace Clipton.Core;

/// <summary>
/// Reads and writes Clipton settings JSON with compatibility normalization.
/// </summary>
/// <remarks>
/// Deserialization stays forgiving: malformed or older files fall back to defaults, and
/// unsupported option values are normalized before the runtime observes them.
/// </remarks>
public sealed class JsonSettingsStore
{
    private readonly string _path;
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public JsonSettingsStore(string path)
    {
        _path = path;
    }

    /// <summary>
    /// Loads settings from disk and normalizes missing, legacy or out-of-range values.
    /// </summary>
    public CliptonSettings Load()
    {
        if (!File.Exists(_path))
        {
            return new CliptonSettings();
        }

        var json = File.ReadAllText(_path);
        CliptonSettings settings;
        bool historyPersistenceConfigured;
        bool maskRuleDefinitionsConfigured;
        try
        {
            settings = JsonSerializer.Deserialize<CliptonSettings>(json, Options) ?? new CliptonSettings();
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return new CliptonSettings();
            }

            historyPersistenceConfigured = document.RootElement.TryGetProperty(nameof(CliptonSettings.HistoryPersistenceConfigured), out _);
            maskRuleDefinitionsConfigured = document.RootElement.TryGetProperty(nameof(CliptonSettings.MaskRuleDefinitions), out _);
        }
        catch (JsonException)
        {
            return new CliptonSettings();
        }

        // Older settings files did not have an explicit history-persistence choice.
        // Preserve the historical default of encrypted local history instead of treating
        // the missing boolean as an opt-out.
        if (!historyPersistenceConfigured)
        {
            settings.PersistEncryptedHistory = true;
        }

        settings.MaxHistoryItems = Math.Clamp(settings.MaxHistoryItems, 1, 1000);
        settings.ClipboardCaptureDelayMilliseconds = NormalizeClipboardCaptureDelay(settings.ClipboardCaptureDelayMilliseconds);
        settings.PinnedHistoryIds = settings.PinnedHistoryIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        settings.Locale = NormalizeLocale(settings.Locale);
        settings.Theme = NormalizeTheme(settings.Theme);
        settings.QuickMenuDisplayMode = NormalizeQuickMenuDisplayMode(settings.QuickMenuDisplayMode);
        settings.QuickMenuImagePreviewSize = NormalizeQuickMenuImagePreviewSize(settings.QuickMenuImagePreviewSize);
        settings.QuickMenuTopLevelHistoryItems = QuickMenuHistoryBuckets.NormalizeTopLevelHistoryItems(settings.QuickMenuTopLevelHistoryItems);
        NormalizeMaskRuleSettings(settings, maskRuleDefinitionsConfigured);
        NormalizeQuickMenuShortcuts(settings);
        NormalizeQuickMenuPasteOptions(settings);
        return settings;
    }

    /// <summary>
    /// Normalizes and writes settings to disk, creating the containing directory if needed.
    /// </summary>
    public void Save(CliptonSettings settings)
    {
        settings.QuickMenuDisplayMode = NormalizeQuickMenuDisplayMode(settings.QuickMenuDisplayMode);
        NormalizeMaskRuleSettings(settings, preferConfiguredDefinitions: true);
        NormalizeQuickMenuPasteOptions(settings);
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(_path, JsonSerializer.Serialize(settings, Options));
    }

    private static void NormalizeMaskRuleSettings(CliptonSettings settings, bool preferConfiguredDefinitions)
    {
        settings.MaskRules ??= new MaskRuleSettings();
        var customPatternEnabled = settings.MaskRules.CustomPattern;
        var definitionsLookDefault = DefinitionsMatchDefaults(settings.MaskRuleDefinitions);
        // If a file only contains generated defaults, keep deriving definitions from the
        // legacy switch model so older toggles remain authoritative. Once the user edits
        // definitions, the definition array becomes the source of truth.
        settings.MaskRuleDefinitions = preferConfiguredDefinitions && !definitionsLookDefault
            ? MaskRuleDefinitionDefaults.Normalize(settings.MaskRuleDefinitions, settings.MaskRules)
            : MaskRuleDefinitionDefaults.CreateDefaultRules(settings.MaskRules);
        settings.MaskRules = MaskRuleDefinitionDefaults.ToSettings(settings.MaskRuleDefinitions);
        settings.MaskRules.CustomPattern = customPatternEnabled;
    }

    private static bool DefinitionsMatchDefaults(IEnumerable<MaskRuleDefinition>? definitions)
    {
        var normalized = MaskRuleDefinitionDefaults.Normalize(definitions);
        var defaults = MaskRuleDefinitionDefaults.CreateDefaultRules();
        return normalized.Length == defaults.Length
            && normalized.Zip(defaults).All(pair =>
                string.Equals(pair.First.Id, pair.Second.Id, StringComparison.Ordinal)
                && pair.First.Enabled == pair.Second.Enabled
                && string.Equals(pair.First.Pattern, pair.Second.Pattern, StringComparison.Ordinal));
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

    private static string NormalizeQuickMenuImagePreviewSize(string? value)
    {
        return value?.ToLowerInvariant() switch
        {
            "none" or "small" or "large" => value.ToLowerInvariant(),
            _ => "medium"
        };
    }

    private static string NormalizeQuickMenuDisplayMode(string? value)
    {
        return string.Equals(value, "rich", StringComparison.OrdinalIgnoreCase) ? "rich" : "default";
    }

    private static int NormalizeClipboardCaptureDelay(int value)
    {
        return value is 0 or 50 or 100 or 150 or 250 or 500 or 1000
            ? value
            : 150;
    }

    private static void NormalizeQuickMenuShortcuts(CliptonSettings settings)
    {
        settings.QuickMenuShortcuts ??= new QuickMenuShortcutSettings();

        var shortcuts = settings.QuickMenuShortcuts;
        shortcuts.Search = NormalizeShortcut(
            shortcuts.Search,
            QuickMenuShortcutSettings.DefaultSearch,
            ["Ctrl+S", "Ctrl+F", "S", "F"]);
        shortcuts.PastePlainText = NormalizeShortcut(
            shortcuts.PastePlainText,
            QuickMenuShortcutSettings.DefaultPastePlainText,
            ["T", "P", "Ctrl+T", "Ctrl+P"]);
        shortcuts.ToggleMaskReveal = NormalizeShortcut(
            shortcuts.ToggleMaskReveal,
            QuickMenuShortcutSettings.DefaultToggleMaskReveal,
            ["Ctrl+M"]);
        shortcuts.ToggleCapturedAt = NormalizeShortcut(
            shortcuts.ToggleCapturedAt,
            QuickMenuShortcutSettings.DefaultToggleCapturedAt,
            ["Ctrl+D", "D"]);

        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        shortcuts.Search = UniqueShortcut(shortcuts.Search, QuickMenuShortcutSettings.DefaultSearch, used);
        shortcuts.PastePlainText = UniqueShortcut(shortcuts.PastePlainText, QuickMenuShortcutSettings.DefaultPastePlainText, used);
        shortcuts.ToggleMaskReveal = UniqueShortcut(shortcuts.ToggleMaskReveal, QuickMenuShortcutSettings.DefaultToggleMaskReveal, used);
        shortcuts.ToggleCapturedAt = UniqueShortcut(shortcuts.ToggleCapturedAt, QuickMenuShortcutSettings.DefaultToggleCapturedAt, used);
    }

    private static void NormalizeQuickMenuPasteOptions(CliptonSettings settings)
    {
        settings.QuickMenuPasteOptions ??= new QuickMenuPasteOptionSettings();
        settings.QuickMenuPasteOptions.DisabledOptionIds =
            QuickMenuPasteOptionSettings.NormalizeDisabledOptionIds(settings.QuickMenuPasteOptions.DisabledOptionIds);
    }

    private static string NormalizeShortcut(string? value, string fallback, IReadOnlyCollection<string> allowed)
    {
        var normalized = NormalizeShortcutText(value);
        return allowed.Contains(normalized, StringComparer.OrdinalIgnoreCase)
            ? allowed.First(item => string.Equals(item, normalized, StringComparison.OrdinalIgnoreCase))
            : fallback;
    }

    private static string NormalizeShortcutText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var parts = value.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return string.Empty;
        }

        if (parts.Take(parts.Length - 1).Any(part => !part.Equals("Ctrl", StringComparison.OrdinalIgnoreCase)
            && !part.Equals("Control", StringComparison.OrdinalIgnoreCase)))
        {
            return string.Empty;
        }

        var control = parts.Take(parts.Length - 1).Any(part => part.Equals("Ctrl", StringComparison.OrdinalIgnoreCase)
            || part.Equals("Control", StringComparison.OrdinalIgnoreCase));
        var key = parts[^1].ToUpperInvariant();
        return control ? $"Ctrl+{key}" : key;
    }

    private static string UniqueShortcut(string shortcut, string fallback, HashSet<string> used)
    {
        if (used.Add(shortcut))
        {
            return shortcut;
        }

        if (used.Add(fallback))
        {
            return fallback;
        }

        return shortcut;
    }

}
