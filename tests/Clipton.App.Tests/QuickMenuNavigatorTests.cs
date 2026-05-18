using System.Windows.Media;

namespace Clipton.App.Tests;

public sealed class QuickMenuNavigatorTests
{
    [Fact]
    public void MoveSelection_CyclesAndSkipsDisabledItems()
    {
        var disabled = new QuickMenuItem("Disabled", "", "-", "", Brushes.Gray, () => { }, IsEnabled: false);
        var first = new QuickMenuItem("First", "", "T", "Enter", Brushes.SteelBlue, () => { });
        var second = new QuickMenuItem("Second", "", "T", "Enter", Brushes.SteelBlue, () => { });
        var navigator = new QuickMenuNavigator("Root", [disabled, first, second]);

        Assert.Equal(1, navigator.SelectedIndex);

        navigator.MoveSelection(-1);
        Assert.Equal("Second", navigator.SelectedItem?.Title);

        navigator.MoveSelection(1);
        Assert.Equal("First", navigator.SelectedItem?.Title);
    }

    [Fact]
    public void OpenSelectedFolderAndNavigateBack_RestoresParentSelection()
    {
        var child = new QuickMenuItem("Child", "", "T", "Enter", Brushes.SteelBlue, () => { });
        var folder = new QuickMenuItem("Folder", "", ">", "Enter", Brushes.DimGray, () => { }, Children: [child]);
        var sibling = new QuickMenuItem("Sibling", "", "T", "Enter", Brushes.SteelBlue, () => { });
        var navigator = new QuickMenuNavigator("Root", [sibling, folder]);

        navigator.Select(1);
        Assert.True(navigator.OpenSelectedFolder());
        Assert.Equal("Folder", navigator.Title);
        Assert.Equal("Child", navigator.SelectedItem?.Title);

        Assert.True(navigator.NavigateBack());
        Assert.Equal("Root", navigator.Title);
        Assert.Equal("Folder", navigator.SelectedItem?.Title);
    }

    [Fact]
    public void OpenSelectedFolder_ResolvesLazyChildren()
    {
        var calls = 0;
        var folder = new QuickMenuItem(
            "Lazy",
            "",
            ">",
            "Enter",
            Brushes.DimGray,
            () => { },
            LazyChildren: () =>
            {
                calls++;
                return [new QuickMenuItem("Child", "", "T", "Enter", Brushes.SteelBlue, () => { })];
            });
        var navigator = new QuickMenuNavigator("Root", [folder]);

        Assert.True(navigator.OpenSelectedFolder());
        Assert.Equal("Child", navigator.SelectedItem?.Title);
        Assert.Equal(1, calls);
    }
}
