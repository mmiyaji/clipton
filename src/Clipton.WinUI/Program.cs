using Microsoft.UI.Xaml;

namespace Clipton.WinUI;

public static class Program
{
    private static App? _app;

    [STAThread]
    public static void Main(string[] args)
    {
        AppProfiler.Initialize(args);
        using var singleInstance = SingleInstanceGuard.TryAcquire();
        if (singleInstance is null)
        {
            SingleInstanceGuard.NotifyExistingInstance();
            return;
        }

        WinRT.ComWrappersSupport.InitializeComWrappers();
        AppProfiler.Mark("COM wrappers initialized.");
        Application.Start(_ =>
        {
            App.LaunchArgs = args;
            App.SingleInstance = singleInstance;
            _app = new App();
            AppProfiler.Mark("Application instance created.");
        });
    }
}
