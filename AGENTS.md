# AGENTS.md

Clipton（WinUI 3 のクリップボード管理アプリ）で作業するときの最低限のルールです。

## 構成

- `src/Clipton.Core`：履歴・設定・暗号化などの共通ロジック
- `src/Clipton.WinUI`：WinUI 3 アプリ本体（Store 版と開発ビルドの対象）
- `src/Clipton.App`：WPF 版
- `tests/`：Core / App / WinUI の各テストプロジェクト
- `packaging/Clipton.Package`：MSIX（Store 用）パッケージ
- `docs/`：プライバシー・利用規約・Store 掲載・検証メモ（GitHub Pages で公開）
- `tools/`：スクリーンショット生成・アイコン生成などの補助スクリプト

## よく使うコマンド

```
dotnet test Clipton.slnx
dotnet run --project src\Clipton.WinUI\Clipton.WinUI.csproj
```

ポータブル版のビルドは `README.md` の「Portable build」を参照してください。

## 変更時のルール

- 変更後は `dotnet test Clipton.slnx` を通してから報告する。
- 利用者向けの変更は `CHANGELOG.md` に記載する。
- `artifacts/` と `.env` はコミットしない。作業用の一時ファイルは `artifacts/` 以下に置く。
- main への直接コミットはしない。作業は `<type>/<短い説明>` のブランチ（type はコミットの prefix と同じ add / update / fix / refactor）で行い、完了後に `--no-ff` で main にマージしてブランチを削除する。
- 区切りのよいところで適宜コミットする。
- push は利用者の許可を得てから行う。「リリースしてください」の指示には push の許可を含むものとする。

## TODO の管理

- 直近の作業は `docs/TODO.md` で管理する。
- 大きめの作業・長期の要望は GitHub Issues で管理する。
- 作業を始めるとき・終えるとき、該当する方を更新する。
