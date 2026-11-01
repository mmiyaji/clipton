using System.Reflection;
using Clipton.Core;
using Clipton.WinUI;

namespace Clipton.WinUI.Tests;

public sealed class LargeHistoryMenuTests
{
    [Fact]
    public void Search_FindsLongTextTailWithoutLoadingHistoryPrefixOrKeepingLongTitle()
    {
        var root = Seed(200, i => i == 199 ? new string('x', 65536) + " tailneedle" : $"entry {i}");
        using var runtime = new CliptonRuntime(root, isSafeMode: true);
        var result = Assert.Single(runtime.SearchQuickMenuItems("tailneedle", default));
        Assert.True(result.Title.Length <= 513);
        Assert.True(result.RevealedTitle is null || result.RevealedTitle.Length <= 513);
        Assert.Contains("tailneedle", result.SearchText);
        Assert.Equal(10, runtime.History.Items.Count);
        Assert.Single(runtime.SearchQuickMenuItems("tailneedle", default));
        Assert.Equal(10, runtime.History.Items.Count);
    }

    [Fact]
    public void LastFolder_LoadsOnlyRequestedRangeAndKeepsOlderItemsAfterSave()
    {
        var root = Seed(200);
        using (var runtime = new CliptonRuntime(root, isSafeMode: true))
        {
            var last = Build(runtime).Single(item => item.Title == "101+").GetChildren().Last().GetChildren();
            Assert.Equal(50, last.Count);
            Assert.Contains("entry 199", last.Last().Title);
            Assert.Equal(10, runtime.History.Items.Count);
            Assert.NotEmpty(runtime.CreateHistoryContextOptions("item-199"));
        }
        Assert.Equal(200, new EncryptedHistoryStore(Path.Combine(root, "history.dat")).Count());
    }

    [Fact]
    public void Top50_CoversAllDirectItemsAndSearchIncludesItem11()
    {
        var root = Seed(200);
        using var runtime = new CliptonRuntime(root, isSafeMode: true);
        runtime.Settings.QuickMenuTopLevelHistoryItems = 50;
        Assert.Equal(50, Build(runtime).Count(item => item.IsNumberShortcutEnabled));
        Assert.Single(runtime.SearchQuickMenuItems("\"entry 11\"", default), item => item.Title == "entry 11");
        Assert.Equal(60, runtime.SearchQuickMenuItems("entry", default).Count);
    }

    [Fact]
    public void BuildQuickMenuItems_KeepsLargePinnedHistoryLazyAndPreservesUnpinnedOrder()
    {
        var root = Seed(1000);
        using var runtime = new CliptonRuntime(root, isSafeMode: true);
        runtime.Settings.PinnedHistoryIds = Enumerable.Range(0, 150).Select(i => $"item-{i}")
            .Concat(Enumerable.Range(950, 50).Select(i => $"item-{i}")).ToArray();
        SaveSettings(runtime);

        var items = Build(runtime);

        Assert.Equal(10, runtime.History.Items.Count);
        Assert.Equal(new[] { "entry 150", "entry 151", "entry 152", "entry 153", "entry 154" },
            items.Where(item => item.IsNumberShortcutEnabled).Select(item => item.Title));
        var folder = Assert.Single(items, item => item.Title == "Pinned");
        var pinned = folder.GetChildren();
        Assert.Equal(200, pinned.Count);
        Assert.Equal("entry 0", pinned[0].Title);
        Assert.Equal("entry 999", pinned[^1].Title);
        Assert.NotEmpty(runtime.CreateHistoryContextOptions("item-950"));

        var firstOlderRange = Assert.Single(items, item => item.Title == "6-50").GetChildren();
        Assert.Equal(45, firstOlderRange.Count);
        Assert.Equal("entry 155", firstOlderRange[0].Title);
        Assert.Equal("entry 199", firstOlderRange[^1].Title);

        runtime.TogglePinnedHistoryItem("item-0");
        var afterUnpin = Build(runtime);
        Assert.Equal(new[] { "entry 0", "entry 150", "entry 151", "entry 152", "entry 153" },
            afterUnpin.Where(item => item.IsNumberShortcutEnabled).Select(item => item.Title));
        Assert.Equal(199, Assert.Single(afterUnpin, item => item.Title == "Pinned").GetChildren().Count);
        typeof(CliptonRuntime).GetMethod("SaveHistory", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(runtime, [true]);
        Assert.Equal(1000, new EncryptedHistoryStore(Path.Combine(root, "history.dat")).Count());
        Assert.Equal(10, runtime.History.Items.Count);
    }

    [Fact]
    public void Search_SeesNewCapturePinAndDeletionWithoutStaleCachedResults()
    {
        var root = Seed(200);
        using (var runtime = new CliptonRuntime(root, isSafeMode: true))
        {
            runtime.TogglePinnedHistoryItem("item-199");
            Assert.Single(runtime.SearchQuickMenuItems("pinned:true type:text", default));
            Assert.Single(Build(runtime).Single(item => item.Title == "Pinned").GetChildren());
            runtime.RemoveHistoryItem("item-199");
            Assert.Empty(runtime.SearchQuickMenuItems("\"entry 199\"", default));
            runtime.History.Add(new ClipboardSnapshot("new", DateTimeOffset.UtcNow, [ClipboardFormatKind.Text], text: "freshneedle"));
            Assert.Single(runtime.SearchQuickMenuItems("freshneedle", default));
        }
        var store = new EncryptedHistoryStore(Path.Combine(root, "history.dat"));
        Assert.DoesNotContain("item-199", store.GetOrderedIds());
        Assert.Contains("new", store.GetOrderedIds());
    }

    [Fact]
    public void Search_AfterSavingResidentDeletionDoesNotSkipNextUnloadedItem()
    {
        var root = Seed(200);
        using var runtime = new CliptonRuntime(root, isSafeMode: true);
        runtime.RemoveHistoryItem("item-0");
        typeof(CliptonRuntime).GetMethod("SaveHistory", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(runtime, [true]);
        Assert.Single(runtime.SearchQuickMenuItems("\"entry 10\"", default), item => item.Title == "entry 10");
        Assert.Equal(199, runtime.AvailableHistoryCount);
    }

    [Fact]
    public void Search_CombinesBodySourceAndFiltersAndPreservesSnippetLabels()
    {
        var root = Seed(20);
        using var runtime = new CliptonRuntime(root, isSafeMode: true);
        runtime.Settings.SaveHistorySourceMetadata = true;
        runtime.History.Add(new ClipboardSnapshot("url", DateTimeOffset.UtcNow, [ClipboardFormatKind.Text],
            text: "https://example.com deepneedle", sourceApplicationName: "SourceApp"));
        runtime.TogglePinnedHistoryItem("url");
        Assert.Single(runtime.SearchQuickMenuItems("deepneedle SourceApp type:text url:true pinned:true", default));
        Assert.Empty(runtime.SearchQuickMenuItems("deepneedle url:false", default));
        runtime.UpsertSnippet("", "SnippetLabel", "entry 19");
        Assert.Contains(runtime.SearchQuickMenuItems("SnippetLabel", default), item => item.IsNumberShortcutEnabled && item.Title == "SnippetLabel");
    }

    [Fact]
    public void Search_CancelledQueryDoesNotMaterializeOldHistory()
    {
        var root = Seed(200);
        using var runtime = new CliptonRuntime(root, isSafeMode: true);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => runtime.SearchQuickMenuItems("missing", cancellation.Token));
        Assert.Equal(10, runtime.History.Items.Count);
    }

    [Fact]
    public void MenuCache_OmitsImageButRetainsOriginalPayloadAndRepairsCorruption()
    {
        var root = Path.Combine(Path.GetTempPath(), "clipton-large-history-tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "history.dat");
        var store = new EncryptedHistoryStore(path);
        byte[] bytes = [1, 2, 3, 4];
        store.Save([new ClipboardSnapshot("image", DateTimeOffset.UtcNow, [ClipboardFormatKind.Text, ClipboardFormatKind.Image], text: "needle", imagePng: bytes)]);
        Assert.Null(new EncryptedHistoryStore(path).LoadMenuSnapshot("image")!.ImagePng);
        Assert.Equal(bytes, store.LoadItem("image")!.ImagePng);
        File.WriteAllBytes(Path.Combine(root, "history", "menu", "image.dat"), [0]);
        Assert.Equal("needle", new EncryptedHistoryStore(path).LoadMenuSnapshot("image")!.Text);
        store.Delete();
        Assert.False(Directory.Exists(Path.Combine(root, "history")));
        Assert.Null(store.LoadMenuSnapshot("image"));
    }

    [Fact]
    public void MenuCache_IsInvalidatedWhenSourceMetadataIsRemoved()
    {
        var root = Path.Combine(Path.GetTempPath(), "clipton-large-history-tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "history.dat");
        var store = new EncryptedHistoryStore(path);
        store.Save([new ClipboardSnapshot("text", DateTimeOffset.UtcNow, [ClipboardFormatKind.Text], text: "needle", sourceWindowTitle: "private-window")]);
        Assert.Equal("private-window", store.LoadMenuSnapshot("text")!.SourceWindowTitle);
        store.ClearSourceMetadata();
        Assert.Null(store.LoadMenuSnapshot("text")!.SourceWindowTitle);
        Assert.Null(new EncryptedHistoryStore(path).LoadMenuSnapshot("text")!.SourceWindowTitle);
    }

    [Fact]
    public void PrepareMenuCache_WarmsCompressedEntryWithoutReadingPayloadOnQuery()
    {
        var root = Path.Combine(Path.GetTempPath(), "clipton-large-history-tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "history.dat");
        var writer = new EncryptedHistoryStore(path);
        var text = new string('x', 256 * 1024) + " warmed-tail";
        byte[] image = [1, 2, 3, 4];
        writer.Save([new ClipboardSnapshot("mixed", DateTimeOffset.UtcNow,
            [ClipboardFormatKind.Text, ClipboardFormatKind.Image], text: text, imagePng: image)]);

        // A new instance has no in-memory entry. Preparation must populate it from the
        // compressed sidecar so a later menu query does not need the payload file.
        var store = new EncryptedHistoryStore(path);
        store.PrepareMenuCache(default);
        var payloadPath = Path.Combine(root, "history", "items", "mixed.dat");
        var cachePath = Path.Combine(root, "history", "menu", "mixed.dat");
        using var payloadLock = File.Open(payloadPath, FileMode.Open, FileAccess.Read, FileShare.None);
        using var cacheLock = File.Open(cachePath, FileMode.Open, FileAccess.Read, FileShare.None);

        var snapshot = store.LoadMenuSnapshot("mixed");

        Assert.NotNull(snapshot);
        Assert.Equal(text, snapshot.Text);
        Assert.Null(snapshot.ImagePng);
    }

    private static string Seed(int count, Func<int, string>? text = null)
    {
        var root = Path.Combine(Path.GetTempPath(), "clipton-large-history-tests", Guid.NewGuid().ToString("N"));
        new JsonSettingsStore(Path.Combine(root, "settings.json")).Save(new CliptonSettings
        {
            PersistEncryptedHistory = true, HistoryPersistenceConfigured = true, MaxHistoryItems = 1000,
            InitialLaunchCompleted = true, PauseCapture = true, Locale = "en", QuickMenuTopLevelHistoryItems = 5
        });
        new EncryptedHistoryStore(Path.Combine(root, "history.dat")).Save(Enumerable.Range(0, count).Select(i =>
            new ClipboardSnapshot($"item-{i}", DateTimeOffset.UtcNow.AddSeconds(-i), [ClipboardFormatKind.Text], text: text?.Invoke(i) ?? $"entry {i}")));
        return root;
    }

    private static IReadOnlyList<QuickMenuItem> Build(CliptonRuntime runtime) =>
        (IReadOnlyList<QuickMenuItem>)typeof(CliptonRuntime).GetMethod("BuildQuickMenuItems", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(runtime, null)!;

    private static void SaveSettings(CliptonRuntime runtime) =>
        typeof(CliptonRuntime).GetMethod("SaveSettings", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(runtime, null);
}
