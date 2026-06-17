# CI Validation Gates

Clipton uses three Windows-based validation gates for release confidence without long-running GitHub Actions jobs.

## Unit And Upgrade Compatibility

Workflow job: `unit-and-upgrade`

- Runs `dotnet test Clipton.slnx -c Release`.
- Includes deterministic upgrade compatibility tests for legacy single-file history, v3 segmented history, v4 chunked history, and v5 itemized history writes.
- Uses `--blame-hang --blame-hang-timeout 2m` plus an overall test session timeout.

## Quick Menu UI Smoke

Workflow job: `ui-smoke`

- Builds the WinUI app in Release configuration and runs the generated exe.
- Runs `tools/e2e/quickmenu-e2e.ps1` with an isolated data directory.
- Seeds settings and clipboard history, opens the quick menu with `Ctrl+Alt+V`, checks keyboard navigation, lazy folder opening, submenu close behavior, Esc dismissal, and reopen behavior.
- The script has bounded waits and a scenario timeout so a blocked desktop interaction fails quickly instead of hanging the workflow.

## Store Package Verification

Workflow job: `package-verify`

- Builds the Windows Application Packaging Project in Release x64 Store upload mode.
- Runs `tools/ci/verify-store-package.ps1`.
- Verifies that Store upload artifacts exist, are non-empty, are readable package archives, and contain an Appx manifest directly or inside a nested package.

The package verification script is also used by the Store build and Store publish workflows before artifacts are uploaded or submitted.
