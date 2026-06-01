using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
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

    [Fact]
    public void Save_WritesManifestAndSegmentFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "clipton-tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "history.dat");
        var store = new EncryptedHistoryStore(path);

        store.Save([TextSnapshot("history-1", "one"), TextSnapshot("history-2", "two")]);

        Assert.True(File.Exists(Path.Combine(root, "history", "manifest.dat")));
        Assert.True(File.Exists(Path.Combine(root, "history", "base.dat")));
    }

    [Fact]
    public void Save_DoesNotRewriteBaseForSmallAppend()
    {
        var root = Path.Combine(Path.GetTempPath(), "clipton-tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "history.dat");
        var store = new EncryptedHistoryStore(path);
        var oldItem = TextSnapshot("history-1", "one");

        store.Save([oldItem]);
        var basePath = Path.Combine(root, "history", "base.dat");
        var before = File.GetLastWriteTimeUtc(basePath);
        File.SetLastWriteTimeUtc(basePath, before.AddMinutes(-5));
        before = File.GetLastWriteTimeUtc(basePath);

        store.Save([TextSnapshot("history-2", "two"), oldItem]);

        Assert.Equal(before, File.GetLastWriteTimeUtc(basePath));
        Assert.True(File.Exists(Path.Combine(root, "history", "delta.dat")));
    }

    [Fact]
    public void Load_MigratesLegacySingleFileHistory()
    {
        var root = Path.Combine(Path.GetTempPath(), "clipton-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "history.dat");
        WriteLegacyHistory(path, [TextSnapshot("legacy-1", "legacy")]);
        var store = new EncryptedHistoryStore(path);

        var loaded = store.Load();

        var item = Assert.Single(loaded);
        Assert.Equal("legacy", item.Text);
        Assert.True(File.Exists(Path.Combine(root, "history", "manifest.dat")));
        Assert.True(File.Exists($"{path}.legacy.bak"));
    }

    private static ClipboardSnapshot TextSnapshot(string id, string text)
    {
        return new ClipboardSnapshot(id, DateTimeOffset.UtcNow, [ClipboardFormatKind.Text], text: text);
    }

    private static void WriteLegacyHistory(string path, ClipboardSnapshot[] snapshots)
    {
        var dto = snapshots.Select(snapshot => new
        {
            snapshot.Id,
            snapshot.CapturedAt,
            Formats = snapshot.Formats.ToArray(),
            snapshot.Text,
            snapshot.Rtf,
            snapshot.Html,
            snapshot.ImagePng,
            FilePaths = snapshot.FilePaths.ToArray()
        }).ToArray();
        var json = JsonSerializer.SerializeToUtf8Bytes(dto);
        var encrypted = ProtectedData.Protect(json, optionalEntropy: null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(path, encrypted);
    }
}
