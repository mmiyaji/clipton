using System.Text.Json;
using Clipton.Core;
using Clipton.WinUI;

namespace Clipton.WinUI.Tests;

public sealed class RuntimeSafetyRegressionTests
{
    [Fact]
    public void ImportHistory_ReplacesEveryExternalIdBeforePersistence()
    {
        var root = CreateTestRoot();
        Directory.CreateDirectory(root);
        using var runtime = new CliptonRuntime(root, isSafeMode: true);
        var sentinelPath = Path.Combine(root, "snippets.dat");
        var sentinel = new byte[] { 1, 3, 3, 7 };
        File.WriteAllBytes(sentinelPath, sentinel);
        var importPath = Path.Combine(root, "history-import.json");
        File.WriteAllText(importPath, """
            {
              "Version": 1,
              "ExportedAt": "2026-07-10T00:00:00+00:00",
              "Items": [
                {
                  "Id": "..\\..\\snippets",
                  "CapturedAt": "2026-07-10T00:00:00+00:00",
                  "Formats": ["Text"],
                  "Text": "first imported value",
                  "FilePaths": []
                },
                {
                  "Id": "..\\..\\snippets",
                  "CapturedAt": "2026-07-10T00:01:00+00:00",
                  "Formats": ["Text"],
                  "Text": "second imported value",
                  "FilePaths": []
                }
              ]
            }
            """);

        Assert.Equal(2, runtime.ImportHistory(importPath));

        Assert.Equal(sentinel, File.ReadAllBytes(sentinelPath));
        Assert.Equal(2, runtime.History.Items.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.All(runtime.History.Items, item => Assert.True(Guid.TryParseExact(item.Id, "N", out _)));
        var persisted = new EncryptedHistoryStore(Path.Combine(root, "history.dat")).Load();
        Assert.Equal(2, persisted.Count);
        Assert.All(persisted, item => Assert.True(Guid.TryParseExact(item.Id, "N", out _)));
    }

    [Fact]
    public void ExportHistory_IncludesItemsThatAreNotResident()
    {
        var root = CreateTestRoot();
        Directory.CreateDirectory(root);
        var snapshots = Enumerable.Range(0, 25)
            .Select(index => TextSnapshot($"item-{index:D2}", $"value-{index:D2}"))
            .ToArray();
        new EncryptedHistoryStore(Path.Combine(root, "history.dat")).Save(snapshots);
        using var runtime = new CliptonRuntime(root, isSafeMode: true);
        var exportPath = Path.Combine(root, "history-export.json");

        Assert.Equal(10, runtime.History.Items.Count);
        Assert.Equal(25, runtime.ExportHistory(exportPath));

        using var document = JsonDocument.Parse(File.ReadAllBytes(exportPath));
        var items = document.RootElement.GetProperty("Items").EnumerateArray().ToArray();
        Assert.Equal(25, items.Length);
        Assert.Equal(
            snapshots.Select(item => item.Text!).OrderBy(value => value, StringComparer.Ordinal),
            items.Select(item => item.GetProperty("Text").GetString()!).OrderBy(value => value, StringComparer.Ordinal));
    }

    [Fact]
    public void ExportHistory_PrefersResidentUpdatesAndOmitsDeletedOrDuplicatePersistedItems()
    {
        var root = CreateTestRoot();
        Directory.CreateDirectory(root);
        var snapshots = Enumerable.Range(0, 20)
            .Select(index => TextSnapshot($"item-{index:D2}", $"value-{index:D2}"))
            .ToArray();
        new EncryptedHistoryStore(Path.Combine(root, "history.dat")).Save(snapshots);
        using var runtime = new CliptonRuntime(root, isSafeMode: true);
        runtime.RemoveHistoryItem("item-04");
        runtime.History.Add(TextSnapshot("replacement-for-15", "value-15"));
        runtime.History.Add(TextSnapshot("resident-new", "new value"));
        var exportPath = Path.Combine(root, "history-export.json");

        Assert.Equal(20, runtime.ExportHistory(exportPath));

        using var document = JsonDocument.Parse(File.ReadAllBytes(exportPath));
        var items = document.RootElement.GetProperty("Items").EnumerateArray().ToArray();
        var ids = items.Select(item => item.GetProperty("Id").GetString()).ToArray();
        Assert.DoesNotContain("item-04", ids);
        Assert.DoesNotContain("item-15", ids);
        Assert.Contains("replacement-for-15", ids);
        Assert.Contains("resident-new", ids);
        Assert.Equal(items.Length, items.Select(item => item.GetProperty("Text").GetString()).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void ExportHistory_WhenManifestIsLocked_RejectsResidentOnlyExport()
    {
        var root = CreateTestRoot();
        Directory.CreateDirectory(root);
        var snapshots = Enumerable.Range(0, 25)
            .Select(index => TextSnapshot($"item-{index:D2}", $"value-{index:D2}"))
            .ToArray();
        new EncryptedHistoryStore(Path.Combine(root, "history.dat")).Save(snapshots);
        using var runtime = new CliptonRuntime(root, isSafeMode: true);
        var exportPath = Path.Combine(root, "history-export.json");
        var manifestPath = Path.Combine(root, "history", "manifest.dat");

        using (File.Open(manifestPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.ThrowsAny<IOException>(() => runtime.ExportHistory(exportPath));
        }

        Assert.False(File.Exists(exportPath));
    }

    [Fact]
    public void ExportHistory_WhenPersistedPayloadIsMissing_RejectsPartialExport()
    {
        var root = CreateTestRoot();
        Directory.CreateDirectory(root);
        var snapshots = Enumerable.Range(0, 25)
            .Select(index => TextSnapshot($"item-{index:D2}", $"value-{index:D2}"))
            .ToArray();
        new EncryptedHistoryStore(Path.Combine(root, "history.dat")).Save(snapshots);
        using var runtime = new CliptonRuntime(root, isSafeMode: true);
        var exportPath = Path.Combine(root, "history-export.json");
        File.Delete(Path.Combine(root, "history", "items", "item-20.dat"));

        Assert.Throws<InvalidDataException>(() => runtime.ExportHistory(exportPath));

        Assert.False(File.Exists(exportPath));
    }

    [Fact]
    public void PutFileDrop_ReturnsFalseWhenNoRequestedFileExists()
    {
        var missingPath = Path.Combine(CreateTestRoot(), "deleted.txt");

        Assert.False(ClipboardBridge.PutFileDrop(missingPath));
    }

    [Fact]
    public void PasteTargetIdentity_RequiresMatchingHandleThreadAndProcess()
    {
        var identity = new PasteTargetWindowIdentity(new IntPtr(42), 7, 9);

        Assert.True(identity.Matches(new IntPtr(42), 7, 9));
        Assert.False(identity.Matches(new IntPtr(43), 7, 9));
        Assert.False(identity.Matches(new IntPtr(42), 8, 9));
        Assert.False(identity.Matches(new IntPtr(42), 7, 10));
        Assert.True(default(PasteTargetWindowIdentity).IsEmpty);
    }

    [Fact]
    public void Dispose_IsIdempotentAndPersistsFinalResidentHistory()
    {
        var root = CreateTestRoot();
        var runtime = new CliptonRuntime(root, isSafeMode: true);
        runtime.History.Add(TextSnapshot("final-item", "final value"));

        runtime.Dispose();
        runtime.Dispose();

        Assert.True(runtime.IsExiting);
        var persisted = Assert.Single(new EncryptedHistoryStore(Path.Combine(root, "history.dat")).Load());
        Assert.Equal("final-item", persisted.Id);
    }

    [Fact]
    public void HighContrastNotification_IsForwardedUntilRuntimeIsDisposed()
    {
        var root = CreateTestRoot();
        var runtime = new CliptonRuntime(root, isSafeMode: true);
        var notificationCount = 0;
        runtime.HighContrastChanged += (_, _) => notificationCount++;

        runtime.NotifyHighContrastChanged();
        runtime.Dispose();
        runtime.NotifyHighContrastChanged();

        Assert.Equal(1, notificationCount);
        Assert.False(runtime.IsHighContrast);
    }

    private static ClipboardSnapshot TextSnapshot(string id, string text)
    {
        return new ClipboardSnapshot(id, DateTimeOffset.UtcNow, [ClipboardFormatKind.Text], text: text);
    }

    private static string CreateTestRoot()
    {
        return Path.Combine(Path.GetTempPath(), "clipton-winui-runtime-safety-tests", Guid.NewGuid().ToString("N"));
    }
}
