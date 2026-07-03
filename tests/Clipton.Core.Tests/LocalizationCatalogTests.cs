using Clipton.Core;

namespace Clipton.Core.Tests;

public sealed class LocalizationCatalogTests
{
    [Fact]
    public void Translate_ReturnsJapaneseResource()
    {
        var catalog = new LocalizationCatalog();

        Assert.Equal("設定", catalog.Translate("ja", "Settings"));
    }

    [Fact]
    public void Translate_FallsBackToEnglish()
    {
        var catalog = new LocalizationCatalog();

        Assert.Equal("Settings", catalog.Translate("it", "Settings"));
    }

    [Fact]
    public void Translate_ReturnsKeyWhenMissingFromAllLocales()
    {
        var catalog = new LocalizationCatalog();

        Assert.Equal("MissingKey", catalog.Translate("ja", "MissingKey"));
    }

    [Fact]
    public void SupportedLocales_IncludeAdditionalLanguages()
    {
        var locales = LocalizationCatalog.SupportedLocales.Select(locale => locale.Code).ToArray();

        Assert.Contains("de", locales);
        Assert.Contains("es", locales);
        Assert.Contains("fr", locales);
        Assert.Contains("ko", locales);
        Assert.Contains("zh-Hans", locales);
    }

    [Theory]
    [InlineData("de-DE", "de")]
    [InlineData("es-MX", "es")]
    [InlineData("fr-CA", "fr")]
    [InlineData("ko-KR", "ko")]
    [InlineData("zh-CN", "zh-Hans")]
    [InlineData("system", "system")]
    [InlineData("unknown", "en")]
    public void NormalizeLocale_MapsSupportedCultureNames(string locale, string expected)
    {
        Assert.Equal(expected, LocalizationCatalog.NormalizeLocale(locale));
    }

    [Fact]
    public void Translate_ReturnsAdditionalLanguageResources()
    {
        var catalog = new LocalizationCatalog();

        Assert.Equal("Einstellungen", catalog.Translate("de-DE", "Settings"));
        Assert.Equal("Configuración", catalog.Translate("es", "Settings"));
        Assert.Equal("Paramètres", catalog.Translate("fr-FR", "Settings"));
        Assert.Equal("설정", catalog.Translate("ko-KR", "Settings"));
        Assert.Equal("设置", catalog.Translate("zh-CN", "Settings"));
    }

    [Fact]
    public void SupportedLocales_ProvideEveryEnglishResourceKey()
    {
        var resources = GetResources();
        var englishKeys = resources["en"].Keys.Order(StringComparer.Ordinal).ToArray();

        foreach (var locale in LocalizationCatalog.SupportedLocales.Select(locale => locale.Code))
        {
            var localeKeys = resources[locale].Keys.Order(StringComparer.Ordinal).ToArray();
            Assert.Equal(englishKeys, localeKeys);
        }
    }

    [Fact]
    public void SupportedLocales_PreserveFormattingPlaceholders()
    {
        var resources = GetResources();
        var english = resources["en"];

        foreach (var locale in LocalizationCatalog.SupportedLocales.Select(locale => locale.Code).Where(locale => locale != "en"))
        {
            foreach (var (key, englishValue) in english)
            {
                var localizedValue = resources[locale][key];
                Assert.Equal(ExtractPlaceholders(englishValue), ExtractPlaceholders(localizedValue));
            }
        }
    }

    [Fact]
    public void Translate_LanguageNamesKeepEnglishFallbackLabels()
    {
        var catalog = new LocalizationCatalog();

        Assert.Contains("(German)", catalog.Translate("ja", "LanguageGerman"));
        Assert.Contains("(English)", catalog.Translate("ja", "LanguageEnglish"));
        Assert.Contains("(Japanese)", catalog.Translate("ja", "LanguageJapanese"));
        Assert.Contains("(German)", catalog.Translate("de", "LanguageGerman"));
        Assert.Contains("(Spanish)", catalog.Translate("de", "LanguageSpanish"));
        Assert.Contains("(Spanish)", catalog.Translate("es", "LanguageSpanish"));
        Assert.Contains("(French)", catalog.Translate("es", "LanguageFrench"));
        Assert.Contains("(French)", catalog.Translate("fr", "LanguageFrench"));
        Assert.Contains("(Korean)", catalog.Translate("fr", "LanguageKorean"));
        Assert.Contains("(Korean)", catalog.Translate("ko", "LanguageKorean"));
        Assert.Contains("(Chinese, Simplified)", catalog.Translate("ko", "LanguageChineseSimplified"));
        Assert.Contains("(English)", catalog.Translate("zh-CN", "LanguageEnglish"));
        Assert.Contains("(Chinese, Simplified)", catalog.Translate("zh-CN", "LanguageChineseSimplified"));
    }

    [Fact]
    public void Translate_MaskDescriptionsClarifyPreviewOnlyBehavior()
    {
        var catalog = new LocalizationCatalog();

        Assert.Contains("list previews", catalog.Translate("en", "MaskSensitiveContentDescription"));
        Assert.Contains("not removed from stored clipboard data", catalog.Translate("en", "MaskDefinitionsDescription"));
    }

    [Fact]
    public void Translate_DisablePersistHistoryMessageMentionsVolatileDataDeletion()
    {
        var catalog = new LocalizationCatalog();

        var message = catalog.Translate("en", "ConfirmDisablePersistHistoryMessage");

        Assert.Contains("current in-memory history", message);
        Assert.Contains("temporary paste files", message);
    }

    [Fact]
    public void Translate_ExportConfirmationWarnsAboutUnencryptedFiles()
    {
        var catalog = new LocalizationCatalog();

        Assert.Contains("does not encrypt", catalog.Translate("en", "ConfirmExportHistoryMessage"));
        Assert.Contains("clipboard text", catalog.Translate("en", "ConfirmExportHistoryMessage"));
        Assert.Contains("does not encrypt", catalog.Translate("en", "ConfirmExportSnippetsMessage"));
        Assert.Contains("registered snippet", catalog.Translate("en", "ConfirmExportSnippetsMessage"));
    }

    [Fact]
    public void Translate_ProvidesQuickEditLabels()
    {
        var catalog = new LocalizationCatalog();

        Assert.Equal("Edit and paste", catalog.Translate("en", "EditAndPaste"));
        Assert.Equal("Quick edit", catalog.Translate("en", "QuickEdit"));
        Assert.Equal("Paste edited", catalog.Translate("en", "PasteEdited"));
        Assert.Equal("編集して貼り付け", catalog.Translate("ja", "EditAndPaste"));
    }

    [Fact]
    public void Translate_ProvidesStoreUpdateLabels()
    {
        var catalog = new LocalizationCatalog();

        Assert.Equal("Store updates", catalog.Translate("en", "StoreUpdate"));
        Assert.Equal("Store 更新", catalog.Translate("ja", "StoreUpdate"));
        Assert.Equal("Store page", catalog.Translate("en", "StorePage"));
        Assert.Equal("公式 Store ページ", catalog.Translate("ja", "StorePage"));
        Assert.Equal("Checking...", catalog.Translate("en", "CheckingForUpdates"));
        Assert.Equal("確認中...", catalog.Translate("ja", "CheckingForUpdates"));
        Assert.Equal("Check again", catalog.Translate("en", "CheckAgainForUpdates"));
        Assert.Equal("もう一度確認", catalog.Translate("ja", "CheckAgainForUpdates"));
        Assert.Contains("update available", catalog.Translate("en", "AboutUpdateAvailableTooltip"));
        Assert.Contains("更新あり", catalog.Translate("ja", "AboutUpdateAvailableTooltip"));
        Assert.Equal("Open changelog", catalog.Translate("en", "OpenChangelog"));
        Assert.Equal("CHANGELOG を開く", catalog.Translate("ja", "OpenChangelog"));
        Assert.Contains("Microsoft Store", catalog.Translate("en", "StoreUpdateDescription"));
        Assert.Contains("Microsoft Store", catalog.Translate("ja", "StoreUpdateDescription"));
        Assert.Contains("{0}", catalog.Translate("en", "StoreUpdateStatusNotAvailable"));
        Assert.Contains("最新", catalog.Translate("ja", "StoreUpdateStatusNotAvailable"));
    }

    [Fact]
    public void Translate_ProvidesAdvancedSearchStateLabels()
    {
        var catalog = new LocalizationCatalog();

        Assert.Contains("hidden", catalog.Translate("en", "AdvancedSearchCollapsed"));
        Assert.Contains("shown", catalog.Translate("en", "AdvancedSearchExpanded"));
    }

    [Fact]
    public void Translate_ProvidesSnippetManagementLabels()
    {
        var catalog = new LocalizationCatalog();

        Assert.Equal("Search snippets", catalog.Translate("en", "SnippetSearchPlaceholder"));
        Assert.Contains("{0}", catalog.Translate("en", "SnippetSearchStatus"));
        Assert.Contains("{0}", catalog.Translate("ja", "SnippetSearchStatus"));
        Assert.Contains("name and text", catalog.Translate("en", "SnippetValidationRequired"));
        Assert.NotEqual("SnippetPreviewPlaceholder", catalog.Translate("ja", "SnippetPreviewPlaceholder"));
    }

    [Fact]
    public void Translate_ProvidesExcludedCaptureApplicationLabels()
    {
        var catalog = new LocalizationCatalog();

        Assert.Equal("Excluded apps", catalog.Translate("en", "ExcludedCaptureApplications"));
        Assert.Contains("process names", catalog.Translate("en", "ExcludedCaptureApplicationsDescription"));
        Assert.Contains("{0}", catalog.Translate("en", "ExcludedCaptureApplicationsSaved"));
        Assert.Contains("{0}", catalog.Translate("ja", "ExcludedCaptureApplicationsSaved"));
        Assert.Equal("Edit list", catalog.Translate("en", "ExcludedCaptureApplicationsExpand"));
        Assert.NotEqual("ExcludedCaptureApplicationsCollapse", catalog.Translate("ja", "ExcludedCaptureApplicationsCollapse"));
    }

    [Fact]
    public void Translate_ProvidesQuickMenuPasteOptionAccessibilityLabels()
    {
        var catalog = new LocalizationCatalog();

        Assert.Contains("Right", catalog.Translate("en", "QuickMenuPasteOptionsHelp"));
        Assert.Contains("{0}", catalog.Translate("en", "QuickMenuPasteOptionsButtonName"));
        Assert.Contains("{0}", catalog.Translate("ja", "QuickMenuPasteOptionsButtonName"));
        Assert.Equal("Paste resized image (50%)", catalog.Translate("en", "PasteImageResizeHalf"));
        Assert.NotEqual("PasteImageResizeHalf", catalog.Translate("ja", "PasteImageResizeHalf"));
    }

    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> GetResources()
    {
        var field = typeof(LocalizationCatalog).GetField("_resources", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("_resources field was not found.");
        return (IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>)field.GetValue(new LocalizationCatalog())!;
    }

    private static string[] ExtractPlaceholders(string value)
    {
        return System.Text.RegularExpressions.Regex.Matches(value, @"\{\d+\}")
            .Select(match => match.Value)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }
}
