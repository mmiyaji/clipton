# Microsoft Store Listing Draft

Last updated: 2026-05-21

Microsoft Partner Center requires Store listing text and at least one screenshot. Microsoft recommends adding multiple screenshots for each supported device type so customers can understand the app before installing it.

References:

- Store listing info: <https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/add-and-edit-store-listing-info>
- Screenshots and images: <https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/screenshots-and-images>

## Product Identity

- Product name: Clipton
- Category: Productivity
- Target device family: PC / Windows Desktop
- Supported languages: English, Japanese
- Privacy policy URL: <https://mmiyaji.github.io/clipton/privacy/>
- Terms of use URL: <https://mmiyaji.github.io/clipton/terms/>

## English Listing

### Short Description

Local-first clipboard history and quick paste for Windows.

### Description

Clipton is a local-first clipboard manager for Windows. It keeps recent clipboard history, reusable snippets, and paste commands available from a keyboard-driven quick menu.

Use Clipton to search recent clipboard items, paste in the original format, paste text as plain text, organize older history into folders, and keep frequently used messages as registered snippets.

Clipton is designed for privacy-sensitive daily work. Clipboard history is stored locally, encrypted with Windows user-scoped data protection, and can be paused, cleared, or disabled from settings. The current version does not include cloud sync, telemetry, crash reporting, advertising SDKs, or third-party data sharing.

### Feature Bullets

- Open clipboard history with a global hotkey.
- Paste text, rich text, HTML, images, and file drops.
- Paste text as plain text with a keyboard shortcut.
- Search clipboard history and registered snippets.
- Organize snippets and older history with folders.
- Mask sensitive-looking clipboard content in lists.
- Store encrypted clipboard history locally.
- Configure hotkey, startup, theme, language, and saved history count.

### Keywords

clipboard, clipboard manager, clipboard history, paste, snippets, productivity, local, privacy, hotkey

### Release Notes

Initial Microsoft Store release candidate with WinUI interface, global hotkey menu, encrypted local history, snippets, folders, search, sensitive-content masking, and configurable saved history count.

## Japanese Listing

### Short Description

Windows 向けのローカル重視クリップボード履歴とクイック貼り付け。

### Description

Clipton は Windows 向けのローカル重視クリップボード管理アプリです。最近のクリップボード履歴、登録メッセージ、貼り付け操作を、キーボード中心のクイックメニューから呼び出せます。

履歴検索、元の形式での貼り付け、プレーンテキスト貼り付け、古い履歴のフォルダ整理、よく使う文面の登録に対応しています。

Clipton はプライバシーに配慮した日常作業向けに設計しています。クリップボード履歴はローカルに保存され、Windows のユーザー単位データ保護で暗号化されます。記録の一時停止、履歴消去、永続化の無効化も設定から行えます。現在のバージョンにはクラウド同期、テレメトリ、クラッシュレポート、広告 SDK、第三者へのデータ共有はありません。

### Feature Bullets

- グローバルホットキーで履歴メニューを表示。
- テキスト、リッチテキスト、HTML、画像、ファイル履歴に対応。
- キーボード操作でプレーンテキスト貼り付け。
- クリップボード履歴と登録メッセージを検索。
- 登録メッセージと古い履歴をフォルダで整理。
- 機密っぽい内容を一覧で自動マスク。
- 履歴をローカルに暗号化保存。
- ホットキー、起動、テーマ、言語、保存履歴数を設定可能。

### Keywords

クリップボード, 履歴, 貼り付け, スニペット, 登録単語, 生産性, ローカル, プライバシー, ホットキー

### Release Notes

WinUI インターフェイス、グローバルホットキーメニュー、暗号化ローカル履歴、登録メッセージ、フォルダ、検索、機密内容マスク、保存履歴数設定を備えた Microsoft Store 初回公開候補です。

## Screenshot Set

Recommended PC screenshots are generated under `artifacts/store/`.

- `01-quick-menu-en.png`
- `02-history-en.png`
- `03-snippets-en.png`
- `04-settings-en.png`
- `05-privacy-en.png`
- `01-quick-menu-ja.png`
- `02-history-ja.png`
- `03-snippets-ja.png`
- `04-settings-ja.png`
- `05-privacy-ja.png`

All files are 1920 x 1080 PNG screenshots. Regenerate them with:

```powershell
dotnet run --project tools\StoreScreenshots\StoreScreenshots.csproj
```

Keep screenshots aligned with the shipping WinUI UI. Regenerate them after visual or major feature changes.
