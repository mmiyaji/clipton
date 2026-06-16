# Clipton

Clipton is a local-first clipboard manager for Windows. It keeps recent clipboard history, registered text snippets, and quick paste commands close to the keyboard while avoiding cloud sync, telemetry, and network dependencies.

![Clipton overview with quick menu, clipboard history, and snippet editor screenshots](docs/images/clipton-readme-overview.png)

## Features

- WinUI resident app with a system tray icon.
- Global hotkey menu, default `Ctrl+Alt+V`, with preset fallback shortcuts when the default is unavailable.
- Clipboard history capture for text, RTF, HTML, images, and file drops.
- Keyboard-driven quick menu with folder navigation, search, plain-text paste, temporary mask reveal, and timestamp display.
- Paste history items in their original clipboard format when possible.
- Registered snippets with folder support.
- Register snippets directly from history.
- Configurable hotkey, startup registration, theme, language, and saved history limit.
- Pause capture, clear history, sensitive-content masking, and encrypted local history.
- Local-only privacy posture: no cloud sync, telemetry, crash reporting, ads, or third-party data sharing.
- English and Japanese UI strings.
- Unit tests for history, hotkeys, settings, localization, clipboard bridge behavior, and encrypted persistence.

## Run

```powershell
dotnet run --project src\Clipton.WinUI\Clipton.WinUI.csproj
```

The app starts in the tray. Double-click the tray icon to open the settings/history window.

## Test

```powershell
dotnet test Clipton.slnx
```

Coverage measurement is documented in [docs/COVERAGE.md](docs/COVERAGE.md).

## Local data

Settings and snippets are stored under `%APPDATA%\Clipton`.

- `settings.json`: hotkey, startup, language, and history size settings.
- `snippets.json`: registered text snippets.
- `history\manifest.dat`, `history\base.dat`, `history\delta.dat`: encrypted clipboard history segments.
- `thumbs\`: local image thumbnails used by the quick menu.

Clipboard history is encrypted and persisted locally by default. Users can disable encrypted local history in the settings window. When enabled, history payloads are protected with Windows user-scoped DPAPI. Older single-file history stores are migrated to the segmented format and backed up as `history.dat.legacy.bak`.

See [docs/PERSISTENCE_PERFORMANCE.md](docs/PERSISTENCE_PERFORMANCE.md) for the persistence performance comparison.

## Privacy

Clipton reads clipboard content only to show recent clipboard history and paste selected items. The current version does not upload clipboard content, settings, snippets, or usage data to any server.

- Privacy policy: <https://mmiyaji.github.io/clipton/privacy/>
- Terms of use: <https://mmiyaji.github.io/clipton/terms/>

## Store readiness

The WinUI app builds today, and the packaging project targets the WinUI executable. Microsoft Store submission still requires Partner Center identity association, signing, Store listing metadata, and clean-profile package validation.

- Store preparation checklist: [docs/STORE_PREP.md](docs/STORE_PREP.md)
- Store listing draft: [docs/STORE_LISTING.md](docs/STORE_LISTING.md)
- Store screenshot assets: [artifacts/store](artifacts/store)
