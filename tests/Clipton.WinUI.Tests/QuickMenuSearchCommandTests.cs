using Clipton.Core;

namespace Clipton.WinUI.Tests;

public sealed class QuickMenuSearchCommandTests
{
    [Theory]
    [InlineData("en", "Search history", 3, "Ctrl+S", false)]
    [InlineData("ja", "\u5C65\u6B74\u3092\u691C\u7D22", 8, "Ctrl+F", true)]
    public void DefaultMenu_SearchCommandFollowsHistory(string locale, string label, int historyCount, string shortcut, bool includesFolders)
    {
        using var runtime = CreateRuntime();
        runtime.Settings.Locale = locale;
        runtime.Settings.QuickMenuShortcuts.Search = shortcut;
        for (var index = 0; index < historyCount; index++)
        {
            runtime.History.Add(new ClipboardSnapshot($"history-{index}", DateTimeOffset.UtcNow.AddSeconds(index),
                [ClipboardFormatKind.Text], text: $"payload {index}"));
        }

        runtime.Settings.PinnedHistoryIds = includesFolders ? ["history-0"] : [];
        runtime.Snippets.Upsert(new Snippet("Test snippet", "snippet payload"));

        var items = runtime.BuildQuickMenuItemsForDisplay(includeSearchCommand: true);
        var search = Assert.Single(items, item => item.Title == label);
        var searchIndex = items.IndexOf(search);

        Assert.Equal(includesFolders ? 7 : historyCount, searchIndex);
        Assert.Equal($"payload {historyCount - 1}", items[0].Title);
        Assert.True(items[0].IsNumberShortcutEnabled);
        Assert.Equal(includesFolders, items[searchIndex - 1].IsFolder);
        if (includesFolders)
        {
            Assert.Equal("6-7", items[searchIndex - 2].Title);
            Assert.Equal(runtime.Translate("PinnedHistory"), items[searchIndex - 1].Title);
        }

        Assert.True(items[searchIndex + 1].IsSeparator);
        Assert.Equal("Test snippet", items[searchIndex + 2].Title);
        Assert.Equal(shortcut, search.KeyboardShortcut);
        Assert.False(search.IsNumberShortcutEnabled);
        Assert.False(search.IsFolder);
        Assert.DoesNotContain(runtime.BuildQuickMenuItemsForDisplay(includeSearchCommand: false), item => item.Title == label);
        Assert.DoesNotContain(runtime.SearchQuickMenuItems(string.Empty, CancellationToken.None), item => item.Title == label);
    }

    [Fact]
    public void DefaultMenu_OmitsSearchCommandWithoutHistory()
    {
        using var runtime = CreateRuntime();
        runtime.Snippets.Upsert(new Snippet("Test snippet", "snippet payload"));

        Assert.DoesNotContain(runtime.BuildQuickMenuItemsForDisplay(includeSearchCommand: true),
            item => item.Title == runtime.Translate("SearchHistory"));
    }

    private static CliptonRuntime CreateRuntime() =>
        new(Path.Combine(Path.GetTempPath(), "clipton-winui-search-command-tests", Guid.NewGuid().ToString("N")),
            isSafeMode: true);
}
