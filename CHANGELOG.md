# Changelog

Clipton の `0.1.9` 以降の主な変更点をまとめます。

このファイルは今後のリリースごとにメンテする前提です。新しい変更はまず `Unreleased` に追記し、リリース時に該当バージョンへ移してください。

> Note: このリポジトリには現時点で Git tag / GitHub Releases がないため、バージョン境界は `Clipton.WinUI.csproj` と `Package.appxmanifest` の version bump コミットから推定しています。

## Unreleased

### Added

- Quick menu の詳細操作をキーボードで選択して Enter する E2E 検証を追加しました。
- 詳細設定から Quick menu の詳細メニュー表示項目を選べるようにしました。

### Changed

- テストプロジェクトのテスト基盤依存を更新し、既知の脆弱な推移的依存を解消しました。

## 0.1.14 - 2026-06-19

### Changed

- アプリと Store パッケージのバージョンを `0.1.14` / `0.1.14.0` に更新しました。

### Fixed

- 言語切り替え直後に、設定画面のドロップダウン選択表示が前の言語のまま残る問題を修正しました。
- 寄付ページの説明、リンク、画像フォールバック文言が言語切り替えに追従するようにしました。
- ライトテーマで About / Donation 画面の一部説明テキストが背景に埋まる問題を修正しました。
- Quick menu の詳細操作をキーボードで選択して Enter を押したとき、選択中の詳細操作ではなく通常貼り付けが実行される問題を修正しました。

## 0.1.12 - 2026-06-17

### Added

- README に Clipton の概要説明画像を追加しました。
- 履歴の由来メタ情報を保持し、quick menu と履歴画面で参照できるようにしました。
- Quick menu から履歴/登録語をクイック編集して、そのまま貼り付けできるようにしました。
- Quick menu のトップレベル項目を数字キーで即貼り付け、Space でプレビュー、E で編集できるショートカットを追加しました。
- 画像プレビューをキーボード操作でも閉じやすいように、閉じるボタンを追加しました。

### Changed

- デフォルトのグローバルホットキーを `Ctrl+Alt+V` に変更しました。
- 起動時にホットキーが競合した場合、`Ctrl+Alt+Space`、`Ctrl+Shift+V` の順にプリセットへフォールバックするようにしました。
- Quick menu の default / rich ウィンドウを表示ごとに破棄せず、キャッシュして再利用するようにしました。
- Quick menu を開く直前に clipboard を短時間だけ更新し、直前にコピーした内容が履歴に反映されやすくしました。

### Fixed

- 永続履歴の遅延ロード時に、`History` の変更と列挙を UI thread に寄せ、background load と UI 更新の競合を避けるようにしました。
- Quick menu を再オープンした直後に古い dismiss cleanup が走って、新しい menu まで閉じてしまうケースを防ぎました。
- Default quick menu の初回表示位置を安定化し、初回起動時に上へ戻される挙動を抑えました。
- Quick menu 起動直後に下キーを押した場合、遅延フォーカス復元が選択位置を先頭へ戻さないようにしました。

## 0.1.11 - 2026-06-12

### Added

- Quick menu に launcher-style のインクリメンタル検索を追加しました。
- Quick menu の keyboard navigation を検証する E2E スクリプトを追加しました。
- 既に起動中の Clipton がある場合に既存インスタンスを利用する単一インスタンス制御を追加しました。

### Changed

- Quick menu の検索 UI を rich window に統合し、ライブフィルタリングできるようにしました。
- Quick menu の search toolbar button が検索中に active 表示になるようにしました。
- default quick menu で `>` chevron をフォルダ専用にし、paste options は `...` として区別しやすくしました。
- Quick menu フォルダ内で Right key から paste options を開けるようにしました。
- Clipboard capture と履歴永続化を UI thread から分離し、操作中の固まりを軽減しました。
- 履歴一覧の行を再利用し、更新時に毎回全体を作り直さないようにしました。
- Clipboard text extraction を `Clipton.Core` 側に共通化し、テストを追加しました。

### Fixed

- Quick menu の keyboard focus が不安定になる問題を修正しました。
- Rich quick menu の Left / Right folder navigation を修正しました。
- scroll による hover が rich menu の keyboard selection を奪う問題を修正しました。
- Window size と chrome の設定順を調整し、quick menu 表示時の flash を避けるようにしました。
- UI thread に `DispatcherQueueSynchronizationContext` を設定し、WinUI 周りの async 継続を安定化しました。
- Quick menu の async path と clipboard retry exhaustion 周りを堅牢化しました。
- `ClipboardBridge` tests から real clipboard 依存を取り除きました。

## 0.1.10 - 2026-06-11

### Changed

- Quick menu の画像プレビュー体験を改善しました。
- Quick menu のタイトルヘッダーを整理しました。

### Fixed

- Quick menu の folder navigation を安定化しました。
- 空の quick menu folder を開いたときの挙動を修正しました。

## 0.1.9 - 2026-06-10

### Changed

- アプリと package のバージョンを `0.1.9` に更新しました。
- Quick menu の先頭に Clipton のタイトル行を追加しました。
- 画像プレビューの枠線や余白を抑え、画像そのものを見やすくしました。
