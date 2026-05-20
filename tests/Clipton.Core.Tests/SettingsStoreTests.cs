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
        Assert.Equal(200, loaded.MaxHistoryItems);
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
            FolderMode = true,
            HistoryPersistenceConfigured = true,
            Locale = "ja",
            MaxHistoryItems = 42,
            PauseCapture = true,
            PastePlainTextByDefault = true,
            PersistEncryptedHistory = true,
            StartWithWindows = true,
            Theme = "dark"
        });

        var loaded = store.Load();

        Assert.Equal("Ctrl+Alt+V", loaded.Hotkey);
        Assert.True(loaded.FolderMode);
        Assert.Equal("ja", loaded.Locale);
        Assert.Equal("dark", loaded.Theme);
        Assert.True(loaded.HistoryPersistenceConfigured);
        Assert.Equal(200, loaded.MaxHistoryItems);
        Assert.True(loaded.PauseCapture);
        Assert.True(loaded.PastePlainTextByDefault);
        Assert.True(loaded.PersistEncryptedHistory);
        Assert.True(loaded.StartWithWindows);
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
}
