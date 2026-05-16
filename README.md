# Clipton

Clipton is a Windows resident clipboard manager inspired by Clipy and CLCL.

## Current features

- WPF resident app with system tray icon.
- Global hotkey menu, default `Ctrl+Shift+V`.
- Clipboard history capture for text, RTF, HTML, images, and file drops.
- Paste history items in the original clipboard format when possible.
- Paste text items as plain text.
- Snippet insertion from a local snippet catalog.
- Configurable hotkey, startup registration, and UI language.
- Pause capture, clear history, item deletion with Delete, and opt-in encrypted local history.
- English and Japanese UI strings.
- Core unit tests for history, hotkeys, settings, and localization.

## Run

```powershell
dotnet run --project src\Clipton.App\Clipton.App.csproj
```

The app starts in the tray. Double-click the tray icon to open the settings/history window.

## Test

```powershell
dotnet test Clipton.slnx
```

## Local data

Settings and snippets are stored under `%APPDATA%\Clipton`.

- `settings.json`: hotkey, startup, language, and history size settings.
- `snippets.json`: registered text snippets.

Clipboard history is kept in memory by default. Users can enable encrypted local history in the settings window; it is protected with Windows user-scoped DPAPI and stored as `history.dat`.

## Store readiness

The WPF app builds today, but Microsoft Store release work remains. See [docs/STORE_PREP.md](docs/STORE_PREP.md).
