using Microsoft.UI.Xaml;

namespace Clipton.WinUI;

public static class Program
{
    private static App? _app;

    [STAThread]
    public static void Main(string[] args)
    {
        AppProfiler.Initialize(args);
        WinRT.ComWrappersSupport.InitializeComWrappers();
        AppProfiler.Mark("COM wrappers initialized.");
        Application.Start(_ =>
        {
            App.LaunchArgs = args;
            _app = new App();
            AppProfiler.Mark("Application instance created.");
        });
    }
}
