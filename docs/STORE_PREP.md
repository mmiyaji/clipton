# Microsoft Store Preparation

Last updated: 2026-05-16

## Required Before Store Submission

- Add a Windows Application Packaging Project or equivalent MSIX packaging flow.
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
- Persistent history should be encrypted per Windows user before being enabled.
- Add item-level delete, pause capture, and full clear controls before public release.
- Any sync, telemetry, or crash reporting must be documented and user-controlled.

## Packaging Notes

WPF does not produce MSIX by default. The intended release shape is:

- `Clipton.App`: WPF desktop app.
- Packaging project: MSIX/App Installer package with desktop bridge/full trust declaration.
- Manifest startup task: optional packaged startup integration, controlled by the user.

Current source includes registry-based startup registration for unpackaged development builds. Packaged Store builds should move startup behavior to the packaged startup task API.
