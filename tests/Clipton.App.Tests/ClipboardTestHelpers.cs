using System.Collections.Specialized;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;
using Clipton.Core;

namespace Clipton.App.Tests;

internal static class ClipboardTestHelpers
{
    public static string GetText(TextDataFormat format)
    {
        return Retry(() => Clipboard.GetText(format));
    }

    public static bool ContainsText(TextDataFormat format)
    {
        return Retry(() => Clipboard.ContainsText(format));
    }

    public static StringCollection GetFileDropList()
    {
        return Retry(Clipboard.GetFileDropList);
    }

    public static BitmapSource? GetImage()
    {
        return Retry(Clipboard.GetImage);
    }

    public static ClipboardSnapshot Capture()
    {
        return Retry(() =>
        {
            var snapshot = ClipboardBridge.Capture();
            if (snapshot is null)
            {
                throw new InvalidOperationException("Clipboard snapshot was not available yet.");
            }

            return snapshot;
        });
    }

    private static T Retry<T>(Func<T> action)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                return action();
            }
            catch (Exception ex) when (attempt < 9 && ex is ExternalException or InvalidOperationException)
            {
                Thread.Sleep(50);
            }
        }

        return action();
    }
}
