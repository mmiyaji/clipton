# CI Validation Gates

Clipton uses two Windows-based GitHub Actions validation gates for release confidence without long-running jobs.

## Unit And Upgrade Compatibility

Workflow job: `unit-and-upgrade`

- Runs `dotnet test Clipton.slnx -c Release`.
- Includes deterministic upgrade compatibility tests for legacy single-file history, v3 segmented history, v4 chunked history, and v5 itemized history writes.
- Uses `--blame-hang --blame-hang-timeout 2m` plus an overall test session timeout.

## Store Package Verification

Workflow job: `package-verify`

- Builds the Windows Application Packaging Project in Release x64 Store upload mode.
- Runs `tools/ci/verify-store-package.ps1`.
- Verifies that Store upload artifacts exist, are non-empty, are readable package archives, and contain an Appx manifest directly or inside a nested package.

The package verification script is also used by the Store build and Store publish workflows before artifacts are uploaded or submitted.

## Manual UI Smoke

Desktop UI automation is intentionally excluded from GitHub-hosted Actions because those runners do not provide a reliable interactive desktop contract for WinUI hotkey and UI Automation tests.

Run the quick menu smoke locally or on a logged-in self-hosted Windows runner:

```powershell
dotnet build src\Clipton.WinUI\Clipton.WinUI.csproj -c Release -p:WindowsPackageType=None
tools\e2e\quickmenu-e2e.ps1 -ExePath src\Clipton.WinUI\bin\Release\net8.0-windows10.0.19041.0\Clipton.exe -Hotkey "Shift+Alt+V" -ScenarioTimeoutSeconds 90 -KillExisting
```
