# Microsoft Store Asset Plan

Last updated: 2026-06-28

This file tracks Store submission assets for Clipton.

Current app UI languages are English, Japanese, German, Spanish, French, Korean, and Simplified Chinese. This document still tracks English and Japanese Store-listing screenshots first because those are the maintained listing drafts.

## Official Requirements Checked

Source: Microsoft Learn, "Add app screenshots, images, and trailers for MSIX apps".

- Minimum Store listing content: text description and at least one screenshot.
- Desktop screenshots: PNG, 1366 x 768 pixels or larger, file size under 50 MB.
- Screenshot count: 1 required, up to 10 desktop screenshots, 4 or more recommended.
- Screenshot guidance: keep critical visuals and text in the top two-thirds; do not add extra logos, icons, or marketing messages to screenshots.
- Screenshot captions: 200 characters or fewer.
- Trailer video: MP4 or MOV, 1920 x 1080 pixels, under 2 GB recommended.
- Trailer thumbnail: PNG, 1920 x 1080 pixels.
- Trailer title: 255 characters or fewer.
- Closed captions: WebVTT, under 50 MB.
- 16:9 Super hero art: PNG, 1920 x 1080 or 3840 x 2160 pixels. Recommended for Windows listings. Do not include text.

References:

- https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/add-and-edit-store-listing-info
- https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/screenshots-and-images

## Asset Folders

- Final Partner Center screenshots: `artifacts/store/final/`
- Draft/generated mockups: `artifacts/store/`
- Demo video deliverables: `artifacts/store/video/`
- Store text source: `docs/STORE_LISTING.md`

Important: existing generated screenshots in `artifacts/store/` are useful as layout drafts, but they include marketing-style overlay text. Do not submit them as final screenshots unless they are replaced with actual app UI captures that follow Microsoft guidance.

Recent engineering verification captures for light-mode quick menus:

- `artifacts/light-menu-check/default-light-menu.png`
- `artifacts/light-menu-check/rich-light-menu.png`
- `artifacts/light-menu-check/default-light-image-menu.png`
- `artifacts/light-menu-check/rich-light-image-menu.png`

These captures prove the latest default and rich quick menus render in light mode, including image preview surfaces. They are not final Partner Center screenshots.

## Final Screenshot Shot List

Capture at 1920 x 1080 PNG. Use actual app UI only. Avoid marketing overlays, additional logos, or text banners.

Demo clipboard history data uses safe fake REST/API-style examples based on JSONPlaceholder public test data patterns, plus generated local image and snippet variable examples.

### English Listing

1. `01-quick-menu-en.png`
   - Show Notepad or another neutral text target with Clipton quick menu open.
   - Highlight visible history rows, paste options, and either default or rich menu mode.
   - Capture both light and dark theme variants internally before choosing the final Store screenshot.

2. `02-history-en.png`
   - Show the Clipboard history tab.
   - Include search input, history rows, timestamps, import/export buttons, and clear-history button.

3. `03-snippets-en.png`
   - Show the Registered snippets tab.
   - Include folder tree, snippet editor, variable sample buttons, and variable reference link.

4. `04-history-settings-en.png`
   - Show history settings.
   - Include encrypted history, saved history limit, capture delay, masking controls, and diagnostic logging.

5. `05-about-privacy-en.png`
   - Show local-first privacy note, terms/privacy links, author link, and app exit action.

### Japanese Listing

Use the same five captures with Japanese UI:

1. `01-quick-menu-ja.png`
2. `02-history-ja.png`
3. `03-snippets-ja.png`
4. `04-history-settings-ja.png`
5. `05-about-privacy-ja.png`

## Screenshot Captions

Use the captions in `docs/STORE_LISTING.md`. They are within the 200-character limit.

## Demo Trailer

Store title:

- English: `Clipton quick clipboard workflow`
- Japanese: `Clipton クイック貼り付けワークフロー`

File targets:

- `artifacts/store/video/clipton-store-trailer.mp4`
- `artifacts/store/video/clipton-store-trailer-thumbnail.png`
- `artifacts/store/video/clipton-store-trailer.en.vtt`
- `artifacts/store/video/clipton-store-trailer.ja.vtt`

Technical targets:

- Resolution: 1920 x 1080.
- Duration: 45 to 60 seconds.
- Format: MP4, H.264 video, AAC audio if narration is included.
- No age rating badges embedded in the Partner Center trailer.
- Thumbnail: PNG, 1920 x 1080, taken from the quick menu scene.

### Storyboard

1. 0:00-0:05 - First launch / tray resident behavior.
   - Show Clipton icon and settings window.
   - Message: Clipton runs locally from the task tray.

2. 0:05-0:15 - Quick menu.
   - Focus Notepad, press Shift+Alt+V, open quick menu.
   - Move with arrow keys, show Enter paste and plain text shortcut hint.

3. 0:15-0:25 - Search and paste options.
   - Open incremental search.
   - Right-click or use the paste options submenu.
   - Show plain text, no line breaks, uppercase/lowercase, JSON, URL extraction.

4. 0:25-0:35 - Clipboard history.
   - Open Clipboard history tab.
   - Show search, timestamps, import/export, image preview, and clear confirmation.

5. 0:35-0:45 - Registered snippets.
   - Show snippets tree.
   - Add or select a slash-separated folder snippet.
   - Show variable samples such as `{{date}}` and `{{time}}`.

6. 0:45-0:55 - Privacy controls.
   - Show encrypted history, pause capture, custom mask settings, and local log mode.
   - End with the app remaining in the tray.

## Voiceover Draft

### English

Clipton is a local-first clipboard manager for Windows. Press the global hotkey to open recent text, images, files, and registered snippets without leaving the keyboard.

Search your history, choose paste options such as plain text or JSON formatting, and keep reusable messages organized in folders.

Clipboard history stays on this device. You can pause capture, mask protected content, export or clear history, and control startup behavior from settings.

### Japanese

Clipton は Windows 向けのローカル重視クリップボード管理アプリです。グローバルホットキーで、最近のテキスト、画像、ファイル、登録単語をキーボードからすばやく呼び出せます。

履歴検索、プレーンテキスト貼り付け、JSON 整形、URL 抽出などの貼り付けオプションを選べます。よく使う文章はフォルダで整理できます。

クリップボード履歴はこのデバイスに保存されます。記録停止、保護対象のマスク、履歴のエクスポートや削除、起動設定を設定画面から制御できます。

## WebVTT Draft

### English

```vtt
WEBVTT

00:00.000 --> 00:05.000
Clipton runs locally from the Windows task tray.

00:05.000 --> 00:15.000
Press the global hotkey to open clipboard history and snippets.

00:15.000 --> 00:25.000
Search, choose paste options, and paste text in the format you need.

00:25.000 --> 00:35.000
Review timestamps, images, import, export, and clear history from settings.

00:35.000 --> 00:45.000
Keep reusable snippets in folders and insert dynamic variables.

00:45.000 --> 00:55.000
Pause capture, mask protected content, and keep data local.
```

### Japanese

```vtt
WEBVTT

00:00.000 --> 00:05.000
Clipton は Windows のタスクトレイからローカルで動作します。

00:05.000 --> 00:15.000
グローバルホットキーで履歴と登録単語を呼び出せます。

00:15.000 --> 00:25.000
検索し、必要な貼り付けオプションを選択できます。

00:25.000 --> 00:35.000
時刻、画像、インポート、エクスポート、履歴削除を確認できます。

00:35.000 --> 00:45.000
登録単語はフォルダで整理し、動的変数も使えます。

00:45.000 --> 00:55.000
記録停止、保護対象のマスク、ローカル保存を制御できます。
```

## Submission Checklist

- Verify final screenshots are actual app captures, not marketing mockups.
- Confirm every screenshot is PNG, at least 1366 x 768, under 50 MB.
- Upload English and Japanese screenshots separately in each Store listing.
- Use captions from `docs/STORE_LISTING.md`.
- Verify quick menu screenshots reflect the latest light/dark theme behavior and the current default `Shift+Alt+V` hotkey.
- Verify UI language screenshots use the selected listing language consistently.
- If a trailer is uploaded, include MP4/MOV video, PNG thumbnail, title, and optional VTT captions.
- Confirm privacy and terms URLs are public before submission.
- Run Windows App Certification Kit before Partner Center upload.
