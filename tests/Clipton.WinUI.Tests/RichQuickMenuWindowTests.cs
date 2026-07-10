using Clipton.WinUI;

namespace Clipton.WinUI.Tests;

public sealed class RichQuickMenuWindowTests
{
    [Theory]
    [InlineData("T", true)]
    [InlineData("R", true)]
    [InlineData("S", true)]
    [InlineData("I", false)]
    [InlineData("F", false)]
    public void MatchesTextFilter_UsesQuickMenuKindLabels(string kindLabel, bool expected)
    {
        var item = CreateItem(kindLabel);

        Assert.Equal(expected, RichQuickMenuWindow.MatchesTextFilter(item));
    }

    [Fact]
    public void MatchesTextFilter_KeepsFoldersAvailableForNavigation()
    {
        var folder = new QuickMenuItem(
            "Folder",
            string.Empty,
            string.Empty,
            string.Empty,
            () => { },
            LazyChildren: () => [CreateItem("T")]);

        Assert.True(RichQuickMenuWindow.MatchesTextFilter(folder));
    }

    private static QuickMenuItem CreateItem(string kindLabel)
    {
        return new QuickMenuItem("Item", string.Empty, kindLabel, string.Empty, () => { });
    }
}
