using System.Security.Cryptography;
using System.Text.Json;
using Clipton.Core;
using Clipton.WinUI;

namespace Clipton.WinUI.Tests;

public sealed class EncryptedHistoryStoreUpgradeTests
{
    [Fact]
    public void LoadRecent_ReadsLegacySingleFileHistory()
    {
        var root = CreateTestRoot();
        var path = Path.Combine(root, "history.dat");
        var store = new EncryptedHistoryStore(path);
        WriteProtected(path, new[]
        {
            Dto("legacy-1", "oldest"),
            Dto("legacy-2", "newest")
        });

        var loaded = store.LoadRecent(2);

        Assert.Equal(2, store.Count());
        Assert.Equal(["legacy-1", "legacy-2"], loaded.Select(item => item.Id));
    }

    [Fact]
    public void LoadAllStrict_ReadsLegacySingleFileHistory()
    {
        var root = CreateTestRoot();
        var path = Path.Combine(root, "history.dat");
        var store = new EncryptedHistoryStore(path);
        WriteProtected(path, new[]
        {
            Dto("legacy-1", "oldest"),
            Dto("legacy-2", "newest")
        });

        var loaded = store.LoadAllStrict();

        Assert.Equal(["legacy-1", "legacy-2"], loaded.Select(item => item.Id));
    }

    [Fact]
    public void LoadRange_ReadsLegacySegmentedVersion3History()
    {
        var root = CreateTestRoot();
        var path = Path.Combine(root, "history.dat");
        var store = new EncryptedHistoryStore(path);
        var historyRoot = Path.Combine(root, "history");
        Directory.CreateDirectory(historyRoot);
        WriteManifest(root, version: 3, ["delta-1", "base-1"], ["base-1"], ["delta-1"]);
        WriteProtected(Path.Combine(historyRoot, "base.dat"), new[] { Dto("base-1", "base") });
        WriteProtected(Path.Combine(historyRoot, "delta.dat"), new[] { Dto("delta-1", "delta") });

        var loaded = store.LoadRange(0, 10);

        Assert.Equal(2, store.Count());
        Assert.Equal(["delta-1", "base-1"], loaded.Select(item => item.Id));
        Assert.Equal("delta", loaded[0].Text);
    }

    [Fact]
    public void LoadRange_ReadsChunkedVersion4History()
    {
        var root = CreateTestRoot();
        var path = Path.Combine(root, "history.dat");
        var store = new EncryptedHistoryStore(path);
        var historyRoot = Path.Combine(root, "history");
        var chunkRoot = Path.Combine(historyRoot, "chunks");
        Directory.CreateDirectory(chunkRoot);
        WriteManifest(
            root,
            version: 4,
            orderedIds: ["chunk-1", "chunk-2", "chunk-3"],
            baseIds: ["chunk-1", "chunk-2", "chunk-3"],
            deltaIds: [],
            chunkIds: ["chunk-0000.dat"]);
        WriteProtected(Path.Combine(chunkRoot, "chunk-0000.dat"), new[]
        {
            Dto("chunk-1", "first"),
            Dto("chunk-2", "second"),
            Dto("chunk-3", "third")
        });

        var loaded = store.LoadRange(1, 1);

        Assert.Equal(3, store.Count());
        var item = Assert.Single(loaded);
        Assert.Equal("chunk-2", item.Id);
        Assert.Equal("second", item.Text);
    }

    [Fact]
    public void SavePreservingOlder_UpgradesLegacySegmentedHistoryToItemizedVersion5()
    {
        var root = CreateTestRoot();
        var path = Path.Combine(root, "history.dat");
        var store = new EncryptedHistoryStore(path);
        var historyRoot = Path.Combine(root, "history");
        Directory.CreateDirectory(historyRoot);
        WriteManifest(root, version: 3, ["old-1"], ["old-1"], []);
        WriteProtected(Path.Combine(historyRoot, "base.dat"), new[] { Dto("old-1", "old") });

        store.SavePreservingOlder([Snapshot("new-1", "new")], loadedPersistedCount: 0, capacity: 10);

        var manifest = ReadManifest(root);
        Assert.Equal(5, manifest.GetProperty("Version").GetInt32());
        Assert.Equal(["new-1", "old-1"], manifest.GetProperty("OrderedIds").EnumerateArray().Select(item => item.GetString()));
        Assert.True(File.Exists(Path.Combine(historyRoot, "items", "new-1.dat")));
        Assert.True(File.Exists(Path.Combine(historyRoot, "items", "old-1.dat")));
        Assert.Equal(["new-1", "old-1"], store.LoadRecent(10).Select(item => item.Id));
    }

    [Fact]
    public void SavePreservingOlder_DoesNotRestoreDeletedItemFromLoadedPrefix()
    {
        var root = CreateTestRoot();
        var path = Path.Combine(root, "history.dat");
        var store = new EncryptedHistoryStore(path);
        store.Save([
            Snapshot("persisted-1", "first"),
            Snapshot("persisted-2", "second"),
            Snapshot("persisted-3", "third"),
            Snapshot("persisted-4", "fourth")
        ]);

        store.SavePreservingOlder(
            [Snapshot("new-1", "new"), Snapshot("persisted-1", "first")],
            loadedPersistedCount: 2,
            capacity: 10);

        Assert.Equal(
            ["new-1", "persisted-1", "persisted-3", "persisted-4"],
            store.LoadRecent(10).Select(item => item.Id));
        Assert.False(File.Exists(Path.Combine(root, "history", "items", "persisted-2.dat")));
    }

    [Fact]
    public void CountAndLoadRange_RepairItemizedManifestWhenItemFileIsMissing()
    {
        var root = CreateTestRoot();
        var path = Path.Combine(root, "history.dat");
        var store = new EncryptedHistoryStore(path);
        var historyRoot = Path.Combine(root, "history");
        var itemRoot = Path.Combine(historyRoot, "items");
        Directory.CreateDirectory(itemRoot);
        WriteManifest(
            root,
            version: 5,
            orderedIds: ["present-1", "missing-1"],
            baseIds: ["present-1", "missing-1"],
            deltaIds: []);
        WriteProtected(Path.Combine(itemRoot, "present-1.dat"), Dto("present-1", "present"));

        Assert.Equal(1, store.Count());
        var loaded = store.LoadRange(0, 10);

        var item = Assert.Single(loaded);
        Assert.Equal("present-1", item.Id);
        var manifest = ReadManifest(root);
        Assert.Equal(["present-1"], manifest.GetProperty("OrderedIds").EnumerateArray().Select(entry => entry.GetString()));
    }

    private static string CreateTestRoot()
    {
        return Path.Combine(Path.GetTempPath(), "clipton-winui-upgrade-tests", Guid.NewGuid().ToString("N"));
    }

    private static ClipboardSnapshot Snapshot(string id, string text)
    {
        return new ClipboardSnapshot(id, DateTimeOffset.UtcNow, [ClipboardFormatKind.Text], text: text);
    }

    private static ClipboardSnapshotDto Dto(string id, string text)
    {
        return new ClipboardSnapshotDto
        {
            Id = id,
            CapturedAt = DateTimeOffset.UtcNow,
            Formats = [ClipboardFormatKind.Text],
            Text = text
        };
    }

    private static void WriteManifest(
        string root,
        int version,
        string[] orderedIds,
        string[] baseIds,
        string[] deltaIds,
        string[]? chunkIds = null)
    {
        var manifest = new
        {
            Version = version,
            OrderedIds = orderedIds,
            BaseIds = baseIds,
            DeltaIds = deltaIds,
            ChunkIds = chunkIds
        };
        var manifestPath = Path.Combine(root, "history", "manifest.dat");
        Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
        File.WriteAllBytes(manifestPath, JsonSerializer.SerializeToUtf8Bytes(manifest));
    }

    private static JsonElement ReadManifest(string root)
    {
        var bytes = File.ReadAllBytes(Path.Combine(root, "history", "manifest.dat"));
        try
        {
            bytes = ProtectedData.Unprotect(bytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
        }
        catch (CryptographicException)
        {
            // Older manifests were stored as plaintext JSON.
        }

        using var document = JsonDocument.Parse(bytes);
        return document.RootElement.Clone();
    }

    private static void WriteProtected<T>(string path, T value)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.SerializeToUtf8Bytes(value, new JsonSerializerOptions { WriteIndented = true });
        var encrypted = ProtectedData.Protect(json, optionalEntropy: null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(path, encrypted);
    }

    private sealed class ClipboardSnapshotDto
    {
        public string Id { get; set; } = string.Empty;

        public DateTimeOffset CapturedAt { get; set; }

        public ClipboardFormatKind[] Formats { get; set; } = [];

        public string? Text { get; set; }

        public string? Rtf { get; set; }

        public string? Html { get; set; }

        public byte[]? ImagePng { get; set; }

        public string[] FilePaths { get; set; } = [];

        public string? SourceApplicationName { get; set; }

        public string? SourceWindowTitle { get; set; }
    }
}
