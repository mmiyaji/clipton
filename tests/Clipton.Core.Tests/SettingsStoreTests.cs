using Clipton.Core;

namespace Clipton.Core.Tests;

public sealed class SettingsStoreTests
{
    [Fact]
    public void SaveAndLoad_RoundTripsSettings()
    {
        var path = Path.Combine(Path.GetTempPath(), "clipton-tests", Guid.NewGuid().ToString("N"), "settings.json");
        var store = new JsonSettingsStore(path);

        store.Save(new CliptonSettings
        {
            Hotkey = "Ctrl+Alt+V",
            Locale = "ja",
            MaxHistoryItems = 42,
            PastePlainTextByDefault = true,
            StartWithWindows = true
        });

        var loaded = store.Load();

        Assert.Equal("Ctrl+Alt+V", loaded.Hotkey);
        Assert.Equal("ja", loaded.Locale);
        Assert.Equal(42, loaded.MaxHistoryItems);
        Assert.True(loaded.PastePlainTextByDefault);
        Assert.True(loaded.StartWithWindows);
    }
}
