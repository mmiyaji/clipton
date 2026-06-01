using Drawing = System.Drawing;

namespace Clipton.WinUI;

internal static class AppAssets
{
    public static string AppIconPath => Path.Combine(AppContext.BaseDirectory, "Assets", "Clipton.ico");

    public static string AppIconDarkPath => Path.Combine(AppContext.BaseDirectory, "Assets", "CliptonDark.ico");

    public static string AppImagePath => Path.Combine(AppContext.BaseDirectory, "Assets", "Clipton.png");

    public static string AppImageDarkPath => Path.Combine(AppContext.BaseDirectory, "Assets", "CliptonDark.png");

    public static string TrayIconPath => Path.Combine(AppContext.BaseDirectory, "Assets", "CliptonTray.ico");

    public static string TrayIconDarkPath => Path.Combine(AppContext.BaseDirectory, "Assets", "CliptonTrayDark.ico");

    public static string GetAppIconPath(string theme)
    {
        return IsDark(theme) && File.Exists(AppIconDarkPath) ? AppIconDarkPath : AppIconPath;
    }

    public static string GetAppImagePath(string theme)
    {
        return IsDark(theme) && File.Exists(AppImageDarkPath) ? AppImageDarkPath : AppImagePath;
    }

    public static Drawing.Icon LoadAppIcon(string theme)
    {
        var path = GetAppIconPath(theme);
        return File.Exists(path)
            ? new Drawing.Icon(path)
            : (Drawing.Icon)Drawing.SystemIcons.Application.Clone();
    }

    public static Drawing.Icon LoadTrayIcon(string theme)
    {
        var path = IsDark(theme) && File.Exists(TrayIconDarkPath) ? TrayIconDarkPath : TrayIconPath;
        return File.Exists(path)
            ? new Drawing.Icon(path)
            : LoadAppIcon(theme);
    }

    private static bool IsDark(string theme) => string.Equals(theme, "dark", StringComparison.OrdinalIgnoreCase);
}
