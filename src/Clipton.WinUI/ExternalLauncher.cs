using Windows.Storage;
using Windows.System;

namespace Clipton.WinUI;

internal static class ExternalLauncher
{
    public static async Task<bool> OpenFolderAsync(string path)
    {
        try
        {
            var folder = await StorageFolder.GetFolderFromPathAsync(path);
            return await Launcher.LaunchFolderAsync(folder);
        }
        catch
        {
            return false;
        }
    }

    public static async Task<bool> OpenFileAsync(string path)
    {
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(path);
            return await Launcher.LaunchFileAsync(file);
        }
        catch
        {
            return false;
        }
    }

    public static async Task<bool> OpenUriAsync(string uri)
    {
        try
        {
            return Uri.TryCreate(uri, UriKind.Absolute, out var parsed)
                && await Launcher.LaunchUriAsync(parsed);
        }
        catch
        {
            return false;
        }
    }
}
