using System.Windows.Media;

namespace Clipton.App.Tests;

public sealed class QuickMenuNavigatorTests
{
    [Fact]
    public void EmptyMenu_HasNoSelectionAndNavigationCommandsReturnFalse()
    {
        var navigator = new QuickMenuNavigator("Empty", []);

        Assert.Equal(-1, navigator.SelectedIndex);
        Assert.Null(navigator.SelectedItem);
        Assert.False(navigator.MoveSelection(1));
        Assert.False(navigator.OpenSelectedFolder());
        Assert.False(navigator.NavigateBack());
    }

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
    public void MoveSelection_WhenAllItemsAreDisabled_ReturnsFalseAndLeavesNoSelection()
    {
        var first = CreateItem("First", isEnabled: false);
        var second = CreateItem("Second", isEnabled: false);
        var navigator = new QuickMenuNavigator("Root", [first, second]);

        Assert.Equal(-1, navigator.SelectedIndex);
        Assert.Null(navigator.SelectedItem);

        Assert.False(navigator.MoveSelection(1));
        Assert.Equal(-1, navigator.SelectedIndex);
        Assert.Null(navigator.SelectedItem);
    }

    [Fact]
    public void Select_IgnoresOutOfRangeAndDisabledIndexes()
    {
        var first = CreateItem("First");
        var disabled = CreateItem("Disabled", isEnabled: false);
        var second = CreateItem("Second");
        var navigator = new QuickMenuNavigator("Root", [first, disabled, second]);

        navigator.Select(-1);
        Assert.Equal(0, navigator.SelectedIndex);

        navigator.Select(3);
        Assert.Equal(0, navigator.SelectedIndex);

        navigator.Select(1);
        Assert.Equal(0, navigator.SelectedIndex);

        navigator.Select(2);
        Assert.Equal("Second", navigator.SelectedItem?.Title);
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
    public void OpenSelectedFolder_WhenSelectedItemHasNoChildren_ReturnsFalseAndPreservesState()
    {
        var leaf = CreateItem("Leaf");
        var sibling = CreateItem("Sibling");
        var navigator = new QuickMenuNavigator("Root", [leaf, sibling]);

        Assert.False(navigator.OpenSelectedFolder());

        Assert.Equal("Root", navigator.Title);
        Assert.Equal(2, navigator.Items.Count);
        Assert.Equal("Leaf", navigator.SelectedItem?.Title);
    }

    [Fact]
    public void OpenSelectedFolder_WhenChildrenAreAllDisabled_OpensWithNoSelection()
    {
        var disabledChild = CreateItem("Disabled child", isEnabled: false);
        var folder = new QuickMenuItem("Folder", "", ">", "Enter", Brushes.DimGray, () => { }, Children: [disabledChild]);
        var navigator = new QuickMenuNavigator("Root", [folder]);

        Assert.True(navigator.OpenSelectedFolder());

        Assert.Equal("Folder", navigator.Title);
        Assert.Equal(-1, navigator.SelectedIndex);
        Assert.Null(navigator.SelectedItem);
        Assert.False(navigator.MoveSelection(1));
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

    [Fact]
    public void SelectedItem_WhenBackingListShrinksPastSelection_ReturnsNull()
    {
        var items = new List<QuickMenuItem> { CreateItem("Only") };
        var navigator = new QuickMenuNavigator("Root", items);

        items.Clear();

        Assert.Equal(0, navigator.SelectedIndex);
        Assert.Null(navigator.SelectedItem);
    }

    private static QuickMenuItem CreateItem(string title, bool isEnabled = true)
    {
        return new QuickMenuItem(title, "", "T", "Enter", Brushes.SteelBlue, () => { }, IsEnabled: isEnabled);
    }
}
