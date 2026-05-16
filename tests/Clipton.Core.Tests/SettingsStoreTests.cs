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
            HistoryPersistenceConfigured = true,
            Locale = "ja",
            MaxHistoryItems = 42,
            PauseCapture = true,
            PastePlainTextByDefault = true,
            PersistEncryptedHistory = true,
            StartWithWindows = true
        });

        var loaded = store.Load();

        Assert.Equal("Ctrl+Alt+V", loaded.Hotkey);
        Assert.Equal("ja", loaded.Locale);
        Assert.True(loaded.HistoryPersistenceConfigured);
        Assert.Equal(42, loaded.MaxHistoryItems);
        Assert.True(loaded.PauseCapture);
        Assert.True(loaded.PastePlainTextByDefault);
        Assert.True(loaded.PersistEncryptedHistory);
        Assert.True(loaded.StartWithWindows);
    }
}
