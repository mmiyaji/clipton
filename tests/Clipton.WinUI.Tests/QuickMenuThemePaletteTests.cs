using Clipton.WinUI;
using Microsoft.UI.Xaml;

namespace Clipton.WinUI.Tests;

public sealed class QuickMenuThemePaletteTests
{
    [Theory]
    [InlineData("light", ElementTheme.Light)]
    [InlineData("dark", ElementTheme.Dark)]
    [InlineData("system", ElementTheme.Light)]
    public void QuickMenuPalette_MapsThemeToRequestedTheme(string theme, ElementTheme expected)
    {
        var palette = QuickMenuThemePalette.ForTheme(theme);

        Assert.Equal(expected, palette.RequestedTheme);
    }

    [Fact]
    public void QuickMenuPalette_UsesDarkTextOnLightImagePreview()
    {
        var light = QuickMenuThemePalette.ForTheme("light");
        var dark = QuickMenuThemePalette.ForTheme("dark");

        Assert.NotEqual(dark.ImagePreviewPanelBackground, light.ImagePreviewPanelBackground);
        Assert.True(light.ImagePreviewPanelBackground.R > light.ImagePreviewForeground.R);
        Assert.True(dark.ImagePreviewForeground.R > dark.ImagePreviewPanelBackground.R);
    }

    [Theory]
    [InlineData("light", ElementTheme.Light)]
    [InlineData("dark", ElementTheme.Dark)]
    [InlineData("system", ElementTheme.Light)]
    public void RichQuickMenuPalette_MapsThemeToRequestedTheme(string theme, ElementTheme expected)
    {
        var palette = RichQuickMenuPalette.ForTheme(theme);

        Assert.Equal(expected, palette.RequestedTheme);
    }

    [Fact]
    public void RichQuickMenuPalette_UsesLightCardsAndDarkTextInLightMode()
    {
        var light = RichQuickMenuPalette.ForTheme("light");
        var dark = RichQuickMenuPalette.ForTheme("dark");

        Assert.NotEqual(dark.ItemBackground, light.ItemBackground);
        Assert.True(light.ItemBackground.R > light.TextForeground.R);
        Assert.True(dark.TextForeground.R > dark.ItemBackground.R);
    }
}
