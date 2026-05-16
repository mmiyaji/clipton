using System.Windows;
using System.Windows.Interop;

namespace Clipton.App;

internal static class WindowThemeService
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaUseImmersiveDarkModeLegacy = 19;

    internal static void Apply(Window window, string theme)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
        {
            return;
        }

        var helper = new WindowInteropHelper(window);
        if (helper.Handle == IntPtr.Zero)
        {
            window.SourceInitialized += (_, _) => Apply(window, theme);
            return;
        }

        var useDarkMode = string.Equals(theme, "dark", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        var result = NativeMethods.DwmSetWindowAttribute(
            helper.Handle,
            DwmwaUseImmersiveDarkMode,
            ref useDarkMode,
            sizeof(int));

        if (result != 0)
        {
            NativeMethods.DwmSetWindowAttribute(
                helper.Handle,
                DwmwaUseImmersiveDarkModeLegacy,
                ref useDarkMode,
                sizeof(int));
        }
    }
}
