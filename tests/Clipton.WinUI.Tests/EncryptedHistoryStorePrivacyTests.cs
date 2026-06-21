using System.Text;
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
