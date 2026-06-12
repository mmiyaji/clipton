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
            // Without this, awaits on the UI thread resume on the thread pool
            // and any subsequent XAML access fails with RPC_E_WRONG_THREAD.
            SynchronizationContext.SetSynchronizationContext(
                new Microsoft.UI.Dispatching.DispatcherQueueSynchronizationContext(
                    Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()));
            App.LaunchArgs = args;
            App.SingleInstance = singleInstance;
            _app = new App();
            AppProfiler.Mark("Application instance created.");
        });
    }
}
