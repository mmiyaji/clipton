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

        Assert.Equal("Settings", catalog.Translate("fr", "Settings"));
    }

    [Fact]
    public void Translate_ReturnsKeyWhenMissingFromAllLocales()
    {
        var catalog = new LocalizationCatalog();

        Assert.Equal("MissingKey", catalog.Translate("ja", "MissingKey"));
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
        Assert.Contains("update available", catalog.Translate("en", "AboutUpdateAvailableTooltip"));
        Assert.Contains("更新あり", catalog.Translate("ja", "AboutUpdateAvailableTooltip"));
        Assert.Contains("Microsoft Store", catalog.Translate("en", "StoreUpdateDescription"));
        Assert.Contains("Microsoft Store", catalog.Translate("ja", "StoreUpdateDescription"));
    }
}
