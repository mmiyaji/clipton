# Clipton Privacy Policy

Last updated: 2026-07-03

Clipton is a local clipboard management app for Windows.

## Data Clipton Handles

Clipton reads clipboard content so it can show recent clipboard history, preview supported items, and paste selected items. Clipboard content may include text, rich text, HTML, images, and file paths copied from other apps.

## Storage

The current version stores clipboard history locally by default and protects history payloads with Windows user-scoped DPAPI. Registered snippets are also stored locally and encrypted. Settings, local image thumbnails, and temporary paste files are kept on the user's device. Users can disable encrypted history in settings.

Temporary files used for file paste and external image preview may contain clipboard content in unencrypted form. Clipton deletes them on a short best-effort schedule, about 30 minutes for paste files and 10 minutes for image preview files, and also cleans old temporary files on startup.

Clipton does not upload clipboard content, settings, snippets, or usage data to any server.

## Network and Telemetry

The current version has no cloud sync, analytics, telemetry, crash reporting, advertising SDK, or third-party data sharing.

## User Control

Users can pause capture, delete individual history items with the Delete key, clear clipboard history from the app window, export or import local data, require a PIN before showing history and snippets, and disable encrypted local history. Users can disable startup launch in the app settings or through Windows startup settings.

## Future Changes

If persistent history, sync, telemetry, or online features are added later, they must be opt-in where appropriate and this policy must be updated before release.

## Contact

Project maintainers can be contacted through the repository issue tracker.
