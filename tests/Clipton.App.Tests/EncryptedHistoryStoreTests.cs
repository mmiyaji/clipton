using System.IO;
using Clipton.Core;

namespace Clipton.App.Tests;

public sealed class EncryptedHistoryStoreTests
{
    [Fact]
    public void SaveAndLoad_RoundTripsClipboardHistory()
    {
        var path = Path.Combine(Path.GetTempPath(), "clipton-tests", Guid.NewGuid().ToString("N"), "history.dat");
        var store = new EncryptedHistoryStore(path);
        var snapshot = new ClipboardSnapshot(
            "history-1",
            DateTimeOffset.UtcNow,
            [ClipboardFormatKind.Text],
            text: "Persistent text");

        store.Save([snapshot]);

        var loaded = store.Load();
        var item = Assert.Single(loaded);
        Assert.Equal("history-1", item.Id);
        Assert.Equal("Persistent text", item.Text);
        Assert.Contains(ClipboardFormatKind.Text, item.Formats);
    }
}
