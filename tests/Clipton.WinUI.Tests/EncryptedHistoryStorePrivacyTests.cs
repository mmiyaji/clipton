using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Clipton.Core;
using Clipton.WinUI;

namespace Clipton.WinUI.Tests;

public sealed class EncryptedHistoryStorePrivacyTests
{
    [Fact]
    public void Save_DoesNotPersistClipboardPayloadAsPlainText()
    {
        var root = CreateTestRoot();
        var path = Path.Combine(root, "history.dat");
        var store = new EncryptedHistoryStore(path);
        const string secretText = "clipton-privacy-secret-text-9d0e34b9";
        const string secretWindowTitle = "clipton-private-window-title-4c0f31d2";

        store.Save([new ClipboardSnapshot(
            "privacy-history-1",
            DateTimeOffset.UtcNow,
            [ClipboardFormatKind.Text],
            text: secretText,
            sourceApplicationName: "private-app",
            sourceWindowTitle: secretWindowTitle)]);

        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var bytes = File.ReadAllBytes(file);
            Assert.False(ContainsBytes(bytes, Encoding.UTF8.GetBytes(secretText)), $"Plain clipboard text leaked into {file}.");
            Assert.False(ContainsBytes(bytes, Encoding.UTF8.GetBytes(secretWindowTitle)), $"Plain source metadata leaked into {file}.");
        }
    }

    [Fact]
    public void Save_DoesNotRewriteUnchangedItemPayloadFiles()
    {
        var root = CreateTestRoot();
        var path = Path.Combine(root, "history.dat");
        var store = new EncryptedHistoryStore(path);
        var snapshots = Enumerable.Range(0, 20)
            .Select(index => new ClipboardSnapshot(
                $"history-{index:0000}",
                DateTimeOffset.UtcNow.AddSeconds(-index),
                [ClipboardFormatKind.Text],
                text: $"unchanged clipboard text {index}"))
            .ToArray();
        store.Save(snapshots);
        var itemFiles = Directory.EnumerateFiles(Path.Combine(root, "history", "items"), "*.dat").ToArray();
        Assert.Equal(snapshots.Length, itemFiles.Length);
        var marker = DateTime.UtcNow.AddDays(-2);
        foreach (var file in itemFiles)
        {
            File.SetLastWriteTimeUtc(file, marker);
        }

        var writeTimes = itemFiles.ToDictionary(file => file, File.GetLastWriteTimeUtc, StringComparer.Ordinal);

        store.Save(snapshots);

        foreach (var (file, writeTime) in writeTimes)
        {
            Assert.Equal(writeTime, File.GetLastWriteTimeUtc(file));
        }
    }

    [Fact]
    public void Save_ProtectsManifestMetadata()
    {
        var root = CreateTestRoot();
        var path = Path.Combine(root, "history.dat");
        var store = new EncryptedHistoryStore(path);
        store.Save([
            new ClipboardSnapshot("manifest-private-1", DateTimeOffset.UtcNow, [ClipboardFormatKind.Text], text: "one"),
            new ClipboardSnapshot("manifest-private-2", DateTimeOffset.UtcNow, [ClipboardFormatKind.Text], text: "two")
        ]);
        var manifestPath = Path.Combine(root, "history", "manifest.dat");

        var bytes = File.ReadAllBytes(manifestPath);

        Assert.False(ContainsBytes(bytes, Encoding.UTF8.GetBytes("manifest-private-1")));
        Assert.ThrowsAny<JsonException>(() => JsonDocument.Parse(bytes));

        var json = ProtectedData.Unprotect(bytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
        using var document = JsonDocument.Parse(json);
        Assert.Equal(5, document.RootElement.GetProperty("Version").GetInt32());
        Assert.Equal(["manifest-private-1", "manifest-private-2"], document.RootElement.GetProperty("OrderedIds").EnumerateArray().Select(item => item.GetString()));
    }

    [Fact]
    public void Save_RejectsItemIdOutsideItemsDirectoryBeforeWritingAnyPayload()
    {
        var root = CreateTestRoot();
        var path = Path.Combine(root, "history.dat");
        var store = new EncryptedHistoryStore(path);
        var outsidePath = Path.Combine(root, "snippets.dat");
        const string sentinel = "existing snippets must not be overwritten";
        Directory.CreateDirectory(root);
        File.WriteAllText(outsidePath, sentinel);

        var exception = Assert.Throws<InvalidDataException>(() => store.Save([
            new ClipboardSnapshot("valid-history-id", DateTimeOffset.UtcNow, [ClipboardFormatKind.Text], text: "valid"),
            new ClipboardSnapshot(@"..\..\snippets", DateTimeOffset.UtcNow, [ClipboardFormatKind.Text], text: "malicious")
        ]));

        Assert.Contains("item id", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(sentinel, File.ReadAllText(outsidePath));
        Assert.False(File.Exists(Path.Combine(root, "history", "items", "valid-history-id.dat")));
        Assert.False(File.Exists(Path.Combine(root, "history", "manifest.dat")));
    }

    [Fact]
    public void Save_WhenManifestCommitFails_PreservesPayloadsReferencedByPreviousManifest()
    {
        var root = CreateTestRoot();
        var path = Path.Combine(root, "history.dat");
        var store = new EncryptedHistoryStore(path);
        var retained = new ClipboardSnapshot(
            "retained-history-id",
            DateTimeOffset.UtcNow,
            [ClipboardFormatKind.Text],
            text: "retained");
        var removed = new ClipboardSnapshot(
            "removed-history-id",
            DateTimeOffset.UtcNow.AddSeconds(-1),
            [ClipboardFormatKind.Text],
            text: "removed after commit");
        store.Save([retained, removed]);
        var manifestPath = Path.Combine(root, "history", "manifest.dat");
        var removedItemPath = Path.Combine(root, "history", "items", "removed-history-id.dat");

        using (File.Open(manifestPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var exception = Record.Exception(() => store.Save([retained]));
            Assert.True(exception is IOException or UnauthorizedAccessException, exception?.ToString());
            Assert.True(File.Exists(removedItemPath));
        }

        Assert.Equal(
            ["retained-history-id", "removed-history-id"],
            store.LoadRecent(10).Select(item => item.Id));
    }

    [Fact]
    public void LoadAllStrict_WhenPayloadIdDoesNotMatchManifest_Throws()
    {
        var root = CreateTestRoot();
        var path = Path.Combine(root, "history.dat");
        var store = new EncryptedHistoryStore(path);
        store.Save([
            new ClipboardSnapshot("strict-item-1", DateTimeOffset.UtcNow, [ClipboardFormatKind.Text], text: "one"),
            new ClipboardSnapshot("strict-item-2", DateTimeOffset.UtcNow.AddSeconds(-1), [ClipboardFormatKind.Text], text: "two")
        ]);
        var itemsDirectory = Path.Combine(root, "history", "items");
        File.Copy(
            Path.Combine(itemsDirectory, "strict-item-1.dat"),
            Path.Combine(itemsDirectory, "strict-item-2.dat"),
            overwrite: true);

        Assert.Throws<InvalidDataException>(() => store.LoadAllStrict());
    }

    [Fact]
    public void ClearSourceMetadata_RewritesExistingItemPayloadFiles()
    {
        var root = CreateTestRoot();
        var path = Path.Combine(root, "history.dat");
        var store = new EncryptedHistoryStore(path);
        store.Save([new ClipboardSnapshot(
            "history-with-source",
            DateTimeOffset.UtcNow,
            [ClipboardFormatKind.Text],
            text: "private clipboard text",
            sourceApplicationName: "PrivateApp",
            sourceWindowTitle: "Private Window")]);
        var itemPath = Path.Combine(root, "history", "items", "history-with-source.dat");
        Assert.True(File.Exists(itemPath));
        var marker = DateTime.UtcNow.AddDays(-2);
        File.SetLastWriteTimeUtc(itemPath, marker);
        var writeTime = File.GetLastWriteTimeUtc(itemPath);

        store.ClearSourceMetadata();

        Assert.True(File.GetLastWriteTimeUtc(itemPath) > writeTime);
        var item = Assert.Single(store.Load());
        Assert.Null(item.SourceApplicationName);
        Assert.Null(item.SourceWindowTitle);
    }

    [Fact]
    public void ClearSourceMetadata_WhenAnItemCannotBeRead_DoesNotSavePartialHistory()
    {
        var root = CreateTestRoot();
        var path = Path.Combine(root, "history.dat");
        var store = new EncryptedHistoryStore(path);
        store.Save([
            new ClipboardSnapshot(
                "readable-history-id",
                DateTimeOffset.UtcNow,
                [ClipboardFormatKind.Text],
                text: "readable",
                sourceApplicationName: "PrivateApp",
                sourceWindowTitle: "Private Window"),
            new ClipboardSnapshot(
                "corrupt-history-id",
                DateTimeOffset.UtcNow.AddSeconds(-1),
                [ClipboardFormatKind.Text],
                text: "corrupt",
                sourceApplicationName: "PrivateApp",
                sourceWindowTitle: "Private Window")
        ]);
        var manifestPath = Path.Combine(root, "history", "manifest.dat");
        var readableItemPath = Path.Combine(root, "history", "items", "readable-history-id.dat");
        var corruptItemPath = Path.Combine(root, "history", "items", "corrupt-history-id.dat");
        File.WriteAllBytes(corruptItemPath, [0x01, 0x02, 0x03, 0x04]);
        var manifestBefore = File.ReadAllBytes(manifestPath);
        var readableItemBefore = File.ReadAllBytes(readableItemPath);
        var corruptItemBefore = File.ReadAllBytes(corruptItemPath);

        store.ClearSourceMetadata();

        Assert.Equal(manifestBefore, File.ReadAllBytes(manifestPath));
        Assert.Equal(readableItemBefore, File.ReadAllBytes(readableItemPath));
        Assert.Equal(corruptItemBefore, File.ReadAllBytes(corruptItemPath));
        var readable = Assert.Single(store.LoadRange(0, 1));
        Assert.Equal("PrivateApp", readable.SourceApplicationName);
        Assert.Equal("Private Window", readable.SourceWindowTitle);
    }

    private static bool ContainsBytes(byte[] haystack, byte[] needle)
    {
        if (needle.Length == 0 || haystack.Length < needle.Length)
        {
            return false;
        }

        for (var index = 0; index <= haystack.Length - needle.Length; index++)
        {
            if (haystack.AsSpan(index, needle.Length).SequenceEqual(needle))
            {
                return true;
            }
        }

        return false;
    }

    private static string CreateTestRoot()
    {
        return Path.Combine(Path.GetTempPath(), "clipton-winui-history-privacy-tests", Guid.NewGuid().ToString("N"));
    }
}
