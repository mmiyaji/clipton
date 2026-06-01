using System.Windows;
using System.Windows.Media;

namespace Clipton.App.Tests;

public sealed class QuickMenuItemTests
{
    [Fact]
    public void FolderItem_UsesFolderVisualAffordances()
    {
        var child = new QuickMenuItem("Child", "Text", "T", "Enter", Brushes.SteelBlue, () => { });
        var folder = new QuickMenuItem("Folder", "2 items", ">", "Enter", Brushes.DimGray, () => { }, Children: [child]);

        Assert.True(folder.IsFolder);
        Assert.Equal(Visibility.Visible, folder.FolderIconVisibility);
        Assert.Equal(Visibility.Visible, folder.FolderChevronVisibility);
        Assert.Equal(Visibility.Collapsed, folder.CommandHintVisibility);
        Assert.Equal(Visibility.Collapsed, folder.IconVisibility);
        Assert.Equal(new Thickness(1), folder.RowBorderThickness);
    }

    [Fact]
    public void LeafItem_KeepsCommandHintAndRegularIcon()
    {
        var item = new QuickMenuItem("Text", "Text", "T", "Enter / T", Brushes.SteelBlue, () => { });

        Assert.False(item.IsFolder);
        Assert.Equal(Visibility.Collapsed, item.FolderIconVisibility);
        Assert.Equal(Visibility.Collapsed, item.FolderChevronVisibility);
        Assert.Equal(Visibility.Visible, item.CommandHintVisibility);
        Assert.Equal(Visibility.Visible, item.IconVisibility);
        Assert.Equal(new Thickness(0), item.RowBorderThickness);
    }

    [Fact]
    public void LazyFolder_ResolvesChildrenOnlyWhenOpened()
    {
        var calls = 0;
        var folder = new QuickMenuItem(
            "Folder",
            "1 item",
            ">",
            "Enter",
            Brushes.DimGray,
            () => { },
            LazyChildren: () =>
            {
                calls++;
                return [new QuickMenuItem("Child", "Text", "T", "Enter", Brushes.SteelBlue, () => { })];
            });

        Assert.True(folder.IsFolder);
        Assert.Equal(0, calls);
        Assert.Single(folder.GetChildren());
        Assert.Single(folder.GetChildren());
        Assert.Equal(1, calls);
    }
}
