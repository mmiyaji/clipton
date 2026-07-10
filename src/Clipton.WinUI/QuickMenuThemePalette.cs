using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace Clipton.WinUI;

internal readonly record struct ThemeColor(byte A, byte R, byte G, byte B)
{
    public static ThemeColor Opaque(byte r, byte g, byte b) => new(255, r, g, b);

    public static ThemeColor Alpha(byte a, byte r, byte g, byte b) => new(a, r, g, b);

    public static ThemeColor FromColor(Color color) => new(color.A, color.R, color.G, color.B);

    public Color ToColor() => Color.FromArgb(A, R, G, B);

    public SolidColorBrush ToBrush() => new(ToColor());
}

internal static class SystemThemeColors
{
    public static bool IsHighContrast
    {
        get
        {
            try
            {
                return new AccessibilitySettings().HighContrast;
            }
            catch
            {
                return false;
            }
        }
    }

    public static ThemeColor Resolve(string resourceKey, UIColorType fallbackType, ThemeColor fallback)
    {
        try
        {
            var resources = Application.Current?.Resources;
            if (resources is not null
                && resources.ContainsKey(resourceKey)
                && resources[resourceKey] is SolidColorBrush brush)
            {
                return ThemeColor.FromColor(brush.Color);
            }

            return ThemeColor.FromColor(new UISettings().GetColorValue(fallbackType));
        }
        catch
        {
            return fallback;
        }
    }
}

internal sealed record QuickMenuThemePalette(
    string Theme,
    ElementTheme RequestedTheme,
    ThemeColor AppTitleForeground,
    ThemeColor ImagePreviewWindowBackground,
    ThemeColor ImagePreviewPanelBackground,
    ThemeColor ImagePreviewImageBackground,
    ThemeColor ImagePreviewForeground,
    ThemeColor ImagePreviewSecondaryForeground,
    ThemeColor ImagePreviewSeparator,
    ThemeColor ImagePreviewFeedbackBackground,
    ThemeColor ImagePreviewFeedbackForeground,
    ThemeColor ImagePreviewActionBackground,
    ThemeColor ImagePreviewActionBorder,
    ThemeColor ImagePreviewActionForeground,
    ThemeColor InlineImagePreviewBorder,
    ThemeColor InlineImagePreviewBackground)
{
    private static readonly QuickMenuThemePalette Dark = new(
        "dark",
        ElementTheme.Dark,
        ThemeColor.Opaque(170, 170, 170),
        ThemeColor.Opaque(28, 28, 28),
        ThemeColor.Opaque(33, 33, 33),
        ThemeColor.Opaque(16, 16, 16),
        ThemeColor.Alpha(245, 255, 255, 255),
        ThemeColor.Alpha(190, 255, 255, 255),
        ThemeColor.Alpha(70, 255, 255, 255),
        ThemeColor.Alpha(220, 32, 32, 32),
        ThemeColor.Opaque(255, 255, 255),
        ThemeColor.Opaque(43, 43, 43),
        ThemeColor.Opaque(68, 68, 68),
        ThemeColor.Alpha(245, 255, 255, 255),
        ThemeColor.Alpha(80, 255, 255, 255),
        ThemeColor.Alpha(32, 255, 255, 255));

    private static readonly QuickMenuThemePalette Light = new(
        "light",
        ElementTheme.Light,
        ThemeColor.Opaque(75, 85, 99),
        ThemeColor.Opaque(243, 244, 246),
        ThemeColor.Opaque(255, 255, 255),
        ThemeColor.Opaque(238, 242, 247),
        ThemeColor.Opaque(17, 24, 39),
        ThemeColor.Opaque(107, 114, 128),
        ThemeColor.Opaque(217, 226, 236),
        ThemeColor.Alpha(235, 17, 24, 39),
        ThemeColor.Opaque(255, 255, 255),
        ThemeColor.Opaque(255, 255, 255),
        ThemeColor.Opaque(203, 213, 225),
        ThemeColor.Opaque(17, 24, 39),
        ThemeColor.Opaque(203, 213, 225),
        ThemeColor.Opaque(248, 250, 252));

    public static QuickMenuThemePalette ForTheme(string? theme)
    {
        if (SystemThemeColors.IsHighContrast)
        {
            return CreateHighContrast();
        }

        return string.Equals(theme, "dark", StringComparison.OrdinalIgnoreCase) ? Dark : Light;
    }

    private static QuickMenuThemePalette CreateHighContrast()
    {
        var background = SystemThemeColors.Resolve(
            "SystemColorWindowColorBrush",
            UIColorType.Background,
            ThemeColor.Opaque(0, 0, 0));
        var foreground = SystemThemeColors.Resolve(
            "SystemColorWindowTextColorBrush",
            UIColorType.Foreground,
            ThemeColor.Opaque(255, 255, 255));
        var buttonFace = SystemThemeColors.Resolve(
            "SystemColorButtonFaceColorBrush",
            UIColorType.Background,
            background);
        var buttonText = SystemThemeColors.Resolve(
            "SystemColorButtonTextColorBrush",
            UIColorType.Foreground,
            foreground);
        var highlight = SystemThemeColors.Resolve(
            "SystemColorHighlightColorBrush",
            UIColorType.Accent,
            ThemeColor.Opaque(0, 120, 215));
        var highlightText = SystemThemeColors.Resolve(
            "SystemColorHighlightTextColorBrush",
            UIColorType.Foreground,
            foreground);
        return new QuickMenuThemePalette(
            "highContrast",
            ElementTheme.Default,
            foreground,
            background,
            background,
            background,
            foreground,
            foreground,
            foreground,
            highlight,
            highlightText,
            buttonFace,
            buttonText,
            buttonText,
            foreground,
            background);
    }
}

internal sealed record RichQuickMenuPalette(
    string Theme,
    ElementTheme RequestedTheme,
    ThemeColor RootBackground,
    ThemeColor MenuBackground,
    ThemeColor TopEdge,
    ThemeColor LoadingRingForeground,
    ThemeColor LoadingOverlayBackground,
    ThemeColor LoadingPanelBackground,
    ThemeColor LoadingPanelBorder,
    ThemeColor PreviewCardBackground,
    ThemeColor AllHistoryBackground,
    ThemeColor AllHistoryBorder,
    ThemeColor AllHistoryForeground,
    ThemeColor PreviewImageBackground,
    ThemeColor FeedbackBackground,
    ThemeColor FeedbackBorder,
    ThemeColor FeedbackForeground,
    ThemeColor ItemBorder,
    ThemeColor ItemBackground,
    ThemeColor ItemSelectedBorder,
    ThemeColor ItemSelectedBackground,
    ThemeColor ItemIconBackground,
    ThemeColor IconForeground,
    ThemeColor ToolbarBackground,
    ThemeColor ToolbarSelectedBackground,
    ThemeColor ToolbarSelectedBorder,
    ThemeColor ButtonForeground,
    ThemeColor PreviewActionBackground,
    ThemeColor PreviewActionBorder,
    ThemeColor TextForeground)
{
    private static readonly RichQuickMenuPalette Dark = new(
        "dark",
        ElementTheme.Dark,
        ThemeColor.Opaque(37, 37, 37),
        ThemeColor.Opaque(37, 37, 37),
        ThemeColor.Opaque(37, 37, 37),
        ThemeColor.Opaque(245, 245, 245),
        ThemeColor.Alpha(188, 24, 24, 24),
        ThemeColor.Opaque(43, 43, 43),
        ThemeColor.Opaque(72, 72, 72),
        ThemeColor.Opaque(44, 44, 44),
        ThemeColor.Opaque(43, 43, 43),
        ThemeColor.Opaque(65, 65, 65),
        ThemeColor.Opaque(246, 246, 246),
        ThemeColor.Opaque(30, 30, 30),
        ThemeColor.Opaque(20, 20, 20),
        ThemeColor.Opaque(74, 74, 74),
        ThemeColor.Opaque(245, 245, 245),
        ThemeColor.Opaque(56, 56, 56),
        ThemeColor.Opaque(40, 40, 40),
        ThemeColor.Opaque(28, 160, 230),
        ThemeColor.Opaque(48, 48, 48),
        ThemeColor.Opaque(34, 34, 34),
        ThemeColor.Opaque(232, 232, 232),
        ThemeColor.Opaque(37, 37, 37),
        ThemeColor.Opaque(36, 79, 102),
        ThemeColor.Opaque(23, 150, 214),
        ThemeColor.Opaque(236, 236, 236),
        ThemeColor.Opaque(58, 58, 58),
        ThemeColor.Opaque(68, 68, 68),
        ThemeColor.Opaque(245, 245, 245));

    private static readonly RichQuickMenuPalette Light = new(
        "light",
        ElementTheme.Light,
        ThemeColor.Opaque(249, 250, 251),
        ThemeColor.Opaque(249, 250, 251),
        ThemeColor.Opaque(249, 250, 251),
        ThemeColor.Opaque(17, 24, 39),
        ThemeColor.Alpha(210, 248, 250, 252),
        ThemeColor.Opaque(255, 255, 255),
        ThemeColor.Opaque(203, 213, 225),
        ThemeColor.Opaque(255, 255, 255),
        ThemeColor.Opaque(255, 255, 255),
        ThemeColor.Opaque(203, 213, 225),
        ThemeColor.Opaque(17, 24, 39),
        ThemeColor.Opaque(238, 242, 247),
        ThemeColor.Opaque(17, 24, 39),
        ThemeColor.Opaque(75, 85, 99),
        ThemeColor.Opaque(255, 255, 255),
        ThemeColor.Opaque(208, 215, 222),
        ThemeColor.Opaque(255, 255, 255),
        ThemeColor.Opaque(11, 115, 183),
        ThemeColor.Opaque(234, 244, 255),
        ThemeColor.Opaque(238, 242, 247),
        ThemeColor.Opaque(31, 41, 55),
        ThemeColor.Opaque(249, 250, 251),
        ThemeColor.Opaque(221, 235, 250),
        ThemeColor.Opaque(25, 135, 201),
        ThemeColor.Opaque(17, 24, 39),
        ThemeColor.Opaque(255, 255, 255),
        ThemeColor.Opaque(203, 213, 225),
        ThemeColor.Opaque(17, 24, 39));

    public static RichQuickMenuPalette ForTheme(string? theme)
    {
        if (SystemThemeColors.IsHighContrast)
        {
            return CreateHighContrast();
        }

        return string.Equals(theme, "dark", StringComparison.OrdinalIgnoreCase) ? Dark : Light;
    }

    private static RichQuickMenuPalette CreateHighContrast()
    {
        var background = SystemThemeColors.Resolve(
            "SystemColorWindowColorBrush",
            UIColorType.Background,
            ThemeColor.Opaque(0, 0, 0));
        var foreground = SystemThemeColors.Resolve(
            "SystemColorWindowTextColorBrush",
            UIColorType.Foreground,
            ThemeColor.Opaque(255, 255, 255));
        var buttonFace = SystemThemeColors.Resolve(
            "SystemColorButtonFaceColorBrush",
            UIColorType.Background,
            background);
        var buttonText = SystemThemeColors.Resolve(
            "SystemColorButtonTextColorBrush",
            UIColorType.Foreground,
            foreground);
        var highlight = SystemThemeColors.Resolve(
            "SystemColorHighlightColorBrush",
            UIColorType.Accent,
            ThemeColor.Opaque(0, 120, 215));
        var highlightText = SystemThemeColors.Resolve(
            "SystemColorHighlightTextColorBrush",
            UIColorType.Foreground,
            foreground);
        return new RichQuickMenuPalette(
            "highContrast",
            ElementTheme.Default,
            background,
            background,
            foreground,
            foreground,
            background,
            buttonFace,
            buttonText,
            background,
            buttonFace,
            buttonText,
            buttonText,
            background,
            highlight,
            highlightText,
            highlightText,
            foreground,
            background,
            highlight,
            background,
            buttonFace,
            buttonText,
            buttonFace,
            buttonFace,
            highlight,
            buttonText,
            buttonFace,
            buttonText,
            foreground);
    }
}
