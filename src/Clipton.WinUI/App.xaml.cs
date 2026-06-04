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
            if (AppProfiler.Enabled)
            {
                AppDiagnostics.Configure(verboseEnabled: true);
            }

            AppProfiler.Mark("OnLaunched entered.");
            EnsureXamlMetadataProvider();
            AppProfiler.Mark("XAML metadata provider ensured.");
            Resources.MergedDictionaries.Add(new XamlControlsResources());
            AppProfiler.Mark("XAML resources added.");
            var launchArguments = GetLaunchArguments(args.Arguments);
            var dataDirectory = ResolveDataDirectory(launchArguments);
            _runtime = new CliptonRuntime(dataDirectory);
            AppProfiler.Mark("Runtime constructed.");
            _runtime.Start();
            AppProfiler.Mark("Runtime started.");
            var forceShow = launchArguments.Contains("--settings", StringComparer.OrdinalIgnoreCase);
            _runtime.ShowStartupWindowIfNeeded(forceShow);
            AppProfiler.Mark(forceShow ? "Startup window shown by launch argument." : "Startup window policy applied.");
        }
        catch (Exception exception)
        {
            AppDiagnostics.Log(exception, "Launch");
            throw;
        }
    }

    private static string[] GetLaunchArguments(string packagedArguments)
    {
        return LaunchArgs
            .Concat(packagedArguments.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .ToArray();
    }

    private static string? ResolveDataDirectory(IReadOnlyList<string> launchArguments)
    {
        var dataDirIndex = -1;
        for (var i = 0; i < launchArguments.Count; i++)
        {
            if (string.Equals(launchArguments[i], "--data-dir", StringComparison.OrdinalIgnoreCase))
            {
                dataDirIndex = i;
                break;
            }
        }

        if (dataDirIndex >= 0 && dataDirIndex + 1 < launchArguments.Count)
        {
            return Path.GetFullPath(Environment.ExpandEnvironmentVariables(launchArguments[dataDirIndex + 1]));
        }

        if (launchArguments.Contains("--safe-mode", StringComparer.OrdinalIgnoreCase))
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Clipton",
                "SafeMode");
        }

        return null;
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
