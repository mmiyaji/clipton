using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.XamlTypeInfo;
using System.Runtime.InteropServices;

namespace Clipton.WinUI;

public sealed class App : Application, IXamlMetadataProvider
{
    private CliptonRuntime? _runtime;
    private XamlControlsXamlMetaDataProvider? _xamlMetadataProvider;

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
            EnsureXamlMetadataProvider();
            Resources.MergedDictionaries.Add(new XamlControlsResources());
            _runtime = new CliptonRuntime();
            _runtime.Start();
            var forceShow = LaunchArgs.Contains("--settings", StringComparer.OrdinalIgnoreCase)
                || args.Arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Contains("--settings", StringComparer.OrdinalIgnoreCase);
            _runtime.ShowStartupWindowIfNeeded(forceShow);
        }
        catch (Exception exception)
        {
            AppDiagnostics.Log(exception, "Launch");
            throw;
        }
    }

    public IXamlType GetXamlType(Type type) => EnsureXamlMetadataProvider().GetXamlType(type);

    public IXamlType GetXamlType(string fullName) => EnsureXamlMetadataProvider().GetXamlType(fullName);

    public XmlnsDefinition[] GetXmlnsDefinitions() => EnsureXamlMetadataProvider().GetXmlnsDefinitions();

    private XamlControlsXamlMetaDataProvider EnsureXamlMetadataProvider()
    {
        if (_xamlMetadataProvider is null)
        {
            XamlControlsXamlMetaDataProvider.Initialize();
            _xamlMetadataProvider = new XamlControlsXamlMetaDataProvider();
        }

        return _xamlMetadataProvider;
    }
}
