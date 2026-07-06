# Microsoft Store Preparation

Last updated: 2026-06-28

## Required Before Store Submission

- Use `packaging/Clipton.Package/Clipton.Package.wapproj` as the Windows Application Packaging Project.
- Generate a Release `.msixupload` or `.appxupload` package for Partner Center.
- Declare only required capabilities. The packaged WinUI desktop app currently requires `runFullTrust` for the global hotkey, tray resident behavior, and clipboard integration.
- Keep `internetClient` absent until network features exist.
- Move persistent app data to user-writable app data locations only.
- Add Store metadata. English and Japanese listing drafts are maintained in `docs/STORE_LISTING.md`.
- Keep package resources for the app display name and description in every shipped UI language.
- Publish privacy policy and terms URLs for both English and Japanese.
- Add screenshots for each Store listing language.
- Use the listing draft in `docs/STORE_LISTING.md`.
- Use the asset plan in `docs/STORE_ASSETS.md`.
- Capture final PC screenshots from the actual app UI. Do not submit marketing mockups with extra slogans or overlay text.
- Validate Release package installation on a clean Windows user profile.

## App Data and Privacy Requirements

- Clipboard history may contain personal or confidential data.
- Default behavior should remain local-only.
- Persistent history is enabled by default and encrypted per Windows user in the current development build.
- Item-level delete, pause capture, and full clear controls exist in the current development build.
- Any sync, telemetry, or crash reporting must be documented and user-controlled.

## Packaging Notes

WinUI is the active release target. The intended release shape is:

- `Clipton.WinUI`: WinUI desktop app.
- Packaging project: MSIX/App Installer package with desktop bridge/full trust declaration.
- Manifest startup task: `CliptonStartup`, disabled by default and controlled by the user.
- Store listing assets are under `packaging/Clipton.Package/Images`.

## Local Build

Use Visual Studio 2022 or MSBuild with DesktopBridge targets installed:

```powershell
msbuild packaging\Clipton.Package\Clipton.Package.wapproj /p:Configuration=Release /p:Platform=x64 /p:UapAppxPackageBuildMode=StoreUpload
```

Production Store submission still requires Partner Center identity association and signing.

Current source uses the packaged `StartupTask` API when Clipton has package identity, and falls back to the registry `Run` key only for unpackaged development builds.

## Release Validation Checklist

- Build and run `Clipton.WinUI` in Release configuration.
- Build the MSIX package from `packaging/Clipton.Package`.
- Run `tools\ci\verify-store-package.ps1` against the Release package output.
- Run Windows App Certification Kit from an elevated Windows environment before Partner Center upload.
- Install the package on a clean Windows user profile and verify first launch.
- Verify tray icon visibility, settings launch, global hotkey, search, paste, startup toggle, and encrypted history persistence.
- Verify light and dark quick menu themes for both default and rich menu modes.
- Verify app UI language switching for English, Japanese, German, Spanish, French, Korean, and Simplified Chinese.
- Verify Store screenshots and descriptions match the WinUI UI, not the previous WPF UI.
- Verify final screenshots are PNG files, at least 1366 x 768 pixels, under 50 MB, and uploaded separately for English and Japanese Store listings.
- If using a trailer, verify MP4/MOV 1920 x 1080 video, PNG 1920 x 1080 thumbnail, title, and optional WebVTT captions.
- Confirm privacy policy and terms URLs are publicly reachable before Partner Center submission.
- Confirm Partner Center identity and publisher fields replace the development manifest values (`Clipton.ClipboardManager`, `CN=Clipton`) before final package upload.

## Current Store Assets

Legacy draft generated PC mockups:

- `artifacts/store/01-quick-menu-en.png`
- `artifacts/store/02-history-en.png`
- `artifacts/store/03-snippets-en.png`
- `artifacts/store/04-settings-en.png`
- `artifacts/store/05-privacy-en.png`
- `artifacts/store/01-quick-menu-ja.png`
- `artifacts/store/02-history-ja.png`
- `artifacts/store/03-snippets-ja.png`
- `artifacts/store/04-settings-ja.png`
- `artifacts/store/05-privacy-ja.png`

All generated mockups are 1920 x 1080 PNG files.

Do not treat these generated files as final Partner Center screenshots until they are replaced or verified against the latest Microsoft screenshot guidance. Final screenshots should be actual app UI captures without extra marketing text.

Recent local validation captures for light-mode quick menus are stored under `artifacts/light-menu-check/`. These are engineering verification artifacts, not final Store assets.

The legacy generator below is retained for reference only. It should not be used for final Japanese assets until its localized strings are reviewed.

```powershell
dotnet run --project tools\StoreScreenshots\StoreScreenshots.csproj
```
