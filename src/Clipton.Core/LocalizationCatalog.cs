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
                    ["ActivationSection"] = "Activation",
                    ["History"] = "Clipboard history",
                    ["HistoryDescription"] = "Review captured clipboard items and control local storage.",
                    ["HistorySection"] = "History",
                    ["CapturePrivacySection"] = "Capture and privacy",
                    ["HistoryEmpty"] = "No clipboard history",
                    ["Search"] = "Search",
                    ["SearchHistory"] = "Search history",
                    ["SearchPrompt"] = "Enter a keyword to search.",
                    ["SearchResults"] = "Search results for \"{0}\"",
                    ["ClearSearch"] = "Clear search",
                    ["LoadMoreHistory"] = "Show more ({0} remaining)",
                    ["NoSearchResults"] = "No matching history",
                    ["Snippets"] = "Snippets",
                    ["SnippetDescription"] = "Create reusable text entries and paste them from the quick menu.",
                    ["SnippetEditor"] = "Snippet editor",
                    ["SnippetEditorEmpty"] = "Select a snippet to edit it, or create a new reusable text entry.",
                    ["SnippetFolder"] = "Folder",
                    ["SnippetName"] = "Name",
                    ["SnippetText"] = "Text",
                    ["NewSnippet"] = "New",
                    ["EditSnippet"] = "Edit",
                    ["RegisterFromHistory"] = "Register selected history",
                    ["Settings"] = "Settings",
                    ["Paste"] = "Paste",
                    ["PastePlain"] = "Paste plain text",
                    ["PasteOriginal"] = "Paste original",
                    ["ClearHistory"] = "Clear history",
                    ["Save"] = "Save",
                    ["Cancel"] = "Cancel",
                    ["Delete"] = "Delete",
                    ["Exit"] = "Exit",
                    ["Startup"] = "Start with Windows",
                    ["StartupDescription"] = "Launch Clipton automatically after signing in.",
                    ["PauseCapture"] = "Pause capture",
                    ["PauseCaptureDescription"] = "Temporarily stop recording new clipboard changes.",
                    ["PersistHistory"] = "Encrypted history",
                    ["PersistHistoryDescription"] = "Store history locally using Windows data protection.",
                    ["MaskSensitiveContent"] = "Mask sensitive-looking content",
                    ["MaskSensitiveContentDescription"] = "Hide passwords, tokens, and registered message contents in lists.",
                    ["MaskedSensitive"] = "Masked sensitive content",
                    ["RegisteredSnippetMasked"] = "Registered message content masked",
                    ["FolderMode"] = "Folder mode",
                    ["FolderModeDescription"] = "Keep the newest items visible and move older history into folders.",
                    ["SimpleContextMenuMode"] = "Simple context menu",
                    ["SimpleContextMenuModeDescription"] = "Use the compact WinUI context menu for the quick paste surface.",
                    ["PastePlainMenu"] = "Paste as plain text",
                    ["PasteOriginalMenu"] = "Paste original",
                    ["Hotkey"] = "Global hotkey",
                    ["HotkeyDescription"] = "Shortcut used to open the quick paste menu.",
                    ["Language"] = "Language",
                    ["LanguageDescription"] = "Choose the display language for Clipton.",
                    ["LanguageSystem"] = "Use system setting",
                    ["Theme"] = "Theme",
                    ["ThemeDescription"] = "Choose the window color theme.",
                    ["ThemeSystem"] = "Use system setting",
                    ["ThemeLight"] = "Light",
                    ["ThemeDark"] = "Dark"
                }),
                ["ja"] = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>
                {
                    ["AppName"] = "Clipton",
                    ["Image"] = "\u753B\u50CF",
                    ["General"] = "\u5168\u822C",
                    ["GeneralDescription"] = "Clipton \u306E\u8D77\u52D5\u3001\u547C\u3073\u51FA\u3057\u3001\u30B3\u30DE\u30F3\u30C9\u8868\u793A\u3092\u8A2D\u5B9A\u3057\u307E\u3059\u3002",
                    ["ActivationSection"] = "\u547C\u3073\u51FA\u3057",
                    ["History"] = "\u30AF\u30EA\u30C3\u30D7\u30DC\u30FC\u30C9\u5C65\u6B74",
                    ["HistoryDescription"] = "\u53D6\u5F97\u3057\u305F\u30AF\u30EA\u30C3\u30D7\u30DC\u30FC\u30C9\u9805\u76EE\u3068\u30ED\u30FC\u30AB\u30EB\u4FDD\u5B58\u3092\u7BA1\u7406\u3057\u307E\u3059\u3002",
                    ["HistorySection"] = "\u5C65\u6B74",
                    ["CapturePrivacySection"] = "\u8A18\u9332\u3068\u30D7\u30E9\u30A4\u30D0\u30B7\u30FC",
                    ["HistoryEmpty"] = "\u5C65\u6B74\u306F\u3042\u308A\u307E\u305B\u3093",
                    ["Search"] = "\u691C\u7D22",
                    ["SearchHistory"] = "\u5C65\u6B74\u3092\u691C\u7D22",
                    ["SearchPrompt"] = "\u691C\u7D22\u3059\u308B\u30AD\u30FC\u30EF\u30FC\u30C9\u3092\u5165\u529B\u3057\u3066\u304F\u3060\u3055\u3044\u3002",
                    ["SearchResults"] = "\u300C{0}\u300D\u306E\u691C\u7D22\u7D50\u679C",
                    ["ClearSearch"] = "\u691C\u7D22\u3092\u89E3\u9664",
                    ["LoadMoreHistory"] = "\u3055\u3089\u306B\u8868\u793A\uFF08\u6B8B\u308A {0} \u4EF6\uFF09",
                    ["NoSearchResults"] = "\u4E00\u81F4\u3059\u308B\u5C65\u6B74\u306F\u3042\u308A\u307E\u305B\u3093",
                    ["Snippets"] = "\u767B\u9332\u5358\u8A9E",
                    ["SnippetDescription"] = "\u7E70\u308A\u8FD4\u3057\u4F7F\u3046\u30C6\u30AD\u30B9\u30C8\u3092\u767B\u9332\u3057\u3001\u30AF\u30A4\u30C3\u30AF\u30E1\u30CB\u30E5\u30FC\u304B\u3089\u8CBC\u308A\u4ED8\u3051\u307E\u3059\u3002",
                    ["SnippetEditor"] = "\u767B\u9332\u5358\u8A9E\u306E\u7DE8\u96C6",
                    ["SnippetEditorEmpty"] = "\u767B\u9332\u5358\u8A9E\u3092\u9078\u3093\u3067\u7DE8\u96C6\u3059\u308B\u304B\u3001\u65B0\u3057\u3044\u5185\u5BB9\u3092\u767B\u9332\u3057\u307E\u3059\u3002",
                    ["SnippetFolder"] = "\u30D5\u30A9\u30EB\u30C0\u30FC",
                    ["SnippetName"] = "\u540D\u524D",
                    ["SnippetText"] = "\u30C6\u30AD\u30B9\u30C8",
                    ["NewSnippet"] = "\u65B0\u898F",
                    ["EditSnippet"] = "\u7DE8\u96C6",
                    ["RegisterFromHistory"] = "\u9078\u629E\u3057\u305F\u5C65\u6B74\u3092\u767B\u9332",
                    ["Settings"] = "\u8A2D\u5B9A",
                    ["Paste"] = "\u8CBC\u308A\u4ED8\u3051",
                    ["PastePlain"] = "\u30D7\u30EC\u30FC\u30F3\u30C6\u30AD\u30B9\u30C8\u3067\u8CBC\u308A\u4ED8\u3051",
                    ["PasteOriginal"] = "\u5143\u306E\u5F62\u5F0F\u3067\u8CBC\u308A\u4ED8\u3051",
                    ["ClearHistory"] = "\u5C65\u6B74\u3092\u6D88\u53BB",
                    ["Save"] = "\u4FDD\u5B58",
                    ["Cancel"] = "\u30AD\u30E3\u30F3\u30BB\u30EB",
                    ["Delete"] = "\u524A\u9664",
                    ["Exit"] = "\u7D42\u4E86",
                    ["Startup"] = "Windows \u8D77\u52D5\u6642\u306B\u958B\u59CB",
                    ["StartupDescription"] = "\u30B5\u30A4\u30F3\u30A4\u30F3\u5F8C\u306B Clipton \u3092\u81EA\u52D5\u8D77\u52D5\u3057\u307E\u3059\u3002",
                    ["PauseCapture"] = "\u8A18\u9332\u3092\u4E00\u6642\u505C\u6B62",
                    ["PauseCaptureDescription"] = "\u65B0\u3057\u3044\u30AF\u30EA\u30C3\u30D7\u30DC\u30FC\u30C9\u5909\u66F4\u306E\u8A18\u9332\u3092\u4E00\u6642\u7684\u306B\u6B62\u3081\u307E\u3059\u3002",
                    ["PersistHistory"] = "\u6697\u53F7\u5316\u5C65\u6B74",
                    ["PersistHistoryDescription"] = "Windows \u306E\u30C7\u30FC\u30BF\u4FDD\u8B77\u3092\u4F7F\u3063\u3066\u5C65\u6B74\u3092\u30ED\u30FC\u30AB\u30EB\u306B\u4FDD\u5B58\u3057\u307E\u3059\u3002",
                    ["MaskSensitiveContent"] = "\u6A5F\u5BC6\u3063\u307D\u3044\u5185\u5BB9\u3092\u30DE\u30B9\u30AF",
                    ["MaskSensitiveContentDescription"] = "\u30D1\u30B9\u30EF\u30FC\u30C9\u3001\u30C8\u30FC\u30AF\u30F3\u3001\u767B\u9332\u30E1\u30C3\u30BB\u30FC\u30B8\u306E\u5185\u5BB9\u3092\u4E00\u89A7\u3067\u96A0\u3057\u307E\u3059\u3002",
                    ["MaskedSensitive"] = "\u6A5F\u5BC6\u3063\u307D\u3044\u5185\u5BB9\uFF08\u30DE\u30B9\u30AF\u6E08\u307F\uFF09",
                    ["RegisteredSnippetMasked"] = "\u767B\u9332\u30E1\u30C3\u30BB\u30FC\u30B8\u306E\u5185\u5BB9\u306F\u30DE\u30B9\u30AF\u6E08\u307F",
                    ["FolderMode"] = "\u30D5\u30A9\u30EB\u30C0\u30FC\u30E2\u30FC\u30C9",
                    ["FolderModeDescription"] = "\u6700\u65B0\u306E\u5C65\u6B74\u3060\u3051\u3092\u8868\u793A\u3057\u3001\u53E4\u3044\u5C65\u6B74\u3092\u30D5\u30A9\u30EB\u30C0\u30FC\u306B\u307E\u3068\u3081\u307E\u3059\u3002",
                    ["SimpleContextMenuMode"] = "\u30B7\u30F3\u30D7\u30EB\u30B3\u30F3\u30C6\u30AD\u30B9\u30C8\u30E1\u30CB\u30E5\u30FC",
                    ["SimpleContextMenuModeDescription"] = "\u30AF\u30A4\u30C3\u30AF\u8CBC\u308A\u4ED8\u3051\u3092 WinUI \u306E\u30B3\u30F3\u30D1\u30AF\u30C8\u306A\u30E1\u30CB\u30E5\u30FC\u3067\u8868\u793A\u3057\u307E\u3059\u3002",
                    ["PastePlainMenu"] = "\u30D7\u30EC\u30FC\u30F3\u30C6\u30AD\u30B9\u30C8\u3067\u8CBC\u308A\u4ED8\u3051",
                    ["PasteOriginalMenu"] = "\u5143\u306E\u5F62\u5F0F\u3067\u8CBC\u308A\u4ED8\u3051",
                    ["Hotkey"] = "\u30B0\u30ED\u30FC\u30D0\u30EB\u30DB\u30C3\u30C8\u30AD\u30FC",
                    ["HotkeyDescription"] = "\u30AF\u30A4\u30C3\u30AF\u8CBC\u308A\u4ED8\u3051\u30E1\u30CB\u30E5\u30FC\u3092\u958B\u304F\u30B7\u30E7\u30FC\u30C8\u30AB\u30C3\u30C8\u3067\u3059\u3002",
                    ["Language"] = "\u8A00\u8A9E",
                    ["LanguageDescription"] = "Clipton \u306E\u8868\u793A\u8A00\u8A9E\u3092\u9078\u629E\u3057\u307E\u3059\u3002",
                    ["LanguageSystem"] = "\u30B7\u30B9\u30C6\u30E0\u8A2D\u5B9A\u3092\u4F7F\u7528",
                    ["Theme"] = "\u30C6\u30FC\u30DE",
                    ["ThemeDescription"] = "\u30A6\u30A3\u30F3\u30C9\u30A6\u306E\u8272\u30C6\u30FC\u30DE\u3092\u9078\u629E\u3057\u307E\u3059\u3002",
                    ["ThemeSystem"] = "\u30B7\u30B9\u30C6\u30E0\u8A2D\u5B9A\u3092\u4F7F\u7528",
                    ["ThemeLight"] = "\u30E9\u30A4\u30C8",
                    ["ThemeDark"] = "\u30C0\u30FC\u30AF"
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
