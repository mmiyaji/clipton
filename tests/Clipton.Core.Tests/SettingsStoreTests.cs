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
    public void Load_ReturnsDefaultsWhenSettingsJsonIsNullLiteral()
    {
        var path = Path.Combine(Path.GetTempPath(), "clipton-tests", Guid.NewGuid().ToString("N"), "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "null");
        var store = new JsonSettingsStore(path);

        var loaded = store.Load();

        Assert.Equal(200, loaded.MaxHistoryItems);
        Assert.True(loaded.PersistEncryptedHistory);
    }

    [Fact]
    public void Load_ReturnsDefaultsWhenSettingsJsonRootIsArray()
    {
        var path = Path.Combine(Path.GetTempPath(), "clipton-tests", Guid.NewGuid().ToString("N"), "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "[]");
        var store = new JsonSettingsStore(path);

        var loaded = store.Load();

        Assert.Equal(200, loaded.MaxHistoryItems);
        Assert.True(loaded.PersistEncryptedHistory);
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
            QuickMenuPasteOptions = new QuickMenuPasteOptionSettings
            {
                DisabledOptionIds =
                [
                    QuickMenuPasteOptionIds.PasteLowercase,
                    QuickMenuPasteOptionIds.TogglePin
                ]
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
        Assert.Equal(
            [QuickMenuPasteOptionIds.PasteLowercase, QuickMenuPasteOptionIds.TogglePin],
            loaded.QuickMenuPasteOptions.DisabledOptionIds);
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
    public void Load_BackfillsNullMaskRules()
    {
        var path = Path.Combine(Path.GetTempPath(), "clipton-tests", Guid.NewGuid().ToString("N"), "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """{"MaskRules":null}""");
        var store = new JsonSettingsStore(path);

        var loaded = store.Load();

        Assert.True(loaded.MaskRules.Email);
        Assert.True(loaded.MaskRules.CustomPattern);
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
    public void Load_NormalizesConfiguredMaskRuleDefinitionFallbackFields()
    {
        var path = Path.Combine(Path.GetTempPath(), "clipton-tests", Guid.NewGuid().ToString("N"), "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """
{
  "MaskRuleDefinitions": [
    { "Id": "email", "NameKey": " ", "Pattern": " ", "Enabled": false, "Order": 0 },
    { "Id": "email", "NameKey": "Duplicate", "Pattern": "duplicate", "Enabled": true, "Order": 99 },
    { "Id": "unknown", "NameKey": "Unknown", "Pattern": "unknown", "Enabled": true, "Order": 1 }
  ]
}
""");
        var store = new JsonSettingsStore(path);

        var loaded = store.Load();
        var email = loaded.MaskRuleDefinitions.First(rule => rule.Id == MaskRuleIds.Email);

        Assert.Equal("MaskRuleEmail", email.NameKey);
        Assert.Contains("[A-Z0-9", email.Pattern);
        Assert.False(email.Enabled);
        Assert.Equal(10, email.Order);
        Assert.DoesNotContain(loaded.MaskRuleDefinitions, rule => rule.Id == "unknown");
    }

    [Fact]
    public void MaskRuleDefinitionDefaults_NormalizeReturnsDefaultsForNullDefinitions()
    {
        var normalized = MaskRuleDefinitionDefaults.Normalize(null);

        Assert.Equal(MaskRuleDefinitionDefaults.CreateDefaultRules().Length, normalized.Length);
        Assert.Contains(normalized, rule => rule.Id == MaskRuleIds.Email && rule.Enabled);
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
    public void Load_NormalizesDarkThemeAndJapaneseLocaleCaseInsensitively()
    {
        var path = Path.Combine(Path.GetTempPath(), "clipton-tests", Guid.NewGuid().ToString("N"), "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """{"Locale":"JA","Theme":"DARK","MaxHistoryItems":200}""");
        var store = new JsonSettingsStore(path);

        var loaded = store.Load();

        Assert.Equal("ja", loaded.Locale);
        Assert.Equal("dark", loaded.Theme);
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
    public void Load_ClampsHistoryLimitToMinimum()
    {
        var path = Path.Combine(Path.GetTempPath(), "clipton-tests", Guid.NewGuid().ToString("N"), "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """{"MaxHistoryItems":0}""");
        var store = new JsonSettingsStore(path);

        var loaded = store.Load();

        Assert.Equal(1, loaded.MaxHistoryItems);
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

    [Theory]
    [InlineData(0)]
    [InlineData(50)]
    [InlineData(100)]
    [InlineData(150)]
    [InlineData(250)]
    [InlineData(500)]
    [InlineData(1000)]
    public void Load_PreservesSupportedClipboardCaptureDelays(int delay)
    {
        var path = Path.Combine(Path.GetTempPath(), "clipton-tests", Guid.NewGuid().ToString("N"), "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, $$"""{"ClipboardCaptureDelayMilliseconds":{{delay}}}""");
        var store = new JsonSettingsStore(path);

        var loaded = store.Load();

        Assert.Equal(delay, loaded.ClipboardCaptureDelayMilliseconds);
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
    public void Load_NormalizesNullImagePreviewSizeToMedium()
    {
        var path = Path.Combine(Path.GetTempPath(), "clipton-tests", Guid.NewGuid().ToString("N"), "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """{"QuickMenuImagePreviewSize":null}""");
        var store = new JsonSettingsStore(path);

        var loaded = store.Load();

        Assert.Equal("medium", loaded.QuickMenuImagePreviewSize);
    }

    [Theory]
    [InlineData("none")]
    [InlineData("small")]
    [InlineData("large")]
    public void Load_PreservesSupportedImagePreviewSizes(string size)
    {
        var path = Path.Combine(Path.GetTempPath(), "clipton-tests", Guid.NewGuid().ToString("N"), "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, $$"""{"QuickMenuImagePreviewSize":"{{size.ToUpperInvariant()}}"}""");
        var store = new JsonSettingsStore(path);

        var loaded = store.Load();

        Assert.Equal(size, loaded.QuickMenuImagePreviewSize);
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

    [Fact]
    public void Load_NormalizesShortcutWhitespaceControlAliasAndBlankValues()
    {
        var path = Path.Combine(Path.GetTempPath(), "clipton-tests", Guid.NewGuid().ToString("N"), "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """{"QuickMenuShortcuts":{"Search":" Control + s ","PastePlainText":" p ","ToggleMaskReveal":" ","ToggleCapturedAt":" Control + d "}}""");
        var store = new JsonSettingsStore(path);

        var loaded = store.Load();

        Assert.Equal("Ctrl+S", loaded.QuickMenuShortcuts.Search);
        Assert.Equal("P", loaded.QuickMenuShortcuts.PastePlainText);
        Assert.Equal("Ctrl+M", loaded.QuickMenuShortcuts.ToggleMaskReveal);
        Assert.Equal("Ctrl+D", loaded.QuickMenuShortcuts.ToggleCapturedAt);
    }

    [Fact]
    public void Load_NormalizesShortcutContainingOnlySeparators()
    {
        var path = Path.Combine(Path.GetTempPath(), "clipton-tests", Guid.NewGuid().ToString("N"), "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """{"QuickMenuShortcuts":{"Search":"+","PastePlainText":"Ctrl + Alt + P"}}""");
        var store = new JsonSettingsStore(path);

        var loaded = store.Load();

        Assert.Equal("Ctrl+F", loaded.QuickMenuShortcuts.Search);
        Assert.Equal("T", loaded.QuickMenuShortcuts.PastePlainText);
    }

    [Fact]
    public void Load_BackfillsNullQuickMenuShortcuts()
    {
        var path = Path.Combine(Path.GetTempPath(), "clipton-tests", Guid.NewGuid().ToString("N"), "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """{"QuickMenuShortcuts":null}""");
        var store = new JsonSettingsStore(path);

        var loaded = store.Load();

        Assert.Equal("Ctrl+F", loaded.QuickMenuShortcuts.Search);
        Assert.Equal("T", loaded.QuickMenuShortcuts.PastePlainText);
    }

    [Fact]
    public void Load_NormalizesQuickMenuPasteOptionSettings()
    {
        var path = Path.Combine(Path.GetTempPath(), "clipton-tests", Guid.NewGuid().ToString("N"), "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """{"QuickMenuPasteOptions":{"DisabledOptionIds":["paste-lowercase","unknown","paste-lowercase"," "]}}""");
        var store = new JsonSettingsStore(path);

        var loaded = store.Load();

        Assert.Equal([QuickMenuPasteOptionIds.PasteLowercase], loaded.QuickMenuPasteOptions.DisabledOptionIds);
    }

    [Fact]
    public void Load_BackfillsNullQuickMenuPasteOptions()
    {
        var path = Path.Combine(Path.GetTempPath(), "clipton-tests", Guid.NewGuid().ToString("N"), "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """{"QuickMenuPasteOptions":null}""");
        var store = new JsonSettingsStore(path);

        var loaded = store.Load();

        Assert.Empty(loaded.QuickMenuPasteOptions.DisabledOptionIds);
    }

    [Fact]
    public void Save_WritesRelativePathInCurrentDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "clipton-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var previous = Environment.CurrentDirectory;
        try
        {
            Environment.CurrentDirectory = root;
            var store = new JsonSettingsStore("settings.json");

            store.Save(new CliptonSettings { Locale = "ja" });

            Assert.True(File.Exists(Path.Combine(root, "settings.json")));
        }
        finally
        {
            Environment.CurrentDirectory = previous;
        }
    }
}
