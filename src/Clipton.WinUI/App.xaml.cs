using Microsoft.UI.Xaml;
using System.Runtime.InteropServices;

namespace Clipton.WinUI;

public sealed class App : Application
{
    private CliptonRuntime? _runtime;

    public static string[] LaunchArgs { get; set; } = [];

    public App()
    {
        UnhandledException += (_, e) =>
        {
            AppDiagnostics.Log(e.Exception, "WinUI unhandled exception");
            if (e.Exception is COMException)
            {
                e.Handled = true;
            }
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception exception)
            {
                AppDiagnostics.Log(exception, "AppDomain unhandled exception");
            }
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            AppDiagnostics.Log(e.Exception, "Unobserved task exception");
            e.SetObserved();
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            _runtime = new CliptonRuntime();
            _runtime.Start();
            _runtime.ShowStartupWindowIfNeeded(LaunchArgs.Contains("--settings", StringComparer.OrdinalIgnoreCase));
        }
        catch (Exception exception)
        {
            AppDiagnostics.Log(exception, "Launch");
            throw;
        }
    }
}
