using Microsoft.UI.Xaml;

namespace Clipton.WinUI;

public sealed class App : Application
{
    private CliptonRuntime? _runtime;

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _runtime = new CliptonRuntime();
        _runtime.Start();
    }
}
