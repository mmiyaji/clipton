# Clipton

Clipton is a Windows resident clipboard manager inspired by Clipy and CLCL.

## Current features

- WinUI resident app with system tray icon.
- Global hotkey menu, default `Ctrl+Shift+V`.
- Clipboard history capture for text, RTF, HTML, images, and file drops.
- Paste history items in the original clipboard format when possible.
- Paste text items as plain text.
- Snippet insertion and snippet add/update/delete with folder support.
- Configurable hotkey, startup registration, theme, and UI language.
- Pause capture, clear history, sensitive-content masking, and encrypted local history.
- English and Japanese UI strings.
- Core unit tests for history, hotkeys, settings, and localization.

## Run

```powershell
dotnet run --project src\Clipton.WinUI\Clipton.WinUI.csproj
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

Clipboard history is encrypted and persisted locally by default. Users can disable encrypted local history in the settings window; when enabled, it is protected with Windows user-scoped DPAPI and stored as `history.dat`.

## Store readiness

The WinUI app builds today, and the packaging project targets the WinUI executable. Microsoft Store submission still requires Partner Center identity association, signing, Store listing metadata, and clean-profile package validation. See [docs/STORE_PREP.md](docs/STORE_PREP.md).
