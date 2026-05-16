using System.Collections.ObjectModel;

namespace Clipton.Core;

public sealed class LocalizationCatalog
{
    private readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> _resources;

    public LocalizationCatalog()
    {
        _resources = new ReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>(
            new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["en"] = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>
                {
                    ["AppName"] = "Clipton",
                    ["History"] = "Clipboard history",
                    ["HistoryEmpty"] = "No clipboard history",
                    ["Snippets"] = "Snippets",
                    ["Settings"] = "Settings",
                    ["PastePlain"] = "Paste plain text",
                    ["PasteOriginal"] = "Paste original",
                    ["ClearHistory"] = "Clear history",
                    ["Save"] = "Save",
                    ["Delete"] = "Delete",
                    ["Exit"] = "Exit",
                    ["Startup"] = "Start with Windows",
                    ["PauseCapture"] = "Pause capture",
                    ["PersistHistory"] = "Encrypted history",
                    ["Hotkey"] = "Global hotkey",
                    ["Language"] = "Language"
                }),
                ["ja"] = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>
                {
                    ["AppName"] = "Clipton",
                    ["History"] = "クリップボード履歴",
                    ["HistoryEmpty"] = "履歴はありません",
                    ["Snippets"] = "登録単語",
                    ["Settings"] = "設定",
                    ["PastePlain"] = "プレーンテキストで貼り付け",
                    ["PasteOriginal"] = "元の形式で貼り付け",
                    ["ClearHistory"] = "履歴を消去",
                    ["Save"] = "保存",
                    ["Delete"] = "削除",
                    ["Exit"] = "終了",
                    ["Startup"] = "Windows 起動時に開始",
                    ["PauseCapture"] = "記録を一時停止",
                    ["PersistHistory"] = "暗号化履歴",
                    ["Hotkey"] = "グローバルホットキー",
                    ["Language"] = "言語"
                })
            });
    }

    public string Translate(string locale, string key)
    {
        if (_resources.TryGetValue(locale, out var localized) && localized.TryGetValue(key, out var value))
        {
            return value;
        }

        return _resources["en"].TryGetValue(key, out var fallback) ? fallback : key;
    }
}
