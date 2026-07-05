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
            text: "Persistent text",
            sourceApplicationName: "notepad",
            sourceWindowTitle: "notes.txt");

        store.Save([snapshot]);

        var loaded = store.Load();
        var item = Assert.Single(loaded);
        Assert.Equal("history-1", item.Id);
        Assert.Equal("Persistent text", item.Text);
        Assert.Contains(ClipboardFormatKind.Text, item.Formats);
        Assert.Equal("notepad", item.SourceApplicationName);
        Assert.Equal("notes.txt", item.SourceWindowTitle);
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
    public void Save_TreatsUnreadableDeltaAsEmptyWhenAppending()
    {
        var root = CreateTestRoot();
        var path = Path.Combine(root, "history.dat");
        var store = new EncryptedHistoryStore(path);
        var baseItem = TextSnapshot("history-1", "one");
        var removedDeltaItem = TextSnapshot("history-2", "two");
        var newDeltaItem = TextSnapshot("history-3", "three");

        store.Save([baseItem]);
        store.Save([removedDeltaItem, baseItem]);
        File.WriteAllBytes(Path.Combine(root, "history", "delta.dat"), [1, 2, 3, 4]);

        store.Save([newDeltaItem, baseItem]);

        var loaded = store.Load();
        Assert.Equal(["history-3", "history-1"], loaded.Select(item => item.Id));
    }

    [Fact]
    public void Load_ReturnsEmptyWhenHistoryDoesNotExist()
    {
        var root = CreateTestRoot();
        var path = Path.Combine(root, "history.dat");
        var store = new EncryptedHistoryStore(path);

        var loaded = store.Load();

        Assert.Empty(loaded);
        Assert.False(Directory.Exists(Path.Combine(root, "history")));
    }

    [Fact]
    public void SaveAndLoad_WorksWithRelativeLegacyPath()
    {
        var root = CreateTestRoot();
        Directory.CreateDirectory(root);
        var previous = Environment.CurrentDirectory;
        try
        {
            Environment.CurrentDirectory = root;
            var store = new EncryptedHistoryStore("history.dat");

            store.Save([TextSnapshot("history-1", "one")]);

            var item = Assert.Single(store.Load());
            Assert.Equal("one", item.Text);
            Assert.True(File.Exists(Path.Combine(root, "history", "base.dat")));
        }
        finally
        {
            Environment.CurrentDirectory = previous;
        }
    }

    [Fact]
    public void Load_ReturnsEmptyForCorruptedLegacyHistory()
    {
        var root = CreateTestRoot();
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "history.dat");
        File.WriteAllBytes(path, [1, 2, 3, 4]);
        var store = new EncryptedHistoryStore(path);

        var loaded = store.Load();

        Assert.Empty(loaded);
        Assert.False(Directory.Exists(Path.Combine(root, "history")));
    }

    [Fact]
    public void Load_ReturnsEmptyForProtectedNullLegacyHistory()
    {
        var root = CreateTestRoot();
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "history.dat");
        var encrypted = ProtectedData.Protect("null"u8.ToArray(), optionalEntropy: null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(path, encrypted);
        var store = new EncryptedHistoryStore(path);

        var loaded = store.Load();

        Assert.Empty(loaded);
        Assert.False(Directory.Exists(Path.Combine(root, "history")));
    }

    [Fact]
    public void Load_ReturnsEmptyForUnsupportedSegmentedManifestVersion()
    {
        var root = CreateTestRoot();
        var path = Path.Combine(root, "history.dat");
        var store = new EncryptedHistoryStore(path);

        store.Save([TextSnapshot("history-1", "one")]);
        WriteManifest(root, version: 2, ["history-1"], ["history-1"], []);

        Assert.Empty(store.Load());
    }

    [Fact]
    public void Load_IgnoresMissingBaseSegmentAndReturnsDeltaItems()
    {
        var root = CreateTestRoot();
        var path = Path.Combine(root, "history.dat");
        var store = new EncryptedHistoryStore(path);
        var baseItem = TextSnapshot("history-1", "one");
        var deltaItem = TextSnapshot("history-2", "two");

        store.Save([baseItem]);
        store.Save([deltaItem, baseItem]);
        File.Delete(Path.Combine(root, "history", "base.dat"));

        var loaded = store.Load();

        var item = Assert.Single(loaded);
        Assert.Equal("history-2", item.Id);
    }

    [Fact]
    public void Load_ReturnsEmptyWhenSegmentCannotBeDecrypted()
    {
        var root = CreateTestRoot();
        var path = Path.Combine(root, "history.dat");
        var store = new EncryptedHistoryStore(path);

        store.Save([TextSnapshot("history-1", "one")]);
        File.WriteAllBytes(Path.Combine(root, "history", "base.dat"), [1, 2, 3, 4]);

        Assert.Empty(store.Load());
    }

    [Fact]
    public void Load_ReadsProtectedCompatibilityManifest()
    {
        var root = CreateTestRoot();
        var path = Path.Combine(root, "history.dat");
        var store = new EncryptedHistoryStore(path);

        store.Save([TextSnapshot("history-1", "one")]);
        WriteProtectedManifest(root, version: 3, ["history-1"], ["history-1"], []);

        var item = Assert.Single(store.Load());
        Assert.Equal("one", item.Text);
    }

    [Fact]
    public void Save_RecompactsWhenBaseSegmentIsMissing()
    {
        var root = CreateTestRoot();
        var path = Path.Combine(root, "history.dat");
        var store = new EncryptedHistoryStore(path);

        store.Save([TextSnapshot("history-1", "one")]);
        var basePath = Path.Combine(root, "history", "base.dat");
        File.Delete(basePath);

        store.Save([TextSnapshot("history-2", "two")]);

        Assert.True(File.Exists(basePath));
        Assert.False(File.Exists(Path.Combine(root, "history", "delta.dat")));
        var item = Assert.Single(store.Load());
        Assert.Equal("two", item.Text);
    }

    [Fact]
    public void Save_RecompactsWhenAllBaseItemsAreRemoved()
    {
        var root = CreateTestRoot();
        var path = Path.Combine(root, "history.dat");
        var store = new EncryptedHistoryStore(path);
        var baseItem = TextSnapshot("history-1", "one");

        store.Save([baseItem]);
        store.Save([TextSnapshot("history-2", "two"), baseItem]);
        Assert.True(File.Exists(Path.Combine(root, "history", "delta.dat")));

        store.Save([TextSnapshot("history-3", "three")]);

        Assert.False(File.Exists(Path.Combine(root, "history", "delta.dat")));
        var item = Assert.Single(store.Load());
        Assert.Equal("three", item.Text);
    }

    [Fact]
    public void Save_RecompactsWhenDeltaReachesThreshold()
    {
        var root = CreateTestRoot();
        var path = Path.Combine(root, "history.dat");
        var store = new EncryptedHistoryStore(path);
        var baseItem = TextSnapshot("history-0", "zero");

        store.Save([baseItem]);
        var snapshots = Enumerable
            .Range(1, 50)
            .Select(index => TextSnapshot($"history-{index}", index.ToString()))
            .Append(baseItem)
            .ToArray();

        store.Save(snapshots);

        Assert.False(File.Exists(Path.Combine(root, "history", "delta.dat")));
        var loaded = store.Load();
        Assert.Equal(51, loaded.Count);
        Assert.Equal("history-1", loaded[0].Id);
        Assert.Equal("history-0", loaded[^1].Id);
    }

    [Fact]
    public void Save_DropsRemovedDeltaItemsWithoutRewritingDelta()
    {
        var root = CreateTestRoot();
        var path = Path.Combine(root, "history.dat");
        var store = new EncryptedHistoryStore(path);
        var baseItem = TextSnapshot("history-1", "one");
        var deltaItem = TextSnapshot("history-2", "two");

        store.Save([baseItem]);
        store.Save([deltaItem, baseItem]);
        var deltaPath = Path.Combine(root, "history", "delta.dat");
        var before = File.GetLastWriteTimeUtc(deltaPath);
        File.SetLastWriteTimeUtc(deltaPath, before.AddMinutes(-5));
        before = File.GetLastWriteTimeUtc(deltaPath);

        store.Save([baseItem]);

        Assert.Equal(before, File.GetLastWriteTimeUtc(deltaPath));
        var item = Assert.Single(store.Load());
        Assert.Equal("history-1", item.Id);
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

    [Fact]
    public void Delete_RemovesLegacyFileAndSegmentedHistory()
    {
        var root = CreateTestRoot();
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "history.dat");
        File.WriteAllText(path, "legacy");
        File.WriteAllText($"{path}.legacy.bak", "legacy backup");
        var store = new EncryptedHistoryStore(path);

        store.Save([TextSnapshot("history-1", "one")]);
        store.Delete();

        Assert.False(File.Exists(path));
        Assert.False(File.Exists($"{path}.legacy.bak"));
        Assert.False(Directory.Exists(Path.Combine(root, "history")));
    }

    [Fact]
    public void Delete_DoesNothingWhenHistoryFilesAreMissing()
    {
        var root = CreateTestRoot();
        var path = Path.Combine(root, "history.dat");
        var store = new EncryptedHistoryStore(path);

        store.Delete();

        Assert.False(File.Exists(path));
        Assert.False(Directory.Exists(Path.Combine(root, "history")));
    }

    private static string CreateTestRoot()
    {
        return Path.Combine(Path.GetTempPath(), "clipton-tests", Guid.NewGuid().ToString("N"));
    }

    private static ClipboardSnapshot TextSnapshot(string id, string text)
    {
        return new ClipboardSnapshot(id, DateTimeOffset.UtcNow, [ClipboardFormatKind.Text], text: text);
    }

    private static void WriteManifest(
        string root,
        int version,
        string[] orderedIds,
        string[] baseIds,
        string[] deltaIds)
    {
        var manifest = new
        {
            Version = version,
            OrderedIds = orderedIds,
            BaseIds = baseIds,
            DeltaIds = deltaIds
        };
        var manifestPath = Path.Combine(root, "history", "manifest.dat");
        File.WriteAllBytes(manifestPath, JsonSerializer.SerializeToUtf8Bytes(manifest));
    }

    private static void WriteProtectedManifest(
        string root,
        int version,
        string[] orderedIds,
        string[] baseIds,
        string[] deltaIds)
    {
        var manifest = new
        {
            Version = version,
            OrderedIds = orderedIds,
            BaseIds = baseIds,
            DeltaIds = deltaIds
        };
        var json = JsonSerializer.SerializeToUtf8Bytes(manifest);
        var encrypted = ProtectedData.Protect(json, optionalEntropy: null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(Path.Combine(root, "history", "manifest.dat"), encrypted);
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
