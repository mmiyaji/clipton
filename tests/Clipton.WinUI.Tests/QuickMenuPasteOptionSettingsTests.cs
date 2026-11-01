using System.Reflection;
using Clipton.Core;
using Clipton.WinUI;

namespace Clipton.WinUI.Tests;

public sealed class QuickMenuPasteOptionSettingsTests
{
    [Fact]
    public void Defaults_KeepCommonActionsAndHideSpecializedActions()
    {
        var settings = new QuickMenuPasteOptionSettings();

        Assert.True(settings.IsEnabled(QuickMenuPasteOptionIds.PasteOriginal));
        Assert.True(settings.IsEnabled(QuickMenuPasteOptionIds.PastePlain));
        Assert.True(settings.IsEnabled(QuickMenuPasteOptionIds.EditAndPaste));
        Assert.False(settings.IsEnabled(QuickMenuPasteOptionIds.PasteUppercase));
        Assert.False(settings.IsEnabled(QuickMenuPasteOptionIds.PasteFormattedJson));
        Assert.False(settings.IsEnabled(QuickMenuPasteOptionIds.PasteImageResizeHalf));
    }

    [Fact]
    public void ResetQuickMenuPasteOptionsToDefaults_RestoresRecommendedVisibility()
    {
        using var runtime = new CliptonRuntime(CreateTestRoot(), isSafeMode: true);
        runtime.SetQuickMenuPasteOptionEnabled(QuickMenuPasteOptionIds.PasteUppercase, enabled: true);

        runtime.ResetQuickMenuPasteOptionsToDefaults();

        Assert.False(runtime.IsQuickMenuPasteOptionEnabled(QuickMenuPasteOptionIds.PasteUppercase));
        Assert.True(runtime.IsQuickMenuPasteOptionEnabled(QuickMenuPasteOptionIds.PastePlain));
    }

    [Fact]
    public void CreateHistoryContextOptions_HidesDisabledTextOptions()
    {
        using var runtime = new CliptonRuntime(CreateTestRoot(), isSafeMode: true);
        runtime.History.Add(new ClipboardSnapshot(
            "text-1",
            DateTimeOffset.UtcNow,
            [ClipboardFormatKind.Text],
            text: """{"url":"https://example.com","name":"Clipton"}"""));
        runtime.SetQuickMenuPasteOptionEnabled(QuickMenuPasteOptionIds.PasteLowercase, enabled: false);
        runtime.SetQuickMenuPasteOptionEnabled(QuickMenuPasteOptionIds.PasteFormattedJson, enabled: false);
        runtime.SetQuickMenuPasteOptionEnabled(QuickMenuPasteOptionIds.TogglePin, enabled: false);
        runtime.SetQuickMenuPasteOptionEnabled(QuickMenuPasteOptionIds.PasteExtractUrls, enabled: true);

        var optionIds = runtime.CreateHistoryContextOptions("text-1")
            .Select(option => option.Id)
            .ToArray();

        Assert.Contains(QuickMenuPasteOptionIds.PastePlain, optionIds);
        Assert.Contains(QuickMenuPasteOptionIds.PasteExtractUrls, optionIds);
        Assert.DoesNotContain(QuickMenuPasteOptionIds.PasteLowercase, optionIds);
        Assert.DoesNotContain(QuickMenuPasteOptionIds.PasteFormattedJson, optionIds);
        Assert.DoesNotContain(QuickMenuPasteOptionIds.TogglePin, optionIds);
        Assert.Contains(QuickMenuPasteOptionIds.RegisterAsSnippet, optionIds);
    }

    [Fact]
    public void HistoryRegistration_UsesRequestedHistoryIdAndRequiresPlainText()
    {
        using var runtime = new CliptonRuntime(CreateTestRoot(), isSafeMode: true);
        runtime.History.Add(new ClipboardSnapshot("first", DateTimeOffset.UtcNow,
            [ClipboardFormatKind.Text], text: "first payload"));
        runtime.History.Add(new ClipboardSnapshot("second", DateTimeOffset.UtcNow,
            [ClipboardFormatKind.Text], text: "second payload"));
        runtime.History.Add(new ClipboardSnapshot("empty", DateTimeOffset.UtcNow,
            [ClipboardFormatKind.Text], text: "  \r\n"));
        runtime.History.Add(new ClipboardSnapshot("image", DateTimeOffset.UtcNow,
            [ClipboardFormatKind.Image], imagePng: [1, 2, 3]));
        runtime.History.Add(new ClipboardSnapshot("mixed", DateTimeOffset.UtcNow,
            [ClipboardFormatKind.Text, ClipboardFormatKind.Image], text: "mixed payload", imagePng: [1, 2, 3]));

        Assert.Equal("first payload", runtime.GetHistoryPlainText("first"));
        Assert.Equal("second payload", runtime.GetHistoryPlainText("second"));
        Assert.Null(runtime.GetHistoryPlainText("empty"));
        Assert.Null(runtime.GetHistoryPlainText("image"));
        Assert.Contains(QuickMenuPasteOptionIds.RegisterAsSnippet,
            runtime.CreateHistoryContextOptions("mixed").Select(option => option.Id));
        Assert.DoesNotContain(QuickMenuPasteOptionIds.RegisterAsSnippet,
            runtime.CreateHistoryContextOptions("image").Select(option => option.Id));
    }

    [Fact]
    public void QuickMenuHistoryOptions_KeepSnippetRegistrationAvailableWhenPasteOptionsAreDisabled()
    {
        using var runtime = new CliptonRuntime(CreateTestRoot(), isSafeMode: true);
        runtime.History.Add(new ClipboardSnapshot("history", DateTimeOffset.UtcNow,
            [ClipboardFormatKind.Text], text: "snippet payload"));
        runtime.Settings.QuickMenuPasteOptions.DisabledOptionIds = QuickMenuPasteOptionIds.All.ToArray();

        var historyItem = BuildQuickMenuItems(runtime).Single(item => item.Title?.Contains("snippet payload") == true);

        Assert.Contains(QuickMenuPasteOptionIds.RegisterAsSnippet,
            historyItem.GetPasteOptions().Select(option => option.Id));
    }

    [Fact]
    public void HistoryRegistration_ResolvesPersistedPayloadOutsideResidentHistory()
    {
        var root = CreateTestRoot();
        new JsonSettingsStore(Path.Combine(root, "settings.json")).Save(new CliptonSettings
        {
            PersistEncryptedHistory = true,
            HistoryPersistenceConfigured = true,
            MaxHistoryItems = 100,
            InitialLaunchCompleted = true,
            PauseCapture = true,
            Locale = "en"
        });
        new EncryptedHistoryStore(Path.Combine(root, "history.dat")).Save(Enumerable.Range(0, 11).Select(i =>
            new ClipboardSnapshot($"item-{i}", DateTimeOffset.UtcNow.AddSeconds(-i),
                [ClipboardFormatKind.Text], text: $"payload {i}")));

        using var runtime = new CliptonRuntime(root, isSafeMode: true);

        Assert.Equal("payload 10", runtime.GetHistoryPlainText("item-10"));
        Assert.Contains(QuickMenuPasteOptionIds.RegisterAsSnippet,
            runtime.CreateHistoryContextOptions("item-10").Select(option => option.Id));
    }

    [Fact]
    public void BuildQuickMenuItems_GroupsNestedSnippets()
    {
        using var runtime = new CliptonRuntime(CreateTestRoot(), isSafeMode: true);
        runtime.Snippets.Upsert(new Snippet("Root", "root text"));
        runtime.Snippets.Upsert(new Snippet("FolderRoot", "folder root text", "folder"));
        runtime.Snippets.Upsert(new Snippet("Nested", "nested text", "folder/child"));

        var items = BuildQuickMenuItems(runtime);

        Assert.Contains(items, item => item.Title == "Root");
        var folder = Assert.Single(items, item => item.Title == "folder");
        var folderChildren = folder.GetChildren();
        Assert.Contains(folderChildren, item => item.Title == "FolderRoot");
        var child = Assert.Single(folderChildren, item => item.Title == "child");
        Assert.Contains(child.GetChildren(), item => item.Title == "Nested");
    }

    [Fact]
    public void BuildQuickMenuItems_IncludesNewSnippetCommandWhenEmpty()
    {
        using var runtime = new CliptonRuntime(CreateTestRoot(), isSafeMode: true);

        var items = BuildQuickMenuItems(runtime);

        Assert.Contains(items, item =>
            item.Title == runtime.Translate("NewSnippet")
            && item.Subtitle == runtime.Translate("Snippets")
            && item.KindLabel == "+");
    }

    [Fact]
    public void QuickMenuItem_CachesLazyPasteOptions()
    {
        var factoryCalls = 0;
        var item = new QuickMenuItem(
            "Item",
            "Text",
            "T",
            "Enter",
            () => { },
            LazyPasteOptions: () =>
            {
                factoryCalls++;
                return [new QuickMenuPasteOption("Paste", "P", () => { })];
            });

        Assert.True(item.HasPasteOptions);
        Assert.Equal(0, factoryCalls);
        Assert.Single(item.GetPasteOptions());
        Assert.Single(item.GetPasteOptions());
        Assert.Equal(1, factoryCalls);
    }

    private static string CreateTestRoot()
    {
        return Path.Combine(Path.GetTempPath(), "clipton-winui-option-tests", Guid.NewGuid().ToString("N"));
    }

    private static IReadOnlyList<QuickMenuItem> BuildQuickMenuItems(CliptonRuntime runtime)
    {
        var method = typeof(CliptonRuntime).GetMethod("BuildQuickMenuItems", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("BuildQuickMenuItems was not found.");
        return (IReadOnlyList<QuickMenuItem>)method.Invoke(runtime, null)!;
    }
}
