# Coverage

Clipton tracks C1 coverage as Cobertura `branch-rate`.

The current unit coverage gate is 98% or higher for code that can be exercised deterministically by unit tests:

- `Clipton.Core` is measured from `Clipton.Core.Tests`.
- `Clipton.App` is measured from `Clipton.App.Tests` for non-UI logic seams.
- Generated files, XAML, test assemblies, WPF windows, process startup, native hotkey registration, startup registration, and theme integration are not part of the unit coverage denominator. Those areas require UI or OS integration tests instead of pure unit tests.

## Prerequisites

Install ReportGenerator into the ignored `artifacts` folder if it is not already present:

```powershell
dotnet tool install dotnet-reportgenerator-globaltool --tool-path artifacts\tools
```

## Run Coverage

Run Core coverage:

```powershell
dotnet test .\tests\Clipton.Core.Tests\Clipton.Core.Tests.csproj -c Release --collect "XPlat Code Coverage" --settings .\coverage.runsettings --results-directory artifacts\coverage\core -- DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Include=[Clipton.Core]*
```

Run App unit-logic coverage:

```powershell
dotnet test .\tests\Clipton.App.Tests\Clipton.App.Tests.csproj -c Release --collect "XPlat Code Coverage" --settings .\coverage.runsettings --results-directory artifacts\coverage\app -- DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Include=[Clipton.App]* DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.ExcludeByFile=**/obj/**/*.cs,**/*.g.cs,**/*.g.i.cs,**/*.xaml,**/src/Clipton.App/App.xaml.cs,**/src/Clipton.App/MainWindow.xaml.cs,**/src/Clipton.App/QuickMenuWindow.xaml.cs,**/src/Clipton.App/CliptonRuntime.cs,**/src/Clipton.App/HotkeyMessageWindow.cs,**/src/Clipton.App/StartupRegistration.cs,**/src/Clipton.App/WindowThemeService.cs
```

Merge and summarize:

```powershell
artifacts\tools\reportgenerator.exe "-reports:artifacts\coverage\core\**\coverage.cobertura.xml;artifacts\coverage\app\**\coverage.cobertura.xml" "-targetdir:artifacts\coverage\merged" "-reporttypes:TextSummary;Cobertura"
Get-Content artifacts\coverage\merged\Summary.txt
```

The merged summary must show `Branch coverage` at 98% or higher.
