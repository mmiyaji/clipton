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
                    ["Image"] = "Image",
                    ["General"] = "General",
                    ["GeneralDescription"] = "Configure how Clipton starts, opens, and presents commands.",
                    ["History"] = "Clipboard history",
                    ["HistoryDescription"] = "Review captured clipboard items and control local storage.",
                    ["HistoryEmpty"] = "No clipboard history",
                    ["Snippets"] = "Snippets",
                    ["SnippetDescription"] = "Create reusable text entries and paste them from the quick menu.",
                    ["SnippetName"] = "Name",
                    ["SnippetText"] = "Text",
                    ["Settings"] = "Settings",
                    ["PastePlain"] = "Paste plain text",
                    ["PasteOriginal"] = "Paste original",
                    ["ClearHistory"] = "Clear history",
                    ["Save"] = "Save",
                    ["Delete"] = "Delete",
                    ["Exit"] = "Exit",
                    ["Startup"] = "Start with Windows",
                    ["StartupDescription"] = "Launch Clipton automatically after signing in.",
                    ["PauseCapture"] = "Pause capture",
                    ["PersistHistory"] = "Encrypted history",
                    ["FolderMode"] = "Folder mode",
                    ["Hotkey"] = "Global hotkey",
                    ["HotkeyDescription"] = "Shortcut used to open the quick paste menu.",
                    ["Language"] = "Language",
                    ["LanguageDescription"] = "Choose the display language for Clipton."
                }),
                ["ja"] = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>
                {
                    ["AppName"] = "Clipton",
                    ["Image"] = "\u753B\u50CF",
                    ["General"] = "\u5168\u822C",
                    ["GeneralDescription"] = "Clipton \u306E\u8D77\u52D5\u3001\u547C\u3073\u51FA\u3057\u3001\u30B3\u30DE\u30F3\u30C9\u8868\u793A\u3092\u8A2D\u5B9A\u3057\u307E\u3059\u3002",
                    ["History"] = "\u30AF\u30EA\u30C3\u30D7\u30DC\u30FC\u30C9\u5C65\u6B74",
                    ["HistoryDescription"] = "\u53D6\u5F97\u3057\u305F\u30AF\u30EA\u30C3\u30D7\u30DC\u30FC\u30C9\u9805\u76EE\u3068\u30ED\u30FC\u30AB\u30EB\u4FDD\u5B58\u3092\u7BA1\u7406\u3057\u307E\u3059\u3002",
                    ["HistoryEmpty"] = "\u5C65\u6B74\u306F\u3042\u308A\u307E\u305B\u3093",
                    ["Snippets"] = "\u767B\u9332\u5358\u8A9E",
                    ["SnippetDescription"] = "\u7E70\u308A\u8FD4\u3057\u4F7F\u3046\u30C6\u30AD\u30B9\u30C8\u3092\u767B\u9332\u3057\u3001\u30AF\u30A4\u30C3\u30AF\u30E1\u30CB\u30E5\u30FC\u304B\u3089\u8CBC\u308A\u4ED8\u3051\u307E\u3059\u3002",
                    ["SnippetName"] = "\u540D\u524D",
                    ["SnippetText"] = "\u30C6\u30AD\u30B9\u30C8",
                    ["Settings"] = "\u8A2D\u5B9A",
                    ["PastePlain"] = "\u30D7\u30EC\u30FC\u30F3\u30C6\u30AD\u30B9\u30C8\u3067\u8CBC\u308A\u4ED8\u3051",
                    ["PasteOriginal"] = "\u5143\u306E\u5F62\u5F0F\u3067\u8CBC\u308A\u4ED8\u3051",
                    ["ClearHistory"] = "\u5C65\u6B74\u3092\u6D88\u53BB",
                    ["Save"] = "\u4FDD\u5B58",
                    ["Delete"] = "\u524A\u9664",
                    ["Exit"] = "\u7D42\u4E86",
                    ["Startup"] = "Windows \u8D77\u52D5\u6642\u306B\u958B\u59CB",
                    ["StartupDescription"] = "\u30B5\u30A4\u30F3\u30A4\u30F3\u5F8C\u306B Clipton \u3092\u81EA\u52D5\u8D77\u52D5\u3057\u307E\u3059\u3002",
                    ["PauseCapture"] = "\u8A18\u9332\u3092\u4E00\u6642\u505C\u6B62",
                    ["PersistHistory"] = "\u6697\u53F7\u5316\u5C65\u6B74",
                    ["FolderMode"] = "\u30D5\u30A9\u30EB\u30C0\u30FC\u30E2\u30FC\u30C9",
                    ["Hotkey"] = "\u30B0\u30ED\u30FC\u30D0\u30EB\u30DB\u30C3\u30C8\u30AD\u30FC",
                    ["HotkeyDescription"] = "\u30AF\u30A4\u30C3\u30AF\u8CBC\u308A\u4ED8\u3051\u30E1\u30CB\u30E5\u30FC\u3092\u958B\u304F\u30B7\u30E7\u30FC\u30C8\u30AB\u30C3\u30C8\u3067\u3059\u3002",
                    ["Language"] = "\u8A00\u8A9E",
                    ["LanguageDescription"] = "Clipton \u306E\u8868\u793A\u8A00\u8A9E\u3092\u9078\u629E\u3057\u307E\u3059\u3002"
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
