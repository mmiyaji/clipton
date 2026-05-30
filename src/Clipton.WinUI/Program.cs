using Microsoft.UI.Xaml;

namespace Clipton.WinUI;

public static class Program
{
    private static App? _app;

    [STAThread]
    public static void Main(string[] args)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(_ =>
        {
            App.LaunchArgs = args;
            _app = new App();
        });
    }
}
