using System.Text.Json;
using Clipton.Core;
using Clipton.WinUI;

namespace Clipton.WinUI.Tests;

public sealed class RuntimeSafetyRegressionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ImportHistory_PreviewIncludesUnloadedMatchesAndCapacityEvictions(bool encrypted)
    {
        var root = CreateTestRoot();
        Directory.CreateDirectory(root);
        new JsonSettingsStore(Path.Combine(root, "settings.json")).Save(new CliptonSettings { MaxHistoryItems = 30 });
        var store = new EncryptedHistoryStore(Path.Combine(root, "history.dat"));
        store.Save(Enumerable.Range(0, 30).Select(index => TextSnapshot($"item-{index}", $"value-{index}")));
        using var runtime = new CliptonRuntime(root, isSafeMode: true);
        var path = Path.Combine(root, encrypted ? "import.clipton" : "import.json");
        const string password = "test-password";
        var dto = new
        {
            Version = 1,
            Items = new[] { "value-20", "new value", "new value", "value-0", "another new value" }
                .Select((value, index) => new
                {
                    CapturedAt = DateTimeOffset.UnixEpoch.AddSeconds(index),
                    Formats = new[] { "Text" },
                    Text = value,
                    FilePaths = Array.Empty<string>()
                }).ToArray()
        };
        if (encrypted)
        {
            EncryptedExportFile.Write(path, "history", dto, password, new JsonSerializerOptions());
        }
        else
        {
            File.WriteAllBytes(path, JsonSerializer.SerializeToUtf8Bytes(dto));
        }

        Assert.Equal(10, runtime.History.Items.Count);
        var preview = encrypted ? runtime.PreviewImportHistoryEncrypted(path, password) : runtime.PreviewImportHistory(path);
        Assert.Equal(new HistoryImportPreview(5, 4, 2, 2, 30), preview);
        Assert.Equal(10, runtime.History.Items.Count);
        Assert.Equal(4, encrypted ? runtime.ImportHistoryEncrypted(path, password) : runtime.ImportHistory(path));

        var saved = store.LoadAllStrict();
        Assert.Equal(30, saved.Count);
        Assert.Equal(new[] { "value-20", "new value", "value-0", "another new value", "value-1" },
            saved.Take(5).Select(item => item.Text));
        Assert.Equal(DateTimeOffset.UnixEpoch.AddSeconds(1), saved[1].CapturedAt);
        Assert.Equal("value-27", saved[^1].Text);
        Assert.Equal(30 - runtime.History.Items.Count, runtime.UnloadedPersistedHistoryCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExportSizeLimit_UsesFinalFileLengthAndPreservesExistingDestination(bool encrypted)
    {
        var root = CreateTestRoot();
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, encrypted ? "backup.clipton" : "backup.json");
        const string password = "test-password";
        const string data = "backup content";
        var options = new JsonSerializerOptions();
        byte[] sentinel = [1, 3, 3, 7];
        File.WriteAllBytes(path, sentinel);

        void Write(long maxBytes)
        {
            if (encrypted)
            {
                EncryptedExportFile.Write(path, "history", data, password, options, maxBytes);
            }
            else
            {
                EncryptedExportFile.WriteBounded(path, data, options, maxBytes);
            }
        }

        // The encrypted payload's Base64 fits 64 bytes, but its full envelope exceeds that limit.
        Assert.Throws<InvalidOperationException>(() => Write(encrypted ? 64 : 15));
        Assert.Equal(sentinel, File.ReadAllBytes(path));
        Assert.Single(Directory.EnumerateFiles(root));

        Write(encrypted ? 1024 : 16);
        Assert.Equal(data, encrypted
            ? EncryptedExportFile.Read<string>(path, "history", password, options)
            : JsonSerializer.Deserialize<string>(File.ReadAllBytes(path), options));
    }

    [Fact]
    public async Task ImportHistory_ReconfirmsChangedEvictionCountAndKeepsSelectedBackup()
    {
        var root = CreateTestRoot();
        using var runtime = new CliptonRuntime(root, isSafeMode: true);
        runtime.SetMaxHistoryItems(2);
        runtime.History.Add(TextSnapshot("first", "first"));
        var path = Path.Combine(root, "backup.json");
        File.WriteAllText(path, """{"Items":[{"Text":"imported"}]}""");
        var confirmations = new List<int>();

        var count = await runtime.ImportHistoryWithConfirmationAsync(path, null, preview =>
        {
            confirmations.Add(preview.RemovedByCapacityItems);
            if (confirmations.Count == 1)
            {
                runtime.History.Add(TextSnapshot("second", "second"));
                File.WriteAllText(path, """{"Items":[{"Text":"different backup"}]}""");
            }

            return Task.FromResult(true);
        });

        Assert.Equal(1, count);
        Assert.Equal([0, 1], confirmations);
        Assert.Equal(["imported", "second"], runtime.History.Items.Select(item => item.Text));
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("Vault.exe", false)]
    [InlineData("editor.exe", true)]
    public void CaptureWithExclusionsRequiresKnownAllowedOwner(string? owner, bool allowed)
    {
        Assert.Equal(allowed, CliptonRuntime.CanCaptureFrom(["Vault*"], owner));
        Assert.True(CliptonRuntime.CanCaptureFrom([], owner));
    }

    [Theory]
    [InlineData(42u, 42u, true)]
    [InlineData(42u, 43u, false)]
    [InlineData(42u, 0u, false)]
    [InlineData(0u, 0u, false)]
    public void PasteRequiresConfirmedPublishedSequence(uint published, uint current, bool allowed)
    {
        Assert.Equal(allowed, CliptonRuntime.IsPublishedClipboardCurrent(published, current));
    }

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
    public void Dispose_ReleasesScheduledQuickMenuWarmupCancellationOnce()
    {
        var runtime = new CliptonRuntime(CreateTestRoot(), isSafeMode: true);
        var scheduledCancellation = new CancellationTokenSource();
        var scheduledToken = scheduledCancellation.Token;
        typeof(CliptonRuntime)
            .GetField("_quickMenuWarmupScheduleCancellation", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(runtime, scheduledCancellation);

        var exception = Record.Exception(runtime.Dispose);

        Assert.Null(exception);
        Assert.True(runtime.IsExiting);
        Assert.True(scheduledToken.IsCancellationRequested);
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
