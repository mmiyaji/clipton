# Microsoft Store Listing Draft

Last updated: 2026-07-10

Use this document as the source text for Partner Center Store listings.

Official references:

- Store listing info: https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/add-and-edit-store-listing-info
- Screenshots, images, and trailers: https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/screenshots-and-images

## Product Identity

- Product name: Clipton
- Release candidate: 0.1.19
- Category: Productivity
- Target device family: PC / Windows Desktop
- Store listing draft languages: English, Japanese
- App UI languages: English, Japanese, German, Spanish, French, Korean, Simplified Chinese
- Privacy policy URL: https://mmiyaji.github.io/clipton/privacy/
- Terms of use URL: https://mmiyaji.github.io/clipton/terms/

## English Listing

### Short Description

Local-first clipboard history and quick paste for Windows.

### Description

Clipton is a local-first clipboard manager for Windows. It keeps recent clipboard history, reusable snippets, and paste commands available from a keyboard-driven quick menu.

Use Clipton to search recent clipboard items, paste in the original format, paste text as plain text, preview images, organize older history into folders, and keep frequently used messages as registered snippets. Choose the compact default quick menu or the richer thumbnail menu, and use light, dark, or system theme mode.

Clipton is designed for privacy-sensitive daily work. Clipboard history is stored locally, encrypted with Windows user-scoped data protection, and can be paused, cleared, exported, PIN-locked, or disabled from settings. The current version does not include cloud sync, telemetry, crash reporting, advertising SDKs, or third-party data sharing.

### Feature Bullets

- Open clipboard history with a global hotkey.
- Paste text, rich text, HTML, images, and file drops.
- Paste text as plain text with a keyboard shortcut.
- Choose compact or rich quick menu styles with thumbnail and image preview support.
- Search clipboard history and registered snippets.
- Organize snippets and older history with folders.
- Mask sensitive-looking clipboard content in lists.
- Require a PIN before showing history and registered snippets.
- Export and import clipboard history and registered snippets.
- Store encrypted clipboard history locally.
- Configure hotkey, startup, light/dark/system theme, language, and saved history count.
- Use the app in English, Japanese, German, Spanish, French, Korean, or Simplified Chinese.

### Keywords

clipboard, clipboard manager, clipboard history, paste, snippets, productivity, local, privacy, hotkey

### Release Notes

Version 0.1.19 strengthens clipboard-history safety and accessibility. Full exports now include encrypted history that is not currently loaded and stop instead of producing a partial backup when stored data cannot be read completely. Paste actions verify both the clipboard write and the original target window before sending Ctrl+V. Quick menu scaling, text filtering, High Contrast colors, and screen-reader automation were improved. Store packages now include all seven UI languages.

## Japanese Listing

### Short Description

Windows 向けのローカル重視クリップボード履歴とクイック貼り付け。

### Description

Clipton は Windows 向けのローカル重視クリップボード管理アプリです。最近のクリップボード履歴、登録単語、貼り付け操作を、キーボード中心のクイックメニューから呼び出せます。

履歴検索、元の形式での貼り付け、プレーンテキスト貼り付け、画像プレビュー、古い履歴のフォルダ整理、よく使う文章の登録単語化に対応しています。クイックメニューはコンパクトな標準表示と、サムネイルを使うリッチ表示から選べます。ライト、ダーク、システム連動のテーマにも対応しています。

Clipton はプライバシーに配慮した日常作業向けに設計しています。クリップボード履歴はローカルに保存され、Windows のユーザー単位データ保護で暗号化されます。記録の一時停止、履歴消去、エクスポート、PIN ロック、永続化の無効化も設定から行えます。現在のバージョンにはクラウド同期、テレメトリ、クラッシュレポート、広告 SDK、第三者へのデータ共有はありません。

### Feature Bullets

- グローバルホットキーでクリップボード履歴を表示。
- テキスト、リッチテキスト、HTML、画像、ファイル履歴を貼り付け。
- キーボード操作でプレーンテキスト貼り付け。
- 標準/リッチのクイックメニューを選択し、サムネイルや画像プレビューを表示。
- クリップボード履歴と登録単語を検索。
- 登録単語と古い履歴をフォルダで整理。
- 保護対象の内容を一覧でマスク表示。
- 履歴と登録単語の表示前に PIN を要求。
- 履歴と登録単語のエクスポート、インポート。
- 履歴をローカルに暗号化保存。
- ホットキー、起動、ライト/ダーク/システムテーマ、言語、保存履歴数を設定。
- 英語、日本語、ドイツ語、スペイン語、フランス語、韓国語、中国語（簡体字）の UI に対応。

### Keywords

クリップボード, クリップボード履歴, 貼り付け, 登録単語, スニペット, 生産性, ローカル, プライバシー, ホットキー

### Release Notes

バージョン 0.1.19 では、履歴の安全性とアクセシビリティを強化しました。エクスポートは未読込の暗号化履歴も対象とし、保存データを完全に読めない場合は不完全なバックアップを作らず停止します。貼り付け前にクリップボード書き込みと元の対象ウィンドウを確認するようにしました。クイックメニューの高 DPI 表示、テキスト絞り込み、ハイコントラスト配色、スクリーンリーダー向け情報も改善し、Store パッケージには 7 言語の UI をすべて収録しています。

## Screenshot Captions

Upload screenshots separately for English and Japanese listings. Captions must be 200 characters or fewer.

### English

- `01-quick-menu-en.png`: Open recent clipboard items from a keyboard-friendly quick menu.
- `02-history-en.png`: Search, review, import, export, and clear clipboard history.
- `03-snippets-en.png`: Keep reusable snippets in folders and insert dynamic variables such as date, time, and UUID.
- `04-settings-en.png`: Configure encrypted history, capture behavior, masking, diagnostics, and local retention.
- `05-privacy-en.png`: Local encrypted storage, capture pause, masking, and cleanup controls.

### Japanese

- `01-quick-menu-ja.png`: キーボードで最近のクリップボード項目をすばやく呼び出せます。
- `02-history-ja.png`: 履歴の検索、確認、インポート、エクスポート、削除を行えます。
- `03-snippets-ja.png`: 登録単語をフォルダで整理し、日付、時刻、UUID などの動的変数を挿入できます。
- `04-settings-ja.png`: 暗号化履歴、取得動作、マスク、診断ログ、保存件数を設定できます。
- `05-privacy-ja.png`: ローカル暗号化保存、記録停止、マスク、削除を制御できます。
