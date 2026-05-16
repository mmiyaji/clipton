# Microsoft Store Preparation

Last updated: 2026-05-16

## Required Before Store Submission

- Use `packaging/Clipton.Package/Clipton.Package.wapproj` as the Windows Application Packaging Project.
- Generate a Release `.msixupload` or `.appxupload` package for Partner Center.
- Declare only required capabilities. WPF packaged desktop apps normally require `runFullTrust`.
- Keep `internetClient` absent until network features exist.
- Move persistent app data to user-writable app data locations only.
- Add Store metadata in English and Japanese.
- Publish privacy policy and terms URLs for both English and Japanese.
- Add screenshots for each Store listing language.
- Validate Release package installation on a clean Windows user profile.

## App Data and Privacy Requirements

- Clipboard history may contain personal or confidential data.
- Default behavior should remain local-only.
- Persistent history is opt-in and encrypted per Windows user in the current development build.
- Item-level delete, pause capture, and full clear controls exist in the current development build.
- Any sync, telemetry, or crash reporting must be documented and user-controlled.

## Packaging Notes

WPF does not produce MSIX by default. The intended release shape is:

- `Clipton.App`: WPF desktop app.
- Packaging project: MSIX/App Installer package with desktop bridge/full trust declaration.
- Manifest startup task: `CliptonStartup`, disabled by default and controlled by the user.

## Local Build

Use Visual Studio 2022 or MSBuild with DesktopBridge targets installed:

```powershell
msbuild packaging\Clipton.Package\Clipton.Package.wapproj /p:Configuration=Release /p:Platform=x64
```

Production Store submission still requires Partner Center identity association and signing.

Current source uses the packaged `StartupTask` API when Clipton has package identity, and falls back to the registry `Run` key only for unpackaged development builds.
