using Drawing = System.Drawing;

namespace Clipton.WinUI;

internal static class AppAssets
{
    public static string AppIconPath => Path.Combine(AppContext.BaseDirectory, "Assets", "Clipton.ico");

    public static string AppImagePath => Path.Combine(AppContext.BaseDirectory, "Assets", "Clipton.png");

    public static string TrayIconPath => Path.Combine(AppContext.BaseDirectory, "Assets", "CliptonTray.ico");

    public static Drawing.Icon LoadAppIcon()
    {
        return File.Exists(AppIconPath)
            ? new Drawing.Icon(AppIconPath)
            : (Drawing.Icon)Drawing.SystemIcons.Application.Clone();
    }

    public static Drawing.Icon LoadTrayIcon()
    {
        return File.Exists(TrayIconPath)
            ? new Drawing.Icon(TrayIconPath)
            : LoadAppIcon();
    }
}
