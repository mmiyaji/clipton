using Microsoft.UI.Xaml;

namespace Clipton.WinUI;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(_ =>
        {
            var app = new App();
        });
    }
}
