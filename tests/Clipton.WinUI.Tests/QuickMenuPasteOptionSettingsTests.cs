using System.Reflection;
using Clipton.Core;
using Clipton.WinUI;

namespace Clipton.WinUI.Tests;

public sealed class QuickMenuPasteOptionSettingsTests
{
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

        var optionIds = runtime.CreateHistoryContextOptions("text-1")
            .Select(option => option.Id)
            .ToArray();

        Assert.Contains(QuickMenuPasteOptionIds.PastePlain, optionIds);
        Assert.Contains(QuickMenuPasteOptionIds.PasteExtractUrls, optionIds);
        Assert.DoesNotContain(QuickMenuPasteOptionIds.PasteLowercase, optionIds);
        Assert.DoesNotContain(QuickMenuPasteOptionIds.PasteFormattedJson, optionIds);
        Assert.DoesNotContain(QuickMenuPasteOptionIds.TogglePin, optionIds);
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
