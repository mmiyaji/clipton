using Microsoft.UI.Xaml;

namespace Clipton.WinUI;

public sealed class App : Application
{
    private CliptonRuntime? _runtime;

    public static string[] LaunchArgs { get; set; } = [];

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _runtime = new CliptonRuntime();
        _runtime.Start();
        if (LaunchArgs.Contains("--settings", StringComparer.OrdinalIgnoreCase))
        {
            _runtime.ShowMainWindow();
        }
    }
}
