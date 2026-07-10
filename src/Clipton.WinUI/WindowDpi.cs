using Windows.Graphics;

namespace Clipton.WinUI;

internal static class WindowDpi
{
    private const double DefaultDpi = 96.0;

    public static double GetScale(IntPtr hwnd)
    {
        var dpi = hwnd == IntPtr.Zero ? 0 : NativeMethods.GetDpiForWindow(hwnd);
        return dpi == 0 ? 1.0 : dpi / DefaultDpi;
    }

    public static int ToPhysicalPixels(IntPtr hwnd, double dip)
    {
        return Math.Max(1, (int)Math.Round(dip * GetScale(hwnd)));
    }

    public static SizeInt32 ToPhysicalSize(IntPtr hwnd, double widthDip, double heightDip)
    {
        return new SizeInt32(
            ToPhysicalPixels(hwnd, widthDip),
            ToPhysicalPixels(hwnd, heightDip));
    }
}
