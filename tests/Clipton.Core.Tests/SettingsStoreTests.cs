using Clipton.Core;

namespace Clipton.Core.Tests;

public sealed class SettingsStoreTests
{
    [Fact]
    public void Load_DefaultSettings_EnableEncryptedHistory()
    {
        var path = Path.Combine(Path.GetTempPath(), "clipton-tests", Guid.NewGuid().ToString("N"), "settings.json");
        var store = new JsonSettingsStore(path);

        var loaded = store.Load();

        Assert.True(loaded.PersistEncryptedHistory);
        Assert.Equal("Ctrl+Alt+V", loaded.Hotkey);
        Assert.Equal(200, loaded.MaxHistoryItems);
        Assert.Equal("default", loaded.QuickMenuDisplayMode);
        Assert.Equal("medium", loaded.QuickMenuImagePreviewSize);
        Assert.Equal("Ctrl+F", loaded.QuickMenuShortcuts.Search);
        Assert.Equal("T", loaded.QuickMenuShortcuts.PastePlainText);
        Assert.Equal("Ctrl+M", loaded.QuickMenuShortcuts.ToggleMaskReveal);
        Assert.Equal("Ctrl+D", loaded.QuickMenuShortcuts.ToggleCapturedAt);
        Assert.False(loaded.QuickMenuShowCapturedAt);
        Assert.True(loaded.QuickMenuShowShortcutHints);
        Assert.Equal(150, loaded.ClipboardCaptureDelayMilliseconds);
        Assert.False(loaded.DiagnosticLoggingEnabled);
        Assert.True(loaded.FolderMode);
        Assert.Equal(5, loaded.QuickMenuTopLevelHistoryItems);
        Assert.True(loaded.HideSettingsWindowOnStartup);
        Assert.False(loaded.InitialLaunchCompleted);
        Assert.Equal("system", loaded.Locale);
        Assert.True(loaded.MaskSensitiveContent);
        Assert.True(loaded.MaskRules.ShortAlphanumericCode);
        Assert.Contains(loaded.MaskRuleDefinitions, rule => rule.Id == MaskRuleIds.ShortAlphanumericCode);
        Assert.Equal("system", loaded.Theme);
    }

    [Fact]
    public void Load_MigratesUnconfiguredEncryptedHistorySettingToEnabled()
    {
        var path = Path.Combine(Path.GetTempPath(), "clipton-tests", Guid.NewGuid().ToString("N"), "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """{"Hotkey":"Ctrl+Shift+V","PersistEncryptedHistory":false,"MaxHistoryItems":30,"Locale":"en"}""");
        var store = new JsonSettingsStore(path);

        var loaded = store.Load();

        Assert.True(loaded.PersistEncryptedHistory);
    }

    [Fact]
    public void Load_ReturnsDefaultsWhenSettingsJsonIsMalformed()
    {
        var path = Path.Combine(Path.GetTempPath(), "clipton-tests", Guid.NewGuid().ToString("N"), "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{ broken json");
        var store = new JsonSettingsStore(path);

        var loaded = store.Load();

        Assert.Equal(200, loaded.MaxHistoryItems);
        Assert.Equal("system", loaded.Locale);
        Assert.Equal("system", loaded.Theme);
    }

    [Fact]
    public void Load_PreservesExplicitEncryptedHistoryOptOut()
    {
        var path = Path.Combine(Path.GetTempPath(), "clipton-tests", Guid.NewGuid().ToString("N"), "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """{"PersistEncryptedHistory":false,"HistoryPersistenceConfigured":true,"MaxHistoryItems":30,"Locale":"en"}""");
        var store = new JsonSettingsStore(path);

        var loaded = store.Load();

        Assert.False(loaded.PersistEncryptedHistory);
    }

    [Fact]
    public void SaveAndLoad_RoundTripsSettings()
    {
        var path = Path.Combine(Path.GetTempPath(), "clipton-tests", Guid.NewGuid().ToString("N"), "settings.json");
        var store = new JsonSettingsStore(path);

        store.Save(new CliptonSettings
        {
            Hotkey = "Ctrl+Alt+V",
            ClipboardCaptureDelayMilliseconds = 250,
            DiagnosticLoggingEnabled = true,
            FolderMode = true,
            HistoryPersistenceConfigured = true,
            HideSettingsWindowOnStartup = false,
            InitialLaunchCompleted = true,
            Locale = "ja",
            MaxHistoryItems = 42,
            PauseCapture = true,
            PastePlainTextByDefault = true,
            PersistEncryptedHistory = true,
            QuickMenuDisplayMode = "rich",
            QuickMenuImagePreviewSize = "large",
            QuickMenuTopLevelHistoryItems = 30,
            QuickMenuShowCapturedAt = true,
            QuickMenuShowShortcutHints = false,
            QuickMenuShortcuts = new QuickMenuShortcutSettings
            {
                Search = "Ctrl+F",
                PastePlainText = "Ctrl+P",
                ToggleMaskReveal = "Ctrl+M",
                ToggleCapturedAt = "D"
            },
            StartWithWindows = true,
            MaskRules = new MaskRuleSettings
            {
                Email = false,
                ShortAlphanumericCode = false
            },
            Theme = "dark"
        });

        var loaded = store.Load();

        Assert.Equal("Ctrl+Alt+V", loaded.Hotkey);
        Assert.Equal(250, loaded.ClipboardCaptureDelayMilliseconds);
        Assert.True(loaded.DiagnosticLoggingEnabled);
        Assert.True(loaded.FolderMode);
        Assert.Equal("ja", loaded.Locale);
        Assert.Equal("dark", loaded.Theme);
        Assert.True(loaded.HistoryPersistenceConfigured);
        Assert.False(loaded.HideSettingsWindowOnStartup);
        Assert.True(loaded.InitialLaunchCompleted);
        Assert.Equal(42, loaded.MaxHistoryItems);
        Assert.True(loaded.PauseCapture);
        Assert.True(loaded.PastePlainTextByDefault);
        Assert.True(loaded.PersistEncryptedHistory);
        Assert.Equal("rich", loaded.QuickMenuDisplayMode);
        Assert.Equal("large", loaded.QuickMenuImagePreviewSize);
        Assert.Equal(30, loaded.QuickMenuTopLevelHistoryItems);
        Assert.True(loaded.QuickMenuShowCapturedAt);
        Assert.False(loaded.QuickMenuShowShortcutHints);
        Assert.Equal("Ctrl+F", loaded.QuickMenuShortcuts.Search);
        Assert.Equal("Ctrl+P", loaded.QuickMenuShortcuts.PastePlainText);
        Assert.Equal("Ctrl+M", loaded.QuickMenuShortcuts.ToggleMaskReveal);
        Assert.Equal("D", loaded.QuickMenuShortcuts.ToggleCapturedAt);
        Assert.True(loaded.StartWithWindows);
        Assert.False(loaded.MaskRules.Email);
        Assert.False(loaded.MaskRules.ShortAlphanumericCode);
        Assert.True(loaded.MaskRules.CreditCard);
        Assert.False(loaded.MaskRuleDefinitions.First(rule => rule.Id == MaskRuleIds.Email).Enabled);
        Assert.False(loaded.MaskRuleDefinitions.First(rule => rule.Id == MaskRuleIds.ShortAlphanumericCode).Enabled);
    }

    [Fact]
    public void Load_BackfillsMissingMaskRules()
    {
        var path = Path.Combine(Path.GetTempPath(), "clipton-tests", Guid.NewGuid().ToString("N"), "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """{"MaskSensitiveContent":true,"CustomMaskPatterns":["alpha-\\d+"]}""");
        var store = new JsonSettingsStore(path);

        var loaded = store.Load();

        Assert.True(loaded.MaskRules.Email);
        Assert.True(loaded.MaskRules.ShortAlphanumericCode);
        Assert.True(loaded.MaskRules.CustomPattern);
        Assert.Contains(loaded.MaskRuleDefinitions, rule => rule.Id == MaskRuleIds.Email && rule.Enabled);
    }

    [Fact]
    public void Load_PreservesPartialMaskRuleOverrides()
    {
        var path = Path.Combine(Path.GetTempPath(), "clipton-tests", Guid.NewGuid().ToString("N"), "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """{"MaskRules":{"Email":false,"ShortAlphanumericCode":false}}""");
        var store = new JsonSettingsStore(path);

        var loaded = store.Load();

        Assert.False(loaded.MaskRules.Email);
        Assert.False(loaded.MaskRules.ShortAlphanumericCode);
        Assert.True(loaded.MaskRules.CreditCard);
        Assert.False(loaded.MaskRuleDefinitions.First(rule => rule.Id == MaskRuleIds.Email).Enabled);
        Assert.False(loaded.MaskRuleDefinitions.First(rule => rule.Id == MaskRuleIds.ShortAlphanumericCode).Enabled);
    }

    [Fact]
    public void SaveAndLoad_RoundTripsEditedMaskRuleDefinitions()
    {
        var path = Path.Combine(Path.GetTempPath(), "clipton-tests", Guid.NewGuid().ToString("N"), "settings.json");
        var store = new JsonSettingsStore(path);
        var settings = new CliptonSettings();
        settings.MaskRuleDefinitions = MaskRuleDefinitionDefaults.CreateDefaultRules();
        var shortCodeRule = settings.MaskRuleDefinitions.First(rule => rule.Id == MaskRuleIds.ShortAlphanumericCode);
        shortCodeRule.Enabled = false;
        shortCodeRule.Pattern = @"\bSTORE-[A-Z0-9]{4}\b";

        store.Save(settings);
        var loaded = store.Load();

        var loadedRule = loaded.MaskRuleDefinitions.First(rule => rule.Id == MaskRuleIds.ShortAlphanumericCode);
        Assert.False(loadedRule.Enabled);
        Assert.Equal(@"\bSTORE-[A-Z0-9]{4}\b", loadedRule.Pattern);
        Assert.False(loaded.MaskRules.ShortAlphanumericCode);
    }

    [Fact]
    public void Load_NormalizesUnknownThemeAndLocaleToStoreSafeDefaults()
    {
        var path = Path.Combine(Path.GetTempPath(), "clipton-tests", Guid.NewGuid().ToString("N"), "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """{"Locale":"fr","Theme":"neon","MaxHistoryItems":200}""");
        var store = new JsonSettingsStore(path);

        var loaded = store.Load();

        Assert.Equal("en", loaded.Locale);
        Assert.Equal("light", loaded.Theme);
    }

    [Fact]
    public void Load_PreservesSystemThemeAndLocaleSettings()
    {
        var path = Path.Combine(Path.GetTempPath(), "clipton-tests", Guid.NewGuid().ToString("N"), "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """{"Locale":"system","Theme":"system","MaxHistoryItems":200}""");
        var store = new JsonSettingsStore(path);

        var loaded = store.Load();

        Assert.Equal("system", loaded.Locale);
        Assert.Equal("system", loaded.Theme);
    }

    [Fact]
    public void Load_ClampsHistoryLimitToSupportedRange()
    {
        var path = Path.Combine(Path.GetTempPath(), "clipton-tests", Guid.NewGuid().ToString("N"), "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """{"MaxHistoryItems":2000}""");
        var store = new JsonSettingsStore(path);

        var loaded = store.Load();

        Assert.Equal(1000, loaded.MaxHistoryItems);
    }

    [Fact]
    public void Load_NormalizesUnsupportedClipboardCaptureDelay()
    {
        var path = Path.Combine(Path.GetTempPath(), "clipton-tests", Guid.NewGuid().ToString("N"), "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """{"ClipboardCaptureDelayMilliseconds":333}""");
        var store = new JsonSettingsStore(path);

        var loaded = store.Load();

        Assert.Equal(150, loaded.ClipboardCaptureDelayMilliseconds);
    }

    [Fact]
    public void Load_NormalizesUnknownImagePreviewSizeToMedium()
    {
        var path = Path.Combine(Path.GetTempPath(), "clipton-tests", Guid.NewGuid().ToString("N"), "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """{"QuickMenuImagePreviewSize":"huge"}""");
        var store = new JsonSettingsStore(path);

        var loaded = store.Load();

        Assert.Equal("medium", loaded.QuickMenuImagePreviewSize);
    }

    [Fact]
    public void Load_NormalizesUnknownQuickMenuDisplayModeToDefault()
    {
        var path = Path.Combine(Path.GetTempPath(), "clipton-tests", Guid.NewGuid().ToString("N"), "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """{"QuickMenuDisplayMode":"floating"}""");
        var store = new JsonSettingsStore(path);

        var loaded = store.Load();

        Assert.Equal("default", loaded.QuickMenuDisplayMode);
    }

    [Fact]
    public void Load_NormalizesUnsupportedTopLevelHistoryItemsToFive()
    {
        var path = Path.Combine(Path.GetTempPath(), "clipton-tests", Guid.NewGuid().ToString("N"), "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """{"QuickMenuTopLevelHistoryItems":12}""");
        var store = new JsonSettingsStore(path);

        var loaded = store.Load();

        Assert.Equal(5, loaded.QuickMenuTopLevelHistoryItems);
    }

    [Fact]
    public void Load_NormalizesUnsupportedQuickMenuShortcuts()
    {
        var path = Path.Combine(Path.GetTempPath(), "clipton-tests", Guid.NewGuid().ToString("N"), "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """{"QuickMenuShortcuts":{"Search":"Alt+S","PastePlainText":"Ctrl+P","ToggleMaskReveal":"Ctrl+M","ToggleCapturedAt":"d"}}""");
        var store = new JsonSettingsStore(path);

        var loaded = store.Load();

        Assert.Equal("Ctrl+F", loaded.QuickMenuShortcuts.Search);
        Assert.Equal("Ctrl+P", loaded.QuickMenuShortcuts.PastePlainText);
        Assert.Equal("Ctrl+M", loaded.QuickMenuShortcuts.ToggleMaskReveal);
        Assert.Equal("D", loaded.QuickMenuShortcuts.ToggleCapturedAt);
    }
}
