using System.Windows;
using Application = System.Windows.Application;

namespace Clipton.App;

public partial class App : Application
{
    private CliptonRuntime? _runtime;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        _runtime = new CliptonRuntime();
        _runtime.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _runtime?.Dispose();
        base.OnExit(e);
    }
}
